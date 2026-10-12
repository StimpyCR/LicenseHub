using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using LicenseHub.Api.Administration;
using LicenseHub.Api.Integration;
using LicenseHub.Application.Integration;
using LicenseHub.Infrastructure.Integration;
using LicenseHub.Application.Administration;
using LicenseHub.Api.Health;
using LicenseHub.Application.Availability;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Administration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var bootstrapMode = args.Contains("--bootstrap-admin", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--bootstrap-admin").ToArray());
builder.Host.UseSerilog((_, configuration) => configuration.MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning).WriteTo.Console());
var connectionFile = builder.Configuration["ConnectionStrings:LicenseHubFile"];
var connection = (string.IsNullOrWhiteSpace(connectionFile) ? null : File.ReadAllText(connectionFile).Trim())
    ?? builder.Configuration.GetConnectionString("LicenseHub")
    ?? throw new InvalidOperationException("Configura ConnectionStrings__LicenseHub fuera del código fuente.");
builder.Services.AddDbContext<LicenseHubDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<IStorageAvailability, PostgresAvailability>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISuperAdminBootstrap, SuperAdminBootstrap>();
builder.Services.AddScoped<IAdminAuthentication, AdminAuthentication>();
builder.Services.AddScoped<IIntegrationAuthentication, IntegrationAuthentication>();
builder.Services.AddScoped<IApplicationAccess, ApplicationAccess>();
builder.Services.AddScoped<IApplicationManagement, ApplicationManagement>();
builder.Services.AddScoped<IAuthorizationHandler, ApplicationCapabilityHandler>();
builder.Services.AddScoped<AdminSessionCookieEvents>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddIdentity<AdminUser, IdentityRole<Guid>>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 14;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<LicenseHubDbContext>().AddDefaultTokenProviders();
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, IntegrationAuthenticationHandler>(
        IntegrationAuthenticationHandler.SchemeName, _ => { })
    .AddCookie(AdminAuthEndpoints.ChallengeScheme, options =>
{
    options.Cookie.Name = "__Host-LicenseHubChallenge";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
});
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-LicenseHub";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.EventsType = typeof(AdminSessionCookieEvents);
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; },
        OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; }
    };
});
builder.Services.AddAuthorization(options =>
{
    var adminPolicy = new AuthorizationPolicyBuilder(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser().RequireClaim("licensehub:mfa", "verified").Build();
    options.FallbackPolicy = adminPolicy;
    options.AddPolicy("AdminSession", adminPolicy);
    foreach (var scope in new[] { IntegrationScopes.Activate, IntegrationScopes.Read, IntegrationScopes.Warnings })
        options.AddPolicy(scope, new AuthorizationPolicyBuilder(IntegrationAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser().RequireClaim(IntegrationAuthenticationHandler.ScopeClaim, scope).Build());
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-LicenseHubCsrf";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    if (!context.ProblemDetails.Extensions.ContainsKey("code"))
        context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
        {
            400 => "validation_failed",
            401 => "authentication_required",
            403 => "forbidden",
            404 => "not_found",
            429 => "rate_limited",
            503 => "service_unavailable",
            _ => "internal_error"
        };
});
builder.Services.AddHealthChecks().AddCheck<StorageHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("AdminLogin", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("AdminMfa", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Demasiadas solicitudes",
            extensions: new Dictionary<string, object?> { ["code"] = "rate_limited", ["traceId"] = context.HttpContext.TraceIdentifier })
            .ExecuteAsync(context.HttpContext);
    };
});
var trustedProxies = builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var proxy in trustedProxies)
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    options.ForwardLimit = 1;
});
var protection = builder.Services.AddDataProtection().SetApplicationName("LicenseHub");
var keyPath = builder.Configuration["DataProtection:KeyPath"];
var production = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
if (production && (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(certificatePath)))
    throw new InvalidOperationException("Producción requiere almacenamiento persistente y certificado de cifrado para DataProtection.");
if (builder.Environment.IsEnvironment("Testing")) protection.UseEphemeralDataProtectionProvider();
else
{
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath ?? Path.Combine(builder.Environment.ContentRootPath, ".keys")));
    if (!string.IsNullOrWhiteSpace(certificatePath))
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath,
            builder.Configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            throw new InvalidOperationException("El certificado de DataProtection debe tener clave privada y estar vigente.");
        protection.ProtectKeysWithCertificate(certificate);
    }
}
if (builder.Configuration.GetValue<bool>("Jobs:Enabled"))
{
    builder.Services.AddHangfire(configuration => configuration.UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings().UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connection),
            new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = false }));
    builder.Services.AddHangfireServer();
}
var app = builder.Build();
if (bootstrapMode)
{
    Environment.ExitCode = await BootstrapCommand.RunAsync(app.Services, app.Configuration, CancellationToken.None);
    await app.DisposeAsync();
    return;
}
// Sin proxy explícito no se procesa ningún encabezado reenviado.
if (trustedProxies.Length > 0)
    app.UseWhen(context => context.Connection.RemoteIpAddress is not null, branch => branch.UseForwardedHeaders());
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHsts();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/admin/auth"))
    {
        var bodySize = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = 8192;
    }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    if (!context.Request.IsHttps && !context.Request.Path.StartsWithSegments("/health"))
    {
        await Results.Problem(statusCode: 400, title: "HTTPS obligatorio",
            extensions: new Dictionary<string, object?> { ["code"] = "https_required", ["traceId"] = context.TraceIdentifier })
            .ExecuteAsync(context);
        return;
    }
    await next(context);
});
app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapAdminAuth();
app.MapApplicationManagement();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = registration => registration.Tags.Contains("ready") }).AllowAnonymous();
// Los endpoints de negocio se implementan por fases; no hay respuestas simuladas.
if (app.Environment.IsDevelopment())
    app.MapGet("/openapi/v1.yaml", () => Results.File(Path.Combine(AppContext.BaseDirectory, "contracts", "licensehub-v1.yaml"), "application/yaml"))
        .AllowAnonymous();
app.Run();

public partial class Program;

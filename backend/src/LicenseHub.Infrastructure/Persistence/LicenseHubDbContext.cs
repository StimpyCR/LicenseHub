using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LicenseHub.Infrastructure.Persistence;

public sealed class LicenseHubDbContext(DbContextOptions<LicenseHubDbContext> options)
    : IdentityDbContext<AdminUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<AdminUser>().Property(x => x.AccessVersion).IsConcurrencyToken();
        var adminChallenges = model.Entity<AdminChallengeRow>();
        adminChallenges.ToTable("AdminAuthChallenges");
        adminChallenges.HasKey(x => x.Id);
        adminChallenges.Property(x => x.SecurityStamp).HasMaxLength(256);
        adminChallenges.HasIndex(x => x.ExpiresAt);
        adminChallenges.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        var adminSessions = model.Entity<AdminSessionRow>();
        adminSessions.ToTable("AdminSessions");
        adminSessions.HasKey(x => x.Id);
        adminSessions.Property(x => x.SecurityStamp).HasMaxLength(256);
        adminSessions.HasIndex(x => x.ExpiresAt);
        adminSessions.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        var adminMfaUses = model.Entity<AdminMfaUseRow>();
        adminMfaUses.ToTable("AdminMfaUses");
        adminMfaUses.HasKey(x => new { x.UserId, x.TokenHash });
        adminMfaUses.Property(x => x.TokenHash).HasMaxLength(64);
        adminMfaUses.HasIndex(x => x.ExpiresAt);
        adminMfaUses.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        var apps = model.Entity<ApplicationRow>();
        apps.ToTable("Applications");
        apps.HasKey(x => x.Id);
        apps.HasIndex(x => x.Slug).IsUnique();
        apps.Property(x => x.Name).HasMaxLength(120);
        apps.Property(x => x.Slug).HasMaxLength(64);
        apps.Property(x => x.Version).IsConcurrencyToken();
        var credentials = Tenant<ApplicationCredentialRow>(model, "ApplicationCredentials");
        credentials.Property(x => x.SecretHash).HasMaxLength(64);
        credentials.HasIndex(x => x.SecretHash).IsUnique();
        credentials.HasIndex(x => new { x.ApplicationId, x.Id });
        var customers = Tenant<CustomerRow>(model, "Customers");
        customers.HasIndex(x => new { x.ApplicationId, x.SubjectId }).IsUnique();
        customers.Property(x => x.SubjectId).HasMaxLength(128);
        customers.Property(x => x.DisplayName).HasMaxLength(200);
        customers.Property(x => x.Email).HasMaxLength(254);
        var licenses = Tenant<LicenseRow>(model, "Licenses");
        licenses.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        licenses.Property(x => x.KeyHash).HasMaxLength(64);
        licenses.Property(x => x.StartMode).HasMaxLength(16);
        licenses.Property(x => x.DurationUnit).HasMaxLength(16);
        licenses.HasIndex(x => x.KeyHash).IsUnique();
        licenses.HasIndex(x => new { x.ApplicationId, x.State, x.ExpiresAt, x.Id });
        licenses.HasIndex(x => new { x.ApplicationId, x.CustomerId }).IsUnique()
            .HasFilter("\"ActivatedAt\" IS NOT NULL AND \"State\" IN ('Active','Suspended')");
        licenses.HasOne<CustomerRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CustomerId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        licenses.ToTable(t =>
        {
            t.HasCheckConstraint("CK_License_Lifetime", "NOT \"IsLifetime\" OR \"ExpiresAt\" IS NULL");
            t.HasCheckConstraint("CK_License_Time", "\"ExpiresAt\" IS NULL OR (\"StartsAt\" IS NOT NULL AND \"ExpiresAt\" > \"StartsAt\")");
            t.HasCheckConstraint("CK_License_Duration", "(\"DurationValue\" IS NULL AND \"DurationUnit\" IS NULL) OR (\"DurationValue\" IS NOT NULL AND \"DurationUnit\" IS NOT NULL AND \"DurationValue\" > 0 AND \"DurationUnit\" IN ('Minute','Hour','Day','Month','Year'))");
            t.HasCheckConstraint("CK_License_State", "\"State\" IN ('Pending','Active','Suspended','Expired','Revoked','Cancelled')");
            t.HasCheckConstraint("CK_License_StartMode", "\"StartMode\" IN ('OnIssue','OnActivation')");
            t.HasCheckConstraint("CK_License_Active", "\"State\" <> 'Active' OR (\"ActivatedAt\" IS NOT NULL AND (\"IsLifetime\" OR \"ExpiresAt\" IS NOT NULL))");
        });
        var activations = Tenant<LicenseActivationRow>(model, "LicenseActivations");
        activations.HasIndex(x => x.LicenseId).IsUnique();
        activations.Property(x => x.SubjectId).HasMaxLength(128);
        activations.HasOne<LicenseRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.LicenseId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        activations.HasOne<CustomerRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CustomerId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var features = Tenant<ApplicationFeatureRow>(model, "ApplicationFeatures");
        features.Property(x => x.Code).HasMaxLength(64);
        features.Property(x => x.Name).HasMaxLength(120);
        features.HasAlternateKey(x => new { x.ApplicationId, x.Code });
        var licenseFeatures = Tenant<LicenseFeatureRow>(model, "LicenseFeatures");
        licenseFeatures.Property(x => x.FeatureCode).HasMaxLength(64);
        licenseFeatures.HasIndex(x => new { x.LicenseId, x.FeatureCode }).IsUnique();
        licenseFeatures.HasOne<LicenseRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.LicenseId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        licenseFeatures.HasOne<ApplicationFeatureRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.FeatureCode })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Code }).OnDelete(DeleteBehavior.Restrict);
        var payments = Tenant<PaymentRow>(model, "Payments");
        payments.Property(x => x.Amount).HasPrecision(19, 4);
        payments.Property(x => x.Currency).HasMaxLength(3);
        payments.Property(x => x.State).HasMaxLength(16);
        payments.Property(x => x.Reference).HasMaxLength(120);
        payments.Property(x => x.RenewalIntentJson).HasColumnType("jsonb");
        payments.HasIndex(x => new { x.ApplicationId, x.Reference }).IsUnique();
        payments.HasIndex(x => new { x.ApplicationId, x.State, x.CreatedAt, x.Id });
        payments.HasOne<LicenseRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.LicenseId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        payments.HasOne<CustomerRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CustomerId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        payments.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Payment_Amount", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_Payment_State", "\"State\" IN ('Pending','Confirmed','Rejected','Refunded')");
        });
        var renewals = Tenant<LicenseRenewalRow>(model, "LicenseRenewals");
        renewals.Property(x => x.Reason).HasMaxLength(1000);
        renewals.HasIndex(x => new { x.ApplicationId, x.OperationId }).IsUnique();
        renewals.HasIndex(x => x.PaymentId).IsUnique().HasFilter("\"PaymentId\" IS NOT NULL");
        renewals.HasOne<LicenseRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.LicenseId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        renewals.HasOne<PaymentRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.PaymentId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        renewals.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var settings = Tenant<NotificationSettingsRow>(model, "NotificationSettings");
        settings.HasIndex(x => x.ApplicationId).IsUnique();
        var templates = Tenant<NotificationTemplateRow>(model, "NotificationTemplates");
        templates.Property(x => x.EventType).HasMaxLength(32);
        templates.Property(x => x.Locale).HasMaxLength(5);
        templates.Property(x => x.Subject).HasMaxLength(200);
        templates.Property(x => x.Body).HasMaxLength(20000);
        templates.HasIndex(x => new { x.ApplicationId, x.EventType, x.Locale }).IsUnique();
        var logs = Tenant<NotificationLogRow>(model, "NotificationLogs");
        logs.HasIndex(x => new { x.LicenseId, x.EntitlementVersion, x.EventType, x.OffsetSeconds, x.Channel }).IsUnique();
        logs.HasIndex(x => new { x.ApplicationId, x.CustomerId, x.ScheduledAt, x.Id });
        logs.Property(x => x.EventType).HasMaxLength(32);
        logs.Property(x => x.State).HasMaxLength(16);
        logs.Property(x => x.Channel).HasMaxLength(16);
        logs.Property(x => x.ProviderId).HasMaxLength(256);
        logs.Property(x => x.LastErrorRedacted).HasMaxLength(1000);
        logs.HasOne<LicenseRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.LicenseId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        logs.HasOne<CustomerRow>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CustomerId })
            .HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var outbox = Tenant<NotificationOutboxRow>(model, "NotificationOutbox");
        outbox.Property(x => x.PayloadJson).HasColumnType("jsonb");
        outbox.Property(x => x.EventType).HasMaxLength(64);
        outbox.HasIndex(x => x.CreatedAt).HasFilter("\"ProcessedAt\" IS NULL");
        outbox.HasIndex(x => new { x.ApplicationId, x.AggregateId, x.AggregateVersion, x.EventType }).IsUnique();
        var idempotency = Tenant<IdempotencyRow>(model, "IdempotencyRecords");
        idempotency.Property(x => x.PrincipalId).HasMaxLength(128);
        idempotency.Property(x => x.Operation).HasMaxLength(128);
        idempotency.Property(x => x.RequestHash).HasMaxLength(64);
        idempotency.Property(x => x.State).HasMaxLength(16);
        idempotency.Property(x => x.ResponseJson).HasColumnType("jsonb");
        idempotency.HasIndex(x => new { x.ApplicationId, x.PrincipalId, x.Operation, x.Key }).IsUnique();
        idempotency.HasIndex(x => x.RetainUntil);
        var grants = model.Entity<ApplicationUserGrantRow>();
        grants.ToTable("ApplicationUserGrants");
        grants.HasKey(x => new { x.UserId, x.ApplicationId, x.Capability });
        grants.Property(x => x.Capability).HasMaxLength(64);
        grants.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        grants.HasOne<ApplicationRow>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        var audit = model.Entity<AuditLogRow>();
        audit.ToTable("AuditLogs");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.ActorId).HasMaxLength(128);
        audit.Property(x => x.Action).HasMaxLength(100);
        audit.Property(x => x.Reason).HasMaxLength(1000);
        audit.Property(x => x.CorrelationId).HasMaxLength(128);
        audit.HasIndex(x => new { x.ApplicationId, x.CreatedAt, x.Id });
        audit.HasOne<ApplicationRow>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
    }

    private static EntityTypeBuilder<T> Tenant<T>(ModelBuilder model, string table) where T : class, ITenantRow
    {
        var entity = model.Entity<T>();
        entity.ToTable(table);
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.ApplicationId, x.Id });
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasOne<ApplicationRow>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        return entity;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareWrites()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<AuditLogRow>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("La auditoría es append-only.");
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var version = entry.Metadata.FindProperty("Version");
            if (version is not null)
            {
                var property = entry.Property("Version");
                property.CurrentValue = entry.State == EntityState.Added ? 1L : checked((long)property.OriginalValue! + 1);
            }
        }
    }
}

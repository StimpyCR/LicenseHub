using System.Security.Cryptography;
using LicenseHub.Domain.Licenses;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LicenseHub.IntegrationTests;

public sealed class PostgresSchemaTests
{
    [Fact]
    public async Task DatabaseRejectsCrossApplicationActivationAndDuplicateRedemption()
    {
        var connection = Environment.GetEnvironmentVariable("LICENSEHUB_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Prueba requiere PostgreSQL real: configura LICENSEHUB_TEST_CONNECTION.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (parsed.Database?.EndsWith("_tests", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("La base de integración debe terminar en _tests.");
        await using var db = new LicenseHubDbContext(new DbContextOptionsBuilder<LicenseHubDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var a = new ApplicationRow { Id = Guid.NewGuid(), Name = "Test A", Slug = Guid.NewGuid().ToString("N"), IsActive = true, CreatedAt = now };
        var b = new ApplicationRow { Id = Guid.NewGuid(), Name = "Test B", Slug = Guid.NewGuid().ToString("N"), IsActive = true, CreatedAt = now };
        var customerA = new CustomerRow { Id = Guid.NewGuid(), ApplicationId = a.Id, SubjectId = "test-business", CreatedAt = now };
        var customerB = new CustomerRow { Id = Guid.NewGuid(), ApplicationId = b.Id, SubjectId = "test-business", CreatedAt = now };
        var license = new LicenseRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = a.Id,
            CustomerId = customerA.Id,
            KeyHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            State = LicenseState.Pending,
            StartMode = "OnActivation",
            DurationUnit = "Minute",
            DurationValue = 2,
            CreatedAt = now
        };
        db.AddRange(a, b, customerA, customerB, license);
        await db.SaveChangesAsync();
        var wrongApp = new LicenseActivationRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = b.Id,
            LicenseId = license.Id,
            CustomerId = customerB.Id,
            SubjectId = customerB.SubjectId,
            ActivatedAt = now,
            CreatedAt = now
        };
        db.Add(wrongApp);
        var crossApp = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(crossApp.InnerException).SqlState);
        db.Entry(wrongApp).State = EntityState.Detached;
        var activation = new LicenseActivationRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = a.Id,
            LicenseId = license.Id,
            CustomerId = customerA.Id,
            SubjectId = customerA.SubjectId,
            ActivatedAt = now,
            CreatedAt = now
        };
        db.Add(activation);
        await db.SaveChangesAsync();
        db.Add(new LicenseActivationRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = a.Id,
            LicenseId = license.Id,
            CustomerId = customerA.Id,
            SubjectId = customerA.SubjectId,
            ActivatedAt = now,
            CreatedAt = now
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        // Todo el fixture se revierte; no se borra ni reutiliza una base de producción.
        await transaction.RollbackAsync();
    }
}

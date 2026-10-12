namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class AdminMfaUseRow
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

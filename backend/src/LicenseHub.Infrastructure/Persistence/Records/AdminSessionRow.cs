namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class AdminSessionRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string SecurityStamp { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

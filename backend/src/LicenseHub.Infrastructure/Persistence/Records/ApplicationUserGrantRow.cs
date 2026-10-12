namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class ApplicationUserGrantRow
{
    public Guid UserId { get; set; }
    public Guid ApplicationId { get; set; }
    public required string Capability { get; set; }
}

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class AuditLogRow
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }
    public required string ActorId { get; set; }
    public required string Action { get; set; }
    public Guid? EntityId { get; set; }
    public string? Reason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

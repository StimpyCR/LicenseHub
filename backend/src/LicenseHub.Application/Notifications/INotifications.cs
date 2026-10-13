namespace LicenseHub.Application.Notifications;

public interface IEmailSender
{
    Task<string> SendAsync(EmailMessage message, CancellationToken ct);
}
public sealed record EmailMessage(Guid Id, string Recipient, string Subject, string Body);
public sealed class EmailDeliveryException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public interface INotificationWorker
{
    Task ReconcileAsync(CancellationToken ct);
    Task DispatchAsync(CancellationToken ct);
}
public sealed record WarningView(Guid Id, string Code, string Severity, string Message, DateTime CreatedAt, Guid LicenseId);
public sealed record WarningPage(WarningView[] Items, string? NextCursor);
public interface IWarningQueries
{
    Task<WarningPage> ListAsync(Guid applicationId, string subjectId, string? cursor, int pageSize, CancellationToken ct);
}

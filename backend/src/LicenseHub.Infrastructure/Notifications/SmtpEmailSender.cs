using System.Net;
using System.Net.Mail;
using LicenseHub.Application.Notifications;

namespace LicenseHub.Infrastructure.Notifications;

// Secrets have no generated ToString and must be loaded from a private configuration source.
public sealed class SmtpOptions
{
    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public string? From { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool AllowUnencryptedDevelopment { get; init; }
}

public sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task<string> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Host) || !MailAddress.TryCreate(options.From, out var from))
            throw new EmailDeliveryException("mail_not_configured");
        using var mail = new MailMessage(from, new MailAddress(message.Recipient)) { Subject = message.Subject, Body = message.Body, IsBodyHtml = false };
        var providerId = message.Id.ToString("N") + "@licensehub.invalid";
        mail.Headers.Add("Message-ID", "<" + providerId + ">");
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = !options.AllowUnencryptedDevelopment, UseDefaultCredentials = false };
        if (options.Username is not null) client.Credentials = new NetworkCredential(options.Username, options.Password);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await client.SendMailAsync(mail, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new EmailDeliveryException("delivery_timeout"); }
        return providerId;
    }
}

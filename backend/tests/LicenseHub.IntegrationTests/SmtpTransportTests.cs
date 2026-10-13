using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using LicenseHub.Application.Notifications;
using LicenseHub.Infrastructure.Notifications;

namespace LicenseHub.IntegrationTests;

public sealed class SmtpTransportTests
{
    [Fact]
    public async Task RealSmtpTransportDeliversToLocalTestServerAndTlsRequirementRejectsPlainServer()
    {
        Assert.True(await ExchangeAsync(allowUnencrypted: true));
        Assert.False(await ExchangeAsync(allowUnencrypted: false));
    }

    private static async Task<bool> ExchangeAsync(bool allowUnencrypted)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = false;
        var messageId = Guid.NewGuid();
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 localhost test SMTP");
            while (await reader.ReadLineAsync(deadline.Token) is { } command)
            {
                if (command.StartsWith("EHLO") || command.StartsWith("HELO")) await writer.WriteLineAsync("250 localhost");
                else if (command.StartsWith("MAIL FROM") || command.StartsWith("RCPT TO") || command.StartsWith("RSET")) await writer.WriteLineAsync("250 OK");
                else if (command == "DATA")
                {
                    await writer.WriteLineAsync("354 End with dot");
                    var content = new StringBuilder();
                    while (await reader.ReadLineAsync(deadline.Token) is { } line && line != ".") content.AppendLine(line);
                    Assert.Contains(messageId.ToString("N") + "@licensehub.invalid", content.ToString());
                    Assert.DoesNotContain("text/html", content.ToString());
                    received = true;
                    await writer.WriteLineAsync("250 accepted");
                }
                else if (command == "QUIT") { await writer.WriteLineAsync("221 Bye"); break; }
                else await writer.WriteLineAsync("500 unsupported");
            }
        }, deadline.Token);
        try
        {
            var sender = new SmtpEmailSender(new() { Host = "127.0.0.1", Port = port, From = "sender@example.invalid", AllowUnencryptedDevelopment = allowUnencrypted });
            var mail = new EmailMessage(messageId, "customer@example.invalid", "Prueba", "Mensaje de prueba.");
            if (allowUnencrypted) Assert.Equal(messageId.ToString("N") + "@licensehub.invalid", await sender.SendAsync(mail, deadline.Token));
            else await Assert.ThrowsAsync<SmtpException>(() => sender.SendAsync(mail, deadline.Token));
            await server.WaitAsync(deadline.Token);
            return received;
        }
        finally { listener.Stop(); }
    }
}

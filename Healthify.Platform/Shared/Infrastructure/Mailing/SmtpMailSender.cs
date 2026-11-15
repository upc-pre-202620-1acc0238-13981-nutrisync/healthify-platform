using Healthify.Platform.Shared.Domain.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Healthify.Platform.Shared.Infrastructure.Mailing;

/// <summary><see cref="IMailSender" /> over SMTP with MailKit.</summary>
/// <remarks>
///     Reads the same configuration as the password reset email of Iam: <c>Smtp:Host</c>, <c>Smtp:Port</c> (587),
///     <c>Smtp:Username</c>, <c>Smtp:Password</c>, <c>Smtp:From</c> and <c>Smtp:UseSsl</c> (false: STARTTLS when the
///     server offers it). Without <c>Smtp:Host</c> or <c>Smtp:From</c> nothing is sent.
/// </remarks>
public class SmtpMailSender(IConfiguration configuration, ILogger<SmtpMailSender> logger) : IMailSender
{
    private const int DefaultPort = 587;

    public bool IsConfigured
    {
        get
        {
            var smtp = configuration.GetSection("Smtp");
            return IsSet(smtp["Host"]) && IsSet(smtp["From"]);
        }
    }

    public async Task<bool> SendHtmlAsync(string toAddress, string? toName, string subject, string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("SMTP is not configured. An email was not sent.");
            return false;
        }

        var smtp = configuration.GetSection("Smtp");
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp["From"]!));
        message.To.Add(string.IsNullOrWhiteSpace(toName)
            ? MailboxAddress.Parse(toAddress)
            : new MailboxAddress(toName, toAddress));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        var port = int.TryParse(smtp["Port"], out var p) ? p : DefaultPort;
        var useSsl = bool.TryParse(smtp["UseSsl"], out var s) && s;
        var username = smtp["Username"];

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp["Host"]!, port,
            useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            cancellationToken);
        if (IsSet(username))
            await client.AuthenticateAsync(username, smtp["Password"] ?? string.Empty, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return true;
    }

    /// <summary>Empty, or a <c>%PLACEHOLDER%</c> of appsettings.Production.json that no variable replaced.</summary>
    private static bool IsSet([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !(value.StartsWith('%') && value.EndsWith('%'));
    }
}

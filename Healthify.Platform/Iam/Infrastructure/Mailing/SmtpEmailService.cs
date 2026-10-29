using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Healthify.Platform.Iam.Infrastructure.Mailing;

/// <summary><see cref="IEmailService" /> over SMTP with MailKit.</summary>
/// <remarks>
///     Configuration: <c>Smtp:Host</c>, <c>Smtp:Port</c> (587), <c>Smtp:Username</c>, <c>Smtp:Password</c>,
///     <c>Smtp:From</c> and <c>Smtp:UseSsl</c> (false: STARTTLS when the server offers it).
///     Without <c>Smtp:Host</c> or <c>Smtp:From</c> (local development) nothing is sent: in Development the code is
///     written to the log so the flow can still be tried; in any other environment only a warning is.
/// </remarks>
public class SmtpEmailService(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<SmtpEmailService> logger) : IEmailService
{
    private const int DefaultPort = 587;

    public async Task SendPasswordResetCodeAsync(Email to, string code, TimeSpan validFor,
        PreferredLanguage language, CancellationToken cancellationToken = default)
    {
        var smtp = configuration.GetSection("Smtp");
        var host = smtp["Host"];
        var from = smtp["From"];

        if (!IsSet(host) || !IsSet(from))
        {
            if (environment.IsDevelopment())
                logger.LogWarning("SMTP is not configured. Password reset code for {Email}: {Code}", to.Value, code);
            else
                logger.LogWarning("SMTP is not configured. The password reset email for {Email} was not sent.",
                    to.Value);
            return;
        }

        var minutes = (int)Math.Round(validFor.TotalMinutes);
        var (subject, html) = language.Value == PreferredLanguage.English
            ? ("Your Healthify password reset code", EnglishBody(code, minutes))
            : ("Tu código para restablecer la contraseña de Healthify", SpanishBody(code, minutes));

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to.Value));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

        var port = int.TryParse(smtp["Port"], out var p) ? p : DefaultPort;
        var useSsl = bool.TryParse(smtp["UseSsl"], out var s) && s;
        var username = smtp["Username"];

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port,
            useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        if (IsSet(username))
            await client.AuthenticateAsync(username, smtp["Password"] ?? string.Empty, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>Empty, or a <c>%PLACEHOLDER%</c> of appsettings.Production.json that no variable replaced.</summary>
    private static bool IsSet([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !(value.StartsWith('%') && value.EndsWith('%'));
    }

    private static string SpanishBody(string code, int minutes)
    {
        return Body("Restablece tu contraseña",
            "Escribe este código en la app de Healthify para elegir una contraseña nueva.",
            code,
            $"El código vence en {minutes} minutos y solo se puede usar una vez.",
            "Si no pediste cambiar tu contraseña, ignora este correo: tu cuenta sigue igual.");
    }

    private static string EnglishBody(string code, int minutes)
    {
        return Body("Reset your password",
            "Enter this code in the Healthify app to choose a new password.",
            code,
            $"The code expires in {minutes} minutes and can only be used once.",
            "If you didn't ask to change your password, ignore this email: your account stays as it is.");
    }

    private static string Body(string title, string intro, string code, string expiry, string ignore)
    {
        return $"""
                <div style="font-family:sans-serif;max-width:480px;margin:auto">
                  <h2>{title}</h2>
                  <p>{intro}</p>
                  <p style="font-size:32px;font-weight:700;letter-spacing:8px;margin:24px 0">{code}</p>
                  <p>{expiry}</p>
                  <p style="margin-top:24px;color:#6b7280;font-size:13px">{ignore}</p>
                </div>
                """;
    }
}

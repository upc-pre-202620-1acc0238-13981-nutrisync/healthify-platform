namespace Healthify.Platform.Shared.Domain.Services;

/// <summary>
///     Sends one HTML email. A technical port shared by every bounded context that needs to write to a person
///     (the appointment reminders of Monitoring and Adherence today); the adapter lives in the infrastructure layer.
/// </summary>
public interface IMailSender
{
    /// <summary>
    ///     False when no SMTP server is configured (local development, or an environment without the
    ///     <c>Smtp__*</c> variables). Callers use it to skip a cycle instead of failing once per recipient.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Delivers the message.</summary>
    /// <param name="toAddress">The recipient address.</param>
    /// <param name="toName">The recipient display name, if known.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="htmlBody">The HTML body.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <returns>False when nothing was sent because the sender is not configured.</returns>
    /// <exception cref="Exception">When the SMTP server refuses or cannot be reached.</exception>
    Task<bool> SendHtmlAsync(string toAddress, string? toName, string subject, string htmlBody,
        CancellationToken cancellationToken = default);
}

using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.Shared.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Mailing;

/// <summary><see cref="IFollowUpReminderNotifier" /> that renders the template and hands it to the mail sender.</summary>
public class SmtpFollowUpReminderNotifier(IMailSender mailSender, IConfiguration configuration)
    : IFollowUpReminderNotifier
{
    public bool IsConfigured => mailSender.IsConfigured;

    public async Task<bool> SendAsync(FollowUpReminderMessage message, CancellationToken cancellationToken = default)
    {
        var settings = FollowUpReminderSettings.From(configuration);
        var (subject, html) = FollowUpReminderEmailTemplate.Render(message, settings.PracticeAddress,
            settings.VideoCallUrl);
        return await mailSender.SendHtmlAsync(message.RecipientEmail, message.RecipientName, subject, html,
            cancellationToken);
    }
}

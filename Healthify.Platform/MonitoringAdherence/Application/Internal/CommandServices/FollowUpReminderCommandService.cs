using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;

/// <summary>
///     Reads the visits that are near, asks <see cref="FollowUpReminderPlanner" /> which reminders are due, delivers
///     them and records each one right after it was sent.
/// </summary>
/// <remarks>
///     Each delivery is committed on its own, so a crash in the middle of a cycle never makes the next cycle send
///     again what already went out. A delivery that fails is not recorded and is tried again in the next cycle.
///     Email addresses are never written to the log.
/// </remarks>
public class FollowUpReminderCommandService(
    IFollowUpReminderRepository reminderRepository,
    IIamContextFacade iamContextFacade,
    IFollowUpReminderNotifier notifier,
    IFollowUpLocalClock localClock,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<FollowUpReminderCommandService> logger) : IFollowUpReminderCommandService
{
    private const string DefaultLanguage = "es";

    public async Task<FollowUpReminderCycleResult> Handle(SendDueFollowUpRemindersCommand command,
        CancellationToken cancellationToken = default)
    {
        var settings = FollowUpReminderSettings.From(configuration);
        if (!settings.Enabled || settings.LeadMinutes.Count == 0 || !notifier.IsConfigured)
            return FollowUpReminderCycleResult.Idle;

        var horizon = command.Now + TimeSpan.FromMinutes(settings.LeadMinutes.Max());
        var visits = await reminderRepository.ListScheduledBetweenAsync(command.Now, horizon, settings.BatchSize,
            cancellationToken);
        if (visits.Count == 0) return FollowUpReminderCycleResult.Idle;

        var logs = await reminderRepository.ListLogsAsync(visits.Select(v => v.Id.Value).ToList(),
            cancellationToken);
        var planned = FollowUpReminderPlanner.Plan(
            visits.Select(v => new ReminderCandidate(v.Id.Value, v.ScheduledFor,
                v.ScheduledAt ?? DateTimeOffset.MinValue, v.IsScheduled)),
            settings.LeadMinutes, settings.NotifyPractitioner, command.Now,
            logs.Select(l => new SentReminder(l.FollowUpId, l.Recipient, l.LeadMinutes, l.VisitMoment)).ToList());
        if (planned.Count == 0) return new FollowUpReminderCycleResult(visits.Count, 0, 0);

        var byId = visits.ToDictionary(v => v.Id.Value);
        var userIds = planned.SelectMany(p => new[] { byId[p.FollowUpId].PatientId, byId[p.FollowUpId].PractitionerId })
            .Distinct().ToList();
        var users = await iamContextFacade.GetUsersByIds(userIds, cancellationToken);
        var languages = new Dictionary<int, string>();

        var sent = 0;
        var failed = 0;
        foreach (var reminder in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var visit = byId[reminder.FollowUpId];
            var toPatient = reminder.Recipient == Domain.Model.ValueObjects.ReminderRecipient.Patient;
            var recipientId = toPatient ? visit.PatientId : visit.PractitionerId;
            if (!users.TryGetValue(recipientId, out var recipient) ||
                !users.TryGetValue(visit.PatientId, out var patient) ||
                !users.TryGetValue(visit.PractitionerId, out var practitioner) ||
                string.IsNullOrWhiteSpace(recipient.Email))
            {
                logger.LogWarning("Reminder for follow up {FollowUpId} skipped: the account of {Recipient} was not found",
                    reminder.FollowUpId, reminder.Recipient);
                failed++;
                continue;
            }

            if (!languages.TryGetValue(recipientId, out var language))
            {
                language = await iamContextFacade.GetPreferredLanguage(recipientId, cancellationToken) ??
                           DefaultLanguage;
                languages[recipientId] = language;
            }

            var message = new FollowUpReminderMessage(
                reminder.Recipient, recipient.Email, recipient.FullName, language, patient.FullName,
                practitioner.FullName, localClock.ToLocal(visit.ScheduledFor), visit.Modality.Value,
                visit.Preparation.Select(p => p.Value).ToList(), reminder.LeadMinutes, reminder.Rescheduled);

            try
            {
                if (!await notifier.SendAsync(message, cancellationToken)) continue;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reminder for follow up {FollowUpId} to the {Recipient} was not delivered",
                    reminder.FollowUpId, reminder.Recipient);
                failed++;
                continue;
            }

            try
            {
                await reminderRepository.AddLogAsync(
                    new FollowUpReminderLog(reminder.FollowUpId, reminder.Recipient, reminder.LeadMinutes,
                        reminder.VisitMoment, DateTimeOffset.UtcNow), cancellationToken);
                await unitOfWork.CompleteAsync(cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Delivered but not recorded: the next cycle may send it again, which is better than losing it.
                logger.LogError(ex, "Reminder for follow up {FollowUpId} was sent but could not be recorded",
                    reminder.FollowUpId);
                failed++;
            }
        }

        return new FollowUpReminderCycleResult(visits.Count, sent, failed);
    }
}

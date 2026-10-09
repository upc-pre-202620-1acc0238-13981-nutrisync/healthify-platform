using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Alert Escalated To Practitioner" (Nutritional Care, Subflow 3.7).
/// </summary>
/// <remarks>
///     Integration event 13 of 13, and the second place where the automation ends on purpose.
///     Business rule: Escalation Notifies Never Modifies The Plan (Monitoring and Adherence, Subflow
///     5.8), and Signal Notifies Never Modifies The Plan (Subflow 3.7). Same shape as the other
///     signal handler and for the same reason: one service, one command, an item in an inbox.
///     By the time this event exists the patient has already been shown the alert and asked about it,
///     because the aggregate on the other side refuses to escalate otherwise. The evidence on the
///     event says when that happened, so the practitioner opening the item can see it. The patient
///     knows this escalation exists and is told before it happens; this is not covert surveillance.
/// </remarks>
public class OnAlertEscalatedToPractitionerHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAlertEscalatedToPractitionerHandler> logger)
    : IEventHandler<AlertEscalatedToPractitioner>
{
    public async Task Handle(AlertEscalatedToPractitioner notification,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IReviewItemCommandService>();

        var clinicalDate = scope.ServiceProvider.GetService<IClinicalDateProvider>();

        var result = await commandService.Handle(
            new OpenReviewItemCommand(notification.PatientId, SignalType.ConsistencyEscalation,
                notification.Evidence, EvidenceDataOf(notification, clinicalDate)), cancellationToken);

        if (result.IsFailure)
            logger.LogInformation(
                "No review item was opened for the escalated consistency alert of patient {PatientId}",
                notification.PatientId);
    }

    /// <summary>
    ///     X-2. The evidence as numbers too, like NC-11 for a sustained deviation, so the client words it in the
    ///     practitioner's language. Dates are days of the practice's calendar. Null when the producer predates X-2 or
    ///     the numbers do not hold together: the English text evidence still opens the item.
    /// </summary>
    private static ReviewItemEvidenceDto? EvidenceDataOf(AlertEscalatedToPractitioner notification,
        IClinicalDateProvider? clinicalDate)
    {
        try
        {
            var evidence = ReviewItemEvidence.FromConsistencyEscalation(notification.Value, notification.State,
                DayOf(notification.AlertSinceAt, clinicalDate), notification.WeeksInAlert,
                DayOf(notification.ShownToPatientAt, clinicalDate));
            return evidence is null
                ? null
                : new ReviewItemEvidenceDto(null, null, null, null,
                    ConsistencyKgPerWeek: evidence.ConsistencyKgPerWeek,
                    ConsistencyState: evidence.ConsistencyState,
                    AlertSinceOn: evidence.AlertSinceOn,
                    WeeksInAlert: evidence.WeeksInAlert,
                    ShownToPatientOn: evidence.ShownToPatientOn);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static DateOnly? DayOf(DateTimeOffset? instant, IClinicalDateProvider? clinicalDate)
    {
        if (instant is null) return null;
        return clinicalDate?.DateOf(instant.Value) ?? DateOnly.FromDateTime(instant.Value.UtcDateTime);
    }
}

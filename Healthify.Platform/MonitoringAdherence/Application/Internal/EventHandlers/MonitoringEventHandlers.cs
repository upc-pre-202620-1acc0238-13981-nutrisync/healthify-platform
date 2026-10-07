using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Care Link Established" (Monitoring and Adherence, Subflow 5.1).
/// </summary>
/// <remarks>
///     Integration event 1 of 13. Interpretation starts when the relationship does: there is nothing
///     to compare against before somebody agreed to be looked after.
/// </remarks>
public class OnCareLinkEstablishedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnCareLinkEstablishedHandler> logger) : IEventHandler<CareLinkEstablished>
{
    public async Task Handle(CareLinkEstablished notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();
        var queryService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowQueryService>();

        var openWindow = await queryService.Handle(
            new GetCurrentEvaluationWindowByPatientIdQuery(notification.PatientId), cancellationToken);

        // Same link: the window is already open. A repeated event opens nothing, so the policy is
        // idempotent.
        if (openWindow is not null && openWindow.CareLinkId == notification.CareLinkId) return;

        // CR-1: a window of a previous link is still open, because its Care Link Revoked has not
        // arrived yet or its handler failed. The new link supersedes it, so it is closed here with
        // the same command the revocation policy uses. The outcome no longer depends on the order of
        // the two events.
        if (openWindow is not null)
        {
            // TODO: ambiguity - Care Link Established carries no timestamp, so the moment the
            // previous window is superseded is taken as the server's current time.
            var closed = await commandService.Handle(
                new CloseEvaluationWindowCommand(notification.PatientId, DateTimeOffset.UtcNow,
                    openWindow.CareLinkId), cancellationToken);

            if (closed.IsFailure)
                logger.LogWarning(
                    "Could not close the evaluation window of care link {PreviousCareLinkId} for patient {PatientId}",
                    openWindow.CareLinkId, notification.PatientId);
        }

        var result = await commandService.Handle(
            new OpenEvaluationWindowCommand(notification.PatientId, notification.CareLinkId),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not open an evaluation window for patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Active Targets Updated" (Monitoring and Adherence, Subflow 5.2).
/// </summary>
/// <remarks>
///     Integration event 5 of 13. The same published contract feeds three policies, and this is the
///     one that freezes it: from here on, the days evaluated under this version keep these numbers
///     whatever happens to the plan afterwards.
///     What arrives is targets, guidelines and restrictions. There is no diagnosis in the payload and
///     no calculation basis, and this context has nowhere to put either.
/// </remarks>
public class OnActiveTargetsUpdatedMonitoringHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnActiveTargetsUpdatedMonitoringHandler> logger) : IEventHandler<ActiveTargetsUpdated>
{
    public async Task Handle(ActiveTargetsUpdated notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();

        var result = await commandService.Handle(
            new SnapshotActiveTargetsCommand(
                notification.PatientId,
                notification.PlanVersion,
                notification.ValidFrom,
                notification.DailyTargets.EnergyKcal,
                notification.DailyTargets.ProteinG,
                notification.DailyTargets.CarbG,
                notification.DailyTargets.FatG),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not snapshot the active targets of patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Clinical Measurement Taken" (Monitoring and Adherence, Subflow 5.3).
/// </summary>
/// <remarks>
///     Integration event 3 of 13. The reading travels with its source because the two weight series
///     are never merged: this one was taken by a professional under a recorded protocol, and the
///     other one is somebody standing on a bathroom scale.
/// </remarks>
public class OnClinicalMeasurementTakenHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnClinicalMeasurementTakenHandler> logger) : IEventHandler<ClinicalMeasurementTaken>
{
    public async Task Handle(ClinicalMeasurementTaken notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();

        var result = await commandService.Handle(
            new AppendAnthropometryPointCommand(notification.PatientId, notification.WeightKg,
                notification.TakenAt), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not append an anthropometry point for patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Meal Logged" (Monitoring and Adherence, Subflow 5.4).
/// </summary>
/// <remarks>
///     Integration event 7 of 13. The day evaluated is the day the patient declared on their own
///     device, never the server day: a meal logged at nine in the evening in Lima belongs to that
///     evening.
/// </remarks>
public class OnMealLoggedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnMealLoggedHandler> logger) : IEventHandler<MealLogged>
{
    public async Task Handle(MealLogged notification, CancellationToken cancellationToken)
    {
        // MA-1: the entry was confirmed in the request that created it, and the Estimate Confirmed
        // By Patient of that same request evaluates the day once, with its final totals. Evaluating
        // here as well would only repeat the work.
        if (notification.ConfirmedOnCreation) return;

        // MA-1: an entry that arrived in a synchronisation batch is evaluated with its day when the
        // batch announces it (Diary Batch Synchronized), once per day rather than once per entry.
        if (notification.ViaSynchronization) return;

        await MonitoringDayEvaluation.Evaluate(scopeFactory, logger, notification.PatientId,
            notification.LocalTimestamp, cancellationToken);
    }
}

/// <summary>
///     Policy "When Estimate Confirmed By Patient" (Monitoring and Adherence, Subflow 5.4).
/// </summary>
/// <remarks>
///     Integration event 8 of 13. A confirmation is what turns a proposal into something the patient
///     said, and only what the patient said is ever counted as intake, so the day is read again here.
/// </remarks>
public class OnEstimateConfirmedByPatientHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnEstimateConfirmedByPatientHandler> logger) : IEventHandler<EstimateConfirmedByPatient>
{
    public async Task Handle(EstimateConfirmedByPatient notification, CancellationToken cancellationToken)
    {
        // IN-6: an entry of a meal logged in a group; Meal Group Logged evaluates the day once for the whole meal.
        if (!notification.EvaluatesDay) return;

        await MonitoringDayEvaluation.Evaluate(scopeFactory, logger, notification.PatientId,
            notification.LocalTimestamp, cancellationToken);
    }
}

/// <summary>
///     Policy "When Meal Group Logged" (Monitoring and Adherence, Subflow 5.4, IN-6).
/// </summary>
/// <remarks>
///     A meal logged in a group («Registrar esta comida» from an idea) is several confirmed entries of one moment.
///     Their own confirmations leave the day to this policy, which evaluates it once, with the final totals.
/// </remarks>
public class OnMealGroupLoggedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnMealGroupLoggedHandler> logger) : IEventHandler<MealGroupLogged>
{
    public async Task Handle(MealGroupLogged notification, CancellationToken cancellationToken)
    {
        await MonitoringDayEvaluation.Evaluate(scopeFactory, logger, notification.PatientId,
            notification.LocalTimestamp, cancellationToken);
    }
}

/// <summary>
///     Policy "When Off Plan Entry Logged" (Monitoring and Adherence, Subflow 5.4).
/// </summary>
/// <remarks>
///     Integration event 9 of 13. An off-plan entry makes the day a logged day and nothing else. It
///     carries no detail, so it moves no total, and nothing anywhere computes a deviation from the
///     fact that somebody declared one: making that declaration cost nothing is the entire point of
///     it existing.
///     Kept for the legacy one-tap entries (IN-1 deprecated the event); an off-plan meal logged with
///     a food reaches the day through Meal Logged and Estimate Confirmed By Patient like any other.
/// </remarks>
public class OnOffPlanEntryLoggedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnOffPlanEntryLoggedHandler> logger) : IEventHandler<OffPlanEntryLogged>
{
    public async Task Handle(OffPlanEntryLogged notification, CancellationToken cancellationToken)
    {
        // MA-1: a legacy entry from a synchronisation batch is evaluated with its batch.
        if (notification.ViaSynchronization) return;

        await MonitoringDayEvaluation.Evaluate(scopeFactory, logger, notification.PatientId,
            notification.LocalTimestamp, cancellationToken);
    }
}

/// <summary>
///     Policy "When Entry Synchronized" (Monitoring and Adherence, Subflow 5.5).
/// </summary>
/// <remarks>
///     Integration event 11 of 13. Business rule: Late Entry Re Evaluates Its Own Day Only. The date
///     handed over is the one the entry declared on the device, which may be days old, and it is the
///     only day that is touched.
/// </remarks>
public class OnEntrySynchronizedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnEntrySynchronizedHandler> logger) : IEventHandler<EntrySynchronized>
{
    public async Task Handle(EntrySynchronized notification, CancellationToken cancellationToken)
    {
        // MA-1: inside a batch, Diary Batch Synchronized re-evaluates each day once.
        if (!notification.ReEvaluatesDay) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();

        var result = await commandService.Handle(
            new ReEvaluateWindowCommand(notification.PatientId,
                DateOnly.FromDateTime(notification.LocalTimestamp.Date)), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not re-evaluate the day of a synchronized entry for patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Diary Batch Synchronized" (Monitoring and Adherence, Subflow 5.5, MA-1).
/// </summary>
/// <remarks>
///     Business rule: Late Entry Re Evaluates Its Own Day Only. The days are the ones the entries
///     declared on the device, which may be days old, and each is re-evaluated exactly once however
///     many entries of the batch it holds.
///     Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Subflow 5.4). After
///     that, the days without entries before the most recent day of the batch are marked Unlogged,
///     as a live evaluation of that day would have done, so the series the practitioner and the
///     patient read shows the silence instead of skipping it. The order matters: the batch's own
///     days are evaluated first, so they are never marked Unlogged on the way. Days already
///     evaluated are never touched, and a later batch with an entry for a day marked Unlogged simply
///     re-evaluates it.
/// </remarks>
public class OnDiaryBatchSynchronizedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnDiaryBatchSynchronizedHandler> logger) : IEventHandler<DiaryBatchSynchronized>
{
    public async Task Handle(DiaryBatchSynchronized notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();

        if (notification.Days.Count == 0) return;

        foreach (var day in notification.Days.Distinct())
        {
            var result = await commandService.Handle(new ReEvaluateWindowCommand(notification.PatientId, day),
                cancellationToken);

            if (result.IsFailure)
                logger.LogDebug("Day {Date} of patient {PatientId} was not re-evaluated after synchronization",
                    day, notification.PatientId);
        }

        // Only once the batch's own days are evaluated, so that none of them is marked Unlogged.
        var marked = await commandService.Handle(
            new MarkUnloggedDaysBeforeCommand(notification.PatientId, notification.Days.Max()), cancellationToken);
        if (marked.IsFailure)
            logger.LogDebug("Unlogged days before a synchronized batch were not marked for patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Day Evaluated" (Monitoring and Adherence, Subflow 5.6). Internal to this context.
/// </summary>
/// <remarks>
///     Business rule: Never Evaluated Under Seven Days. The command this policy issues stops on its
///     own when the window is too young, which is why the policy can fire after every evaluated day
///     without producing a verdict on a Tuesday.
/// </remarks>
public class OnDayEvaluatedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnDayEvaluatedHandler> logger) : IEventHandler<DayEvaluated>
{
    public async Task Handle(DayEvaluated notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IDeviationCommandService>();

        var result = await commandService.Handle(new DetectDeviationCommand(notification.PatientId),
            cancellationToken);

        // A window that is too young, or a horizon with nothing logged in it, is the ordinary case
        // rather than a fault, so it is not reported as one.
        if (result is Result<Deviation, MonitoringError>.Failure
            {
                Error: not MonitoringError.InsufficientWindowLength
                and not MonitoringError.NoLoggedDays
                and not MonitoringError.EvaluationWindowNotFound
            })
            logger.LogWarning("Could not run deviation detection for patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Deviation Detected" (Monitoring and Adherence, Subflow 5.6). Internal.
/// </summary>
/// <remarks>
///     Business rule: Sustained If Persists Across Majority Of Window. This is the gate between an
///     observation and a signal. Only what comes out the far side of it ever reaches a clinical inbox.
/// </remarks>
public class OnDeviationDetectedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnDeviationDetectedHandler> logger) : IEventHandler<DeviationDetected>
{
    public async Task Handle(DeviationDetected notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IDeviationCommandService>();

        var result = await commandService.Handle(
            new FlagSustainedDeviationCommand(notification.DeviationId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not test deviation {DeviationId} against the majority rule",
                notification.DeviationId);
    }
}

/// <summary>
///     Policy "When Weight Trend Recalculated" (Monitoring and Adherence, Subflow 5.7).
/// </summary>
/// <remarks>
///     Integration event 10 of 13. The trend is one of the two series the index needs; the other is
///     the recorded intake this context already holds. Without both there is no index at all.
/// </remarks>
public class OnWeightTrendRecalculatedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnWeightTrendRecalculatedHandler> logger) : IEventHandler<WeightTrendRecalculated>
{
    public async Task Handle(WeightTrendRecalculated notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider
            .GetRequiredService<IConsistencyIndexCommandService>();

        var result = await commandService.Handle(
            new RecomputeConsistencyIndexCommand(notification.PatientId), cancellationToken);

        if (result.IsFailure)
            logger.LogDebug("The consistency index of patient {PatientId} was not recomputed",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Consistency Alert Raised" (Monitoring and Adherence, Subflow 5.7). Internal.
/// </summary>
/// <remarks>
///     Business rule: Patient First Always. This is the only handler of the alert. Nothing else
///     subscribes to it, so there is no path by which a practitioner learns about an alert the
///     patient has not been asked about first.
/// </remarks>
public class OnConsistencyAlertRaisedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnConsistencyAlertRaisedHandler> logger) : IEventHandler<ConsistencyAlertRaised>
{
    public async Task Handle(ConsistencyAlertRaised notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider
            .GetRequiredService<IConsistencyIndexCommandService>();

        var result = await commandService.Handle(new PromptPatientCommand(notification.PatientId),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not prompt patient {PatientId} about their consistency index",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Logging Gap Detected" (Monitoring and Adherence, Subflow 5.9). Internal.
/// </summary>
/// <remarks>
///     Business rules: Gap Never Escalates and Reminder Is Local And Non Accusatory. This is the only
///     handler of a logging gap in the whole platform, and all it does is remind the patient. There
///     is no second subscriber, in this context or any other, and that absence is the rule: absence
///     of data is not evidence of non-compliance and nothing here treats it as such.
/// </remarks>
public class OnLoggingGapDetectedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnLoggingGapDetectedHandler> logger) : IEventHandler<LoggingGapDetected>
{
    public async Task Handle(LoggingGapDetected notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();

        var result = await commandService.Handle(new RemindPatientCommand(notification.PatientId),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not remind patient {PatientId}", notification.PatientId);
    }
}

/// <summary>
///     Policy "When Care Link Revoked" (Monitoring and Adherence, Subflow 5.11).
/// </summary>
/// <remarks>
///     Integration event 2 of 13. The window stops counting and keeps everything it holds. A care
///     relationship that ended is still a care relationship that happened.
///     CR-4: when the patient switched practitioner (CR-1), the visits still ahead with the previous one are
///     cancelled too, as at a discharge.
/// </remarks>
public class OnCareLinkRevokedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnCareLinkRevokedHandler> logger) : IEventHandler<CareLinkRevoked>
{
    /// <summary>NOTE: the CR-1 reason as the event carries it, a plain string (RevocationReason stays in its context).</summary>
    public const string SwitchedPractitionerReason = "SwitchedPractitioner";

    public async Task Handle(CareLinkRevoked notification, CancellationToken cancellationToken)
    {
        await CloseWindow(notification, cancellationToken);

        if (string.Equals(notification.Reason, SwitchedPractitionerReason, StringComparison.OrdinalIgnoreCase))
            await TreatmentEnd.CancelFutureFollowUps(scopeFactory, logger, notification.PatientId,
                notification.PractitionerId, ScheduledFollowUp.SwitchedPractitionerReason, cancellationToken);
    }

    private async Task CloseWindow(CareLinkRevoked notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();

        var result = await commandService.Handle(
            new CloseEvaluationWindowCommand(notification.PatientId, notification.RevokedAt,
                notification.CareLinkId),
            cancellationToken);

        // Not finding an open window of this link is expected when Care Link Established for a new
        // link arrived first and already closed it (CR-1).
        if (result is Result<EvaluationWindow, MonitoringError>.Failure
            {
                Error: MonitoringError.EvaluationWindowNotFound
            })
            logger.LogInformation(
                "No open evaluation window of care link {CareLinkId} to close for patient {PatientId}",
                notification.CareLinkId, notification.PatientId);
        else if (result.IsFailure)
            logger.LogWarning("Could not close the evaluation window of patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     CR-4 - Policy "When Treatment Discharged" (Monitoring and Adherence). PR18 "Esto cierra el tratamiento con
///     este paciente".
/// </summary>
/// <remarks>
///     The evaluation window of the link stops counting (it keeps everything it holds) and the visits still ahead
///     with that practitioner are cancelled with reason "Discharged", so they leave the agenda (PR17.0-Alta).
///     Each step runs in its own scope: one failing does not stop the other, and neither throws.
/// </remarks>
public class OnTreatmentDischargedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnTreatmentDischargedHandler> logger) : IEventHandler<TreatmentDischarged>
{
    public async Task Handle(TreatmentDischarged notification, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var windows = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();
            var result = await windows.Handle(
                new CloseEvaluationWindowCommand(notification.PatientId, notification.DischargedAt,
                    notification.CareLinkId), cancellationToken);

            if (result is Result<EvaluationWindow, MonitoringError>.Failure
                {
                    Error: MonitoringError.EvaluationWindowNotFound
                })
                logger.LogInformation(
                    "No open evaluation window of care link {CareLinkId} to close for patient {PatientId}",
                    notification.CareLinkId, notification.PatientId);
            else if (result.IsFailure)
                logger.LogWarning("Could not close the evaluation window of discharged patient {PatientId}",
                    notification.PatientId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not close the evaluation window of discharged patient {PatientId}",
                notification.PatientId);
        }

        await TreatmentEnd.CancelFutureFollowUps(scopeFactory, logger, notification.PatientId,
            notification.PractitionerId, ScheduledFollowUp.DischargedReason, cancellationToken);
    }
}

/// <summary>CR-4. What the discharge and the switch of practitioner share: the visits still ahead are cancelled.</summary>
internal static class TreatmentEnd
{
    public static async Task CancelFutureFollowUps(IServiceScopeFactory scopeFactory, ILogger logger, int patientId,
        int practitionerId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var followUps = scope.ServiceProvider.GetRequiredService<IScheduledFollowUpCommandService>();
            var result = await followUps.Handle(
                new CancelFollowUpsForPatientCommand(patientId, reason, practitionerId), cancellationToken);

            if (result is Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>.Success { Value.Count: > 0 } done)
                logger.LogInformation("Cancelled {Count} future visits of patient {PatientId} ({Reason})",
                    done.Value.Count, patientId, reason);
            else if (result.IsFailure)
                logger.LogWarning("Could not cancel the future visits of patient {PatientId} ({Reason})", patientId,
                    reason);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not cancel the future visits of patient {PatientId} ({Reason})", patientId,
                reason);
        }
    }
}

/// <summary>
///     The body the three Subflow 5.4 policies share.
/// </summary>
/// <remarks>
///     They are three separate handlers because they are three separate policies in the model, and
///     collapsing them would hide which events this context actually listens to. What they do once
///     they have fired is the same thing, and it lives here rather than being written three times.
/// </remarks>
internal static class MonitoringDayEvaluation
{
    public static async Task Evaluate(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        int patientId,
        DateTimeOffset localTimestamp,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowCommandService>();

        // The day the patient was living when they logged, taken from the timestamp they declared.
        var date = DateOnly.FromDateTime(localTimestamp.Date);

        var result = await commandService.Handle(new EvaluateDayCommand(patientId, date),
            cancellationToken);

        if (result.IsFailure)
            logger.LogDebug("Day {Date} of patient {PatientId} was not evaluated", date, patientId);
    }
}

/// <summary>
///     MA-2. Policy "When Consultation Completed" (integration event of Nutritional Care, NC-2).
/// </summary>
/// <remarks>
///     The visit the consultation started from, or the visit of the same pair on the same local day, is
///     marked Completed, so the missed-visit policy does not flag a visit that happened. Only identifiers
///     and dates cross: the event carries no diagnosis and no calculation basis.
///     This runs inside the request that published the consultation (Cortex.Mediator awaits its handlers),
///     after that commit. It therefore never throws: a visit that could not be completed is logged, and the
///     publication the practitioner just made is not reported as failed because of it.
/// </remarks>
public class OnConsultationCompletedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnConsultationCompletedHandler> logger) : IEventHandler<ConsultationCompleted>
{
    public async Task Handle(ConsultationCompleted notification, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IScheduledFollowUpCommandService>();

            var result = await commandService.Handle(
                new CompleteFollowUpFromConsultationCommand(notification.PatientId, notification.PractitionerId,
                    notification.ConsultationId, notification.CompletedAt, notification.ScheduledFollowUpId),
                cancellationToken);

            if (result is Result<ScheduledFollowUp, MonitoringError>.Failure failure)
            {
                // A consultation without a visit on the calendar is ordinary (walk-in, first visit).
                if (failure.Error == MonitoringError.ScheduledFollowUpNotFound)
                    logger.LogInformation("Consultation {ConsultationId} matched no visit on the agenda",
                        notification.ConsultationId);
                else
                    logger.LogWarning("Could not complete the visit of consultation {ConsultationId}: {Error}",
                        notification.ConsultationId, failure.Error);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not complete the visit of consultation {ConsultationId}",
                notification.ConsultationId);
        }
    }
}

/// <summary>
///     Policy "When AI Processing Consent Withdrawn" (CR-2, §12-#14), this context's part: purge the AI content it
///     keeps for the patient (weekly summaries of IA-2, cached suggested questions of IA-4 and monitoring summaries of
///     IA-5).
/// </summary>
/// <remarks>
///     The technical audit (<c>ai_generations</c>) is purged by Care Relationship. Granting the consent again
///     generates nothing by itself: the next run of each function does. Never throws.
/// </remarks>
public class OnAiProcessingConsentChangedMonitoringHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAiProcessingConsentChangedMonitoringHandler> logger) : IEventHandler<AiProcessingConsentChanged>
{
    public async Task Handle(AiProcessingConsentChanged notification, CancellationToken cancellationToken)
    {
        if (notification.Granted) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IMonitoringAiContentCommandService>();
            var result = await commandService.Handle(
                new PurgeMonitoringAiContentCommand(notification.PatientId, true, true, true), cancellationToken);

            if (result is Result<int, MonitoringError>.Failure failure)
                logger.LogWarning("Could not purge the Monitoring AI content of patient {PatientId}: {Error}",
                    notification.PatientId, failure.Error);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not purge the Monitoring AI content of patient {PatientId}",
                notification.PatientId);
        }
    }
}

/// <summary>
///     IA-1. A patient function of this context that goes off (weekly summary, IA-2; suggested questions, IA-4) stops
///     using the diary, and what it generated is purged.
/// </summary>
/// <remarks>
///     The event carries the state after the change; purging what is already gone is a no-op. The monitoring
///     summary (IA-5) is the practitioner's and depends on the consent only, not on these preferences.
/// </remarks>
public class OnAiPreferencesChangedMonitoringHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAiPreferencesChangedMonitoringHandler> logger) : IEventHandler<AiPreferencesChanged>
{
    public async Task Handle(AiPreferencesChanged notification, CancellationToken cancellationToken)
    {
        if (notification is { WeeklySummaryEnabled: true, SuggestedQuestionsEnabled: true }) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IMonitoringAiContentCommandService>();
            var result = await commandService.Handle(
                new PurgeMonitoringAiContentCommand(notification.PatientId, !notification.WeeklySummaryEnabled,
                    !notification.SuggestedQuestionsEnabled, false), cancellationToken);

            if (result is Result<int, MonitoringError>.Failure failure)
                logger.LogWarning("Could not purge the Monitoring AI content of patient {PatientId}: {Error}",
                    notification.PatientId, failure.Error);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not purge the Monitoring AI content of patient {PatientId}",
                notification.PatientId);
        }
    }
}

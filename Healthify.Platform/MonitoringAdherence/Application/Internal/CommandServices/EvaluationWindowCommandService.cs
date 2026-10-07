using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;

/// <summary>
///     Subflows 5.1 to 5.5, 5.9 and 5.11.
/// </summary>
/// <remarks>
///     This is the only class in the platform that sets what was prescribed beside what was recorded.
///     It reads the recorded side through the published contract of the context that owns it, which
///     returns totals and counts and no opinion, and it reads the prescribed side from the snapshots
///     the window took at the time. Neither side is written from here.
///     Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Subflow 5.4). The
///     distinction is carried by <c>HasAnyEntry</c> on the intake summary, which is why the summary
///     reports it separately from the totals: a day that added up to little and a day nobody wrote in
///     are different facts, and only one of them is about eating.
/// </remarks>
public class EvaluationWindowCommandService(
    IEvaluationWindowRepository evaluationWindowRepository,
    IIntakeContextFacade intakeContextFacade,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<EvaluationWindowCommandService> logger,
    IMediator mediator) : IEvaluationWindowCommandService
{
    /// <summary>Subflow 5.1 - Open Evaluation Window.</summary>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(
        OpenEvaluationWindowCommand command, CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(MonitoringError.EvaluationWindowNotFound);

        var windowDays = ConfiguredWindowDays();

        // Business rule: Minimum Seven Day Window (Monitoring and Adherence, Subflow 5.1)
        if (windowDays < EvaluationWindow.MinimumDays)
            return Failure(MonitoringError.WindowShorterThanMinimum);

        try
        {
            // Business rule: One Open Window Per Patient (Monitoring and Adherence, Subflow 5.1)
            // TODO: race condition - MySQL has no partial index, so "at most one row with state Open
            // per patient" cannot be expressed as a unique index. Two Care Link Established events
            // for the same patient arriving at the same instant could both pass this check. Mitigated
            // by the rule upstream that a patient has at most one active care link at a time.
            if (await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                    cancellationToken) is not null)
                return Failure(MonitoringError.PatientAlreadyHasOpenWindow);

            // TODO: ambiguity - the event that opens a window carries no timestamp of its own, and
            // this context only ever learns a patient's local calendar from the timestamps declared
            // on their diary entries. Interpretation assumed: the window starts on the server's
            // current UTC date, while every day inside it is the local day the patient declared.
            // Source: event storming v3, section 5, Subflow 5.1.
            var from = DateOnly.FromDateTime(DateTime.UtcNow);

            var window = new EvaluationWindow(command, windowDays, from);

            await evaluationWindowRepository.AddAsync(window, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new EvaluationWindowOpened(window.Id.Value, window.PatientId, window.From, window.To),
                cancellationToken);

            return Success(window);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.WindowShorterThanMinimum);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open an evaluation window for patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.2 - Snapshot Active Targets.</summary>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(
        SnapshotActiveTargetsCommand command, CancellationToken cancellationToken = default)
    {
        TargetsSnapshot snapshot;
        try
        {
            snapshot = new TargetsSnapshot(command.PlanVersion, command.EnergyKcal, command.ProteinG,
                command.CarbG, command.FatG, command.ValidFrom);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.TargetsSnapshotMissing);
        }

        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            // Business rule: Later Adjustment Never Rewrites Evaluated Days (Subflow 5.2). The
            // aggregate appends and leaves the daily series alone; nothing here recalculates a day.
            if (!window.TakeSnapshot(snapshot)) return Success(window);

            evaluationWindowRepository.Update(window);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new TargetsSnapshotTaken(window.Id.Value, window.PatientId, snapshot.PlanVersion,
                    snapshot.TakenAt), cancellationToken);

            return Success(window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not snapshot the active targets of patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.3 - Append Anthropometry Point.</summary>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(
        AppendAnthropometryPointCommand command, CancellationToken cancellationToken = default)
    {
        AnthropometryPoint point;
        try
        {
            // Business rules: Clinical Measurement Outranks Self Weigh In and Two Series Never
            // Merged (Monitoring and Adherence, Subflow 5.3). This factory is the only way to build
            // a point, and it stamps the source itself.
            point = AnthropometryPoint.FromClinicalMeasurement(
                DateOnly.FromDateTime(command.TakenAt.UtcDateTime), command.WeightKg);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.UnexpectedError);
        }

        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            if (!window.AppendAnthropometryPoint(point)) return Success(window);

            evaluationWindowRepository.Update(window);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new AnthropometryPointAppended(window.Id.Value, window.PatientId, point.Date,
                    point.ValueKg), cancellationToken);

            return Success(window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not append an anthropometry point for patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.4 - Evaluate Day.</summary>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(EvaluateDayCommand command,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateSingleDay(command.PatientId, command.Date, false, cancellationToken);
    }

    /// <summary>Subflow 5.5 - Re Evaluate Window.</summary>
    /// <remarks>
    ///     Business rule: Late Entry Re Evaluates Its Own Day Only (Monitoring and Adherence,
    ///     Subflow 5.5). It runs the same single-day evaluation as Subflow 5.4 and differs only in
    ///     the event it publishes, because that is genuinely the whole difference: an entry that
    ///     arrives late is an entry, and the day it declares is the day it belongs to.
    /// </remarks>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(ReEvaluateWindowCommand command,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateSingleDay(command.PatientId, command.Date, true, cancellationToken);
    }

    /// <summary>Subflow 5.4, MA-1 - Mark Unlogged Days Before.</summary>
    /// <remarks>
    ///     The same filling of the silence that a live evaluation performs before its day
    ///     (<c>EvaluationWindow.MarkUnloggedDaysBefore</c>), for a synchronised batch whose days are
    ///     re-evaluated one by one. Idempotent: a second run finds nothing to mark and publishes nothing.
    /// </remarks>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(MarkUnloggedDaysBeforeCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Business rule: Closed Windows Never Reopened (Subflow 5.5). Only an open window is filled.
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            // Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Subflow 5.4)
            var unlogged = window.MarkUnloggedDaysBefore(command.Date);
            if (unlogged.Count == 0) return Success(window);

            evaluationWindowRepository.Update(window);
            await unitOfWork.CompleteAsync(cancellationToken);

            // As on the live path: only the daily indicator event, never Day Evaluated, because a day
            // nobody wrote in is not evidence of anything to detect.
            foreach (var quiet in unlogged)
                await mediator.PublishAsync(
                    new DailyComplianceComputed(window.Id.Value, window.PatientId, quiet.Date, quiet.Outcome),
                    cancellationToken);

            return Success(window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not mark unlogged days before {Date} for patient {PatientId}",
                command.Date, command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.9 - Flag Logging Gap.</summary>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(FlagLoggingGapCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            // TODO: hotspot (event storming 5, hotspot 2) - what counts as a logged day, and where
            // does the boundary between a deviation and a logging gap sit? Interpretation assumed:
            // one entry is enough for the day to count as logged, which is the reading that never
            // turns a thin day into an accusation. Parameter: Monitoring:LoggingGapThresholdDays.
            // Source: event storming v3, section 5, hotspot 2.
            var thresholdDays = configuration.GetValue<int?>("Monitoring:LoggingGapThresholdDays") ?? 3;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var lastLogged = window.LastLoggedDate();
            var since = lastLogged ?? window.From;
            var daysWithoutEntry = today.DayNumber - since.DayNumber;

            if (daysWithoutEntry < Math.Max(1, thresholdDays)) return Success(window);

            // Business rules: Gap Is Not A Deviation, Gap Excluded From Deviation Calculation and
            // Gap Never Escalates (Monitoring and Adherence, Subflow 5.9). Nothing below reaches the
            // deviation aggregate or the consistency index, and the event published has exactly one
            // subscriber, inside this context, which reminds the patient.
            if (!window.FlagLoggingGap(today)) return Success(window);

            evaluationWindowRepository.Update(window);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new LoggingGapDetected(window.PatientId, lastLogged, daysWithoutEntry),
                cancellationToken);

            return Success(window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not flag a logging gap for patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.9 - Remind Patient.</summary>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(RemindPatientCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            // Business rule: Reminder Is Local And Non Accusatory (Monitoring and Adherence,
            // Subflow 5.9). Local means it reaches the patient and nobody else: the event published
            // below has no subscriber in any other context, and the wording lives in the localized
            // message rather than in this file.
            if (!window.RemindPatient()) return Success(window);

            evaluationWindowRepository.Update(window);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new PatientReminded(window.PatientId, window.LastPatientRemindedAt!.Value),
                cancellationToken);

            return Success(window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not remind patient {PatientId}", command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.11 - Close Evaluation Window.</summary>
    public async Task<Result<EvaluationWindow, MonitoringError>> Handle(
        CloseEvaluationWindowCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            // CR-1: the open window belongs to another link, typically the new one after a switch of
            // practitioner whose Care Link Established arrived first. That window is not this
            // command's to close: the window of the revoked link was already closed when it was
            // superseded.
            if (command.CareLinkId is not null && window.CareLinkId != command.CareLinkId)
                return Failure(MonitoringError.EvaluationWindowNotFound);

            // Business rules: Closed Window Stops Counting Days and Evaluated Data Is Preserved
            // (Monitoring and Adherence, Subflow 5.11). Nothing here deletes a series or recalculates
            // one. The window stops counting and everything it holds stays readable.
            window.Close(command.RevokedAt);

            evaluationWindowRepository.Update(window);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new EvaluationWindowClosed(window.Id.Value, window.PatientId, command.RevokedAt),
                cancellationToken);

            return Success(window);
        }
        catch (InvalidOperationException)
        {
            return Failure(MonitoringError.WindowClosed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not close the evaluation window of patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>The whole of Subflows 5.4 and 5.5: one day in, one day out.</summary>
    private async Task<Result<EvaluationWindow, MonitoringError>> EvaluateSingleDay(
        int patientId, DateOnly date, bool isReEvaluation, CancellationToken cancellationToken)
    {
        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(patientId,
                cancellationToken);

            if (window is null)
            {
                // Business rule: Closed Windows Never Reopened (Monitoring and Adherence, Subflow
                // 5.5). A patient whose link was revoked has a closed window and no open one, and an
                // entry that arrives afterwards does not bring it back.
                var any = (await evaluationWindowRepository.ListByPatientIdAsync(patientId,
                    cancellationToken)).Any();

                if (!any) return Failure(MonitoringError.EvaluationWindowNotFound);

                return Failure(isReEvaluation
                    ? MonitoringError.ClosedWindowCannotBeReopened
                    : MonitoringError.WindowClosed);
            }

            // Business rule: Compared Against That Day Snapshot (Monitoring and Adherence, Subflow
            // 5.4). The targets used are the ones that were in force on the day being evaluated, not
            // the ones in force now. That is what stops a plan adjusted on Thursday from turning
            // Monday into a bad day.
            var snapshot = window.SnapshotInForceOn(date);
            if (snapshot is null) return Failure(MonitoringError.TargetsSnapshotMissing);

            var summary = await intakeContextFacade.GetDailyIntakeSummary(patientId, date,
                cancellationToken);
            if (summary is null) return Failure(MonitoringError.UnexpectedError);

            // Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Monitoring and
            // Adherence, Subflow 5.4). The decision uses whether the diary holds anything at all,
            // never the totals. A patient who logged three meals and confirmed none of the photo
            // estimates has recorded their day.
            var day = DailyCompliance.Evaluate(date, summary.HasAnyEntry, summary.EntryCount,
                summary.EnergyKcal, snapshot, summary.OffPlanEntryCount, summary.OffPlanEnergyKcal);

            // Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Monitoring and
            // Adherence, Subflow 5.4). The days between the last one that was evaluated and this one
            // are marked as unlogged, so the series shows the silence instead of skipping it. Not on
            // the re-evaluation path: a late entry re-evaluates its own day and nothing else.
            var unlogged = isReEvaluation ? [] : window.MarkUnloggedDaysBefore(date);

            var changed = window.RecordDayEvaluation(day);

            if (!changed && unlogged.Count == 0) return Failure(MonitoringError.DayAlreadyEvaluated);

            evaluationWindowRepository.Update(window);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Only the daily indicator event is published for a filled-in day. Day Evaluated is what
            // the deviation policy listens to, and a day nobody wrote in is not evidence of anything
            // to detect.
            foreach (var quiet in unlogged)
                await mediator.PublishAsync(
                    new DailyComplianceComputed(window.Id.Value, window.PatientId, quiet.Date,
                        quiet.Outcome), cancellationToken);

            if (!changed) return Success(window);

            await mediator.PublishAsync(
                new DayEvaluated(window.Id.Value, window.PatientId, day.Date, day.Outcome),
                cancellationToken);
            await mediator.PublishAsync(
                new DailyComplianceComputed(window.Id.Value, window.PatientId, day.Date, day.Outcome),
                cancellationToken);

            if (isReEvaluation)
                await mediator.PublishAsync(
                    new WindowReEvaluated(window.Id.Value, window.PatientId, day.Date),
                    cancellationToken);

            return Success(window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not evaluate {Date} for patient {PatientId}", date, patientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    private int ConfiguredWindowDays()
    {
        return configuration.GetValue<int?>("Monitoring:EvaluationWindowDays")
               ?? EvaluationWindow.MinimumDays;
    }

    private static Result<EvaluationWindow, MonitoringError> Success(EvaluationWindow window)
    {
        return new Result<EvaluationWindow, MonitoringError>.Success(window);
    }

    private static Result<EvaluationWindow, MonitoringError> Failure(MonitoringError error)
    {
        return new Result<EvaluationWindow, MonitoringError>.Failure(error);
    }
}

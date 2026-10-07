using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.MonitoringAdherence.Application.CommandServices;

/// <summary>
///     Subflows 5.1 to 5.5, 5.9 and 5.11. Every one of them is driven by a policy: none of these
///     commands has an endpoint, because nobody decides to evaluate a day.
/// </summary>
public interface IEvaluationWindowCommandService
{
    /// <summary>Subflow 5.1 - Open Evaluation Window.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(OpenEvaluationWindowCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.2 - Snapshot Active Targets.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(SnapshotActiveTargetsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.3 - Append Anthropometry Point.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(AppendAnthropometryPointCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.4 - Evaluate Day.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(EvaluateDayCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.5 - Re Evaluate Window. One day, and only that day.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(ReEvaluateWindowCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Subflow 5.4, MA-1 - Mark Unlogged Days Before. Fills the silence before a synchronised
    ///     batch; never touches a day already evaluated.
    /// </summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(MarkUnloggedDaysBeforeCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.9 - Flag Logging Gap. Never a deviation and never an escalation.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(FlagLoggingGapCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.9 - Remind Patient. Local, and to the patient alone.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(RemindPatientCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.11 - Close Evaluation Window.</summary>
    Task<Result<EvaluationWindow, MonitoringError>> Handle(CloseEvaluationWindowCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Subflow 5.6. Both commands are driven by internal policies; neither has an endpoint.</summary>
public interface IDeviationCommandService
{
    /// <summary>Subflow 5.6 - Detect Deviation.</summary>
    Task<Result<Deviation, MonitoringError>> Handle(DetectDeviationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.6 - Flag Sustained Deviation. The only deviation that ever leaves this context.</summary>
    Task<Result<Deviation, MonitoringError>> Handle(FlagSustainedDeviationCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Subflows 5.7 and 5.8.
/// </summary>
/// <remarks>
///     Invariant 2 of this bounded context: the patient is shown the index first, and the escalation
///     only happens after three weeks in Alert. Both halves are enforced in the aggregate rather than
///     here, so no caller can reorder them.
/// </remarks>
public interface IConsistencyIndexCommandService
{
    /// <summary>Subflow 5.7 - Recompute Consistency Index.</summary>
    Task<Result<ConsistencyIndex, MonitoringError>> Handle(RecomputeConsistencyIndexCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.7 - Prompt Patient.</summary>
    /// <summary>MA-7. Issued by the patient when the card was shown.</summary>
    Task<Result<ConsistencyIndex, MonitoringError>> Handle(AcknowledgeConsistencyPromptCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<ConsistencyIndex, MonitoringError>> Handle(PromptPatientCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.8 - Escalate To Practitioner. It notifies; it never modifies a plan.</summary>
    Task<Result<ConsistencyIndex, MonitoringError>> Handle(EscalateToPractitionerCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Subflow 5.10 - Record Referral. One of the two commands in this context a person issues.</summary>
public interface IReferralCommandService
{
    Task<Result<Referral, MonitoringError>> Handle(RecordReferralCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>RM-4. Issued by the practitioner who recorded the referral.</summary>
    Task<Result<Referral, MonitoringError>> Handle(CloseReferralCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Subflow 5.10 - Schedule Follow Up and Flag Missed Follow Up.</summary>
public interface IScheduledFollowUpCommandService
{
    /// <summary>Subflow 5.10 - Schedule Follow Up. The other command a person issues.</summary>
    Task<Result<ScheduledFollowUp, MonitoringError>> Handle(ScheduleFollowUpCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 5.10 - Flag Missed Follow Up. Time-driven, and it closes nothing.</summary>
    Task<Result<ScheduledFollowUp, MonitoringError>> Handle(FlagMissedFollowUpCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>MA-5. Issued by the practitioner whose agenda the visit is on.</summary>
    Task<Result<ScheduledFollowUp, MonitoringError>> Handle(CancelFollowUpCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>MA-5. Issued by the practitioner whose agenda the visit is on. The check in is kept.</summary>
    Task<Result<ScheduledFollowUp, MonitoringError>> Handle(RescheduleFollowUpCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>MA-2. Issued by the policy on Consultation Completed only.</summary>
    Task<Result<ScheduledFollowUp, MonitoringError>> Handle(CompleteFollowUpFromConsultationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     CR-4. Issued by the policies on Treatment Discharged and Care Link Revoked only. Returns the visits it
    ///     cancelled (none is a success).
    /// </summary>
    Task<Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>> Handle(CancelFollowUpsForPatientCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     MA-4 - Submit Pre Visit Check In. Issued by the patient; creates or edits the check in of a visit.
/// </summary>
/// <remarks>Business rule: Check In Raises No Signal (MA-4). It writes one row and reaches nothing else.</remarks>
public interface IPreVisitCheckInCommandService
{
    Task<Result<PreVisitCheckInView, MonitoringError>> Handle(SubmitPreVisitCheckInCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     IA-2 - Generate Weekly Summary (issued by <c>WeeklySummaryHostedService</c>) and the retention of the summaries.
/// </summary>
/// <remarks>
///     Business rule: The AI Does Not Count (IA-2). The figures come from MA-6 and IN-5 and the output is rejected
///     when its numbers are not those. Business rule: Summary Needs Three Logged Days (IA-2).
/// </remarks>
public interface IWeeklySummaryCommandService
{
    Task<Result<WeeklySummary, MonitoringAiFailure>> Handle(GenerateWeeklySummaryCommand command,
        CancellationToken cancellationToken = default);

    /// <returns>Summaries deleted.</returns>
    Task<Result<int, MonitoringError>> Handle(PurgeExpiredWeeklySummariesCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>IA-4 - Suggest Questions for the visit (PT25, PT25.2). Never receives the diagnosis.</summary>
public interface ISuggestedQuestionsCommandService
{
    Task<Result<SuggestedQuestionsView, MonitoringAiFailure>> Handle(SuggestQuestionsCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     IA-5 - Summarize Monitoring for the practitioner (PAC-2). Without the patient's AI consent it answers the
///     deterministic facts only (§12-#5).
/// </summary>
public interface IMonitoringSummaryCommandService
{
    Task<Result<MonitoringSummaryView, MonitoringAiFailure>> Handle(SummarizeMonitoringCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>§12-#14 - Purge the AI content this context generated for a patient.</summary>
public interface IMonitoringAiContentCommandService
{
    /// <returns>Weekly summaries and cache entries removed.</returns>
    Task<Result<int, MonitoringError>> Handle(PurgeMonitoringAiContentCommand command,
        CancellationToken cancellationToken = default);
}

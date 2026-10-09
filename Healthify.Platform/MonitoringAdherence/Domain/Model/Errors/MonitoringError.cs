namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;

/// <summary>
///     Every failure this bounded context can report. One value per business rule it enforces.
/// </summary>
/// <remarks>
///     Read the list for what is missing. There is no value here for a patient who did badly, ate too
///     much or stopped trying. This context interprets data and reports that it could not interpret
///     something; it never reports a person.
/// </remarks>
public enum MonitoringError
{
    EvaluationWindowNotFound,

    /// <summary>Rule: One Open Window Per Patient (Subflow 5.1).</summary>
    PatientAlreadyHasOpenWindow,

    /// <summary>Rule: Minimum Seven Day Window (Subflow 5.1), read from configuration.</summary>
    WindowShorterThanMinimum,

    /// <summary>The window has stopped counting days, so there is nothing to write to it.</summary>
    WindowClosed,

    /// <summary>Rule: Closed Windows Never Reopened (Subflow 5.5).</summary>
    ClosedWindowCannotBeReopened,

    /// <summary>Rule: Compared Against That Day Snapshot (Subflow 5.4). No targets, no comparison.</summary>
    TargetsSnapshotMissing,

    /// <summary>The day already said exactly this, so nothing changed and nothing was published.</summary>
    DayAlreadyEvaluated,

    /// <summary>Rule: Never Evaluated Under Seven Days (Subflow 5.6), and invariant 1.</summary>
    InsufficientWindowLength,

    /// <summary>Rule: Only Logged Days Count (Subflow 5.6). Silence is not evidence.</summary>
    NoLoggedDays,

    DeviationNotFound,

    /// <summary>Rules: Both Series Required and No Index Without Both Series (Subflow 5.7).</summary>
    BothSeriesRequired,

    /// <summary>
    ///     Rule: Alert Threshold (Subflow 5.7), which is the uncalibrated one. Reported when an
    ///     escalation is asked for while <c>Monitoring:ConsistencyAlertThreshold</c> is not set:
    ///     nothing is ever raised against a patient on the strength of a number nobody validated.
    /// </summary>
    ConsistencyThresholdNotConfigured,

    /// <summary>Rule: Patient Prompt Required Before Escalation (Subflow 5.8), and invariant 2.</summary>
    PatientPromptRequiredBeforeEscalation,

    /// <summary>Rule: Three Weeks In Alert Required (Subflow 5.8), and invariant 2.</summary>
    ThreeWeeksInAlertRequired,

    /// <summary>Rule: Active Care Link Required (Subflow 5.10).</summary>
    ActiveCareLinkRequired,

    /// <summary>Rule: Specialty And Reason Required (Subflow 5.10).</summary>
    SpecialtyAndReasonRequired,

    /// <summary>Rule: One Active Scheduled Visit Per Patient (Subflow 5.10).</summary>
    PatientAlreadyHasActiveScheduledFollowUp,

    ScheduledFollowUpNotFound,

    /// <summary>Rule: Preparation From The Closed List (MA-2).</summary>
    UnknownPreparationInstruction,

    /// <summary>Rule: Modality Is In Person Or Remote (MA-2).</summary>
    UnknownConsultationModality,

    /// <summary>Rule: Only A Scheduled Visit Changes (MA-2, MA-4, MA-5).</summary>
    FollowUpNotScheduled,

    /// <summary>MA-2. The agenda filter names a state that does not exist.</summary>
    InvalidFollowUpState,

    /// <summary>Rule: Check In Editable Until The Visit (MA-4).</summary>
    CheckInLocked,

    /// <summary>Rule: Feeling Is Required (MA-4). Good, Fair or Hard.</summary>
    FeelingRequired,

    /// <summary>Rule: At Most Three Questions (MA-4), of each origin.</summary>
    TooManyQuestions,

    /// <summary>Rule: Difficulties From The Closed List (MA-4).</summary>
    UnknownCheckInDifficulty,

    /// <summary>Rule: A Question Has 3 To 300 Characters (MA-4), and its origin is Patient or AiSuggested.</summary>
    InvalidCheckInQuestion,

    /// <summary>MA-4. The patient has not answered the check in of this visit yet.</summary>
    PreVisitCheckInNotFound,

    /// <summary>Rule: A Visit Is Scheduled For The Future (MA-5).</summary>
    ScheduledForMustBeInFuture,

    /// <summary>MA-5. A cancellation reason has at most 30 characters.</summary>
    CancellationReasonTooLong,

    /// <summary>
    ///     Rule: A Compliance Range Has Both Ends, In Order, And At Most 31 Days (MA-6). Also when <c>date</c> is sent
    ///     together with <c>from</c>/<c>to</c>.
    /// </summary>
    InvalidComplianceRange,

    /// <summary>Rule: Only An Issued Prompt Is Acknowledged (MA-7). There is no consistency prompt to acknowledge.</summary>
    ConsistencyPromptNotIssued,

    /// <summary>
    ///     Rule: Patient Prompt Required Before Escalation (MA-7). The patient saw the prompt less than N days ago
    ///     (Monitoring:EscalationDaysAfterPatientAcknowledgement).
    /// </summary>
    PatientAcknowledgementTooRecent,

    /// <summary>RM-4. The referral does not exist, or another practitioner recorded it.</summary>
    ReferralNotFound,

    /// <summary>Rule: A Referral Is Closed Once (RM-4).</summary>
    ReferralAlreadyClosed,

    /// <summary>
    ///     Rule: Summary Needs Three Logged Days (IA-2, IA-4). There is not enough of the diary to write about yet (PT13.2.V
    ///     "aún sin resumen"); also when no weekly summary was generated. 404.
    /// </summary>
    NotEnoughData,

    /// <summary>IA-2. A weekly summary starts on a Monday.</summary>
    InvalidWeekStart,

    UnexpectedError
}

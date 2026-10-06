using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     The next visit, on the calendar.
/// </summary>
/// <remarks>
///     Business rule: Missed Visit Does Not Close The Care Link (Subflow 5.10). Nothing reads
///     <see cref="FollowUpState.Missed" /> to revoke a link, discharge a patient or change anything at all
///     outside this row, and the absence of that path is the rule: somebody who could not make it on
///     Tuesday is still somebody's patient on Wednesday.
///     MA-2: the visit carries how to prepare for it and its modality, and it can end in
///     <see cref="FollowUpState.Completed" /> (a consultation was published for it) or
///     <see cref="FollowUpState.Cancelled" />. MA-5: the practitioner cancels or reschedules it through its own
///     endpoints; rescheduling keeps the pre-visit check in (MA-4).
/// </remarks>
public partial class ScheduledFollowUp
{
    /// <summary>NOTE: technical constant, the size of the cancellation_reason column.</summary>
    public const int CancellationReasonMaxLength = 30;

    /// <summary>MA-5. The reason stored when the practitioner cancels without giving one.</summary>
    public const string CancelledByPractitionerReason = "CancelledByPractitioner";

    /// <summary>CR-4. The reason stored when the patient is discharged (PR18).</summary>
    public const string DischargedReason = "Discharged";

    /// <summary>CR-4. The reason stored when the patient switches practitioner (CR-1).</summary>
    public const string SwitchedPractitionerReason = "SwitchedPractitioner";

    private List<string> _preparation = [];

    /// <summary>Required by EF Core.</summary>
    protected ScheduledFollowUp()
    {
    }

    /// <summary>Subflow 5.10 - Schedule Follow Up.</summary>
    /// <param name="command">Who is being seen, by whom, when, how to prepare and in what modality.</param>
    public ScheduledFollowUp(ScheduleFollowUpCommand command)
    {
        if (command.ScheduledFor <= DateTimeOffset.UtcNow)
            throw new ArgumentException("A follow up is scheduled for a moment that has not happened yet.",
                nameof(command));

        PatientId = command.PatientId;
        PractitionerId = command.PractitionerId;
        ScheduledFor = command.ScheduledFor;
        State = new FollowUpState(FollowUpState.Scheduled);
        _preparation = PreparationInstruction.ListOf(command.Preparation).Select(p => p.Value).ToList();
        Modality = new ConsultationModality(command.Modality);
    }

    public FollowUpId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The agenda is read per practitioner,
    ///     so the row records whose agenda it belongs to.
    /// </summary>
    public int PractitionerId { get; private set; }

    public DateTimeOffset ScheduledFor { get; private set; }

    public FollowUpState State { get; private set; } = null!;

    /// <summary>NOTE: technical field, not part of the domain model. When the visit was flagged missed.</summary>
    public DateTimeOffset? MissedAt { get; private set; }

    /// <summary>MA-2. How to prepare (PT25.1). Empty means "sin indicaciones de preparación".</summary>
    public IReadOnlyCollection<PreparationInstruction> Preparation =>
        _preparation.Select(p => new PreparationInstruction(p)).ToList();

    /// <summary>MA-2. In person by default.</summary>
    public ConsultationModality Modality { get; private set; } = new(ConsultationModality.InPerson);

    /// <summary>MA-2. "Agendada el 4 de septiembre": the moment the visit was put on the calendar.</summary>
    public DateTimeOffset? ScheduledAt => CreatedAt;

    /// <summary>MA-2. When the consultation that completed it was published.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>MA-2. Cross-context reference to the consultation (Nutritional Care). A plain int.</summary>
    public int? CompletedByConsultationId { get; private set; }

    /// <summary>MA-2. When it was cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>MA-2. Why it was cancelled ("Discharged", or the practitioner's short reason).</summary>
    public string? CancellationReason { get; private set; }

    public bool IsScheduled => State.IsScheduled;

    /// <summary>
    ///     MA-4. Rule: Check In Editable Until The Visit. The patient can send or edit the check in of this visit
    ///     while it is still scheduled and its hour has not arrived.
    /// </summary>
    /// <param name="now">The moment of the request.</param>
    public bool AcceptsCheckInAt(DateTimeOffset now)
    {
        return IsScheduled && ScheduledFor > now;
    }

    /// <summary>Subflow 5.10 - Flag Missed Follow Up.</summary>
    /// <remarks>
    ///     Business rule: Missed Visit Does Not Close The Care Link (Monitoring and Adherence,
    ///     Subflow 5.10). The method writes a state and a date on this row and reaches nothing else.
    /// </remarks>
    /// <param name="asOf">The moment the scheduled date was found to have passed.</param>
    /// <returns>False when it was already flagged, which keeps the time-driven policy idempotent.</returns>
    public bool MarkMissed(DateTimeOffset asOf)
    {
        if (!IsScheduled) return false;
        if (ScheduledFor > asOf) return false;

        State = new FollowUpState(FollowUpState.Missed);
        MissedAt = asOf;
        return true;
    }

    /// <summary>
    ///     MA-2 - A consultation of the guided flow was published for this visit, so the visit happened.
    /// </summary>
    /// <remarks>
    ///     A visit already flagged missed can still be completed: the missed-visit policy runs on a timer,
    ///     and a consultation published after it ran is proof that the patient did come. <see cref="MissedAt" />
    ///     is kept as history. A cancelled visit is not reopened.
    /// </remarks>
    /// <param name="consultationId">The consultation that was published.</param>
    /// <param name="at">When it was published.</param>
    /// <returns>False when it was already completed, which keeps the policy idempotent.</returns>
    /// <exception cref="InvalidOperationException">When the visit was cancelled.</exception>
    public bool MarkCompleted(int consultationId, DateTimeOffset at)
    {
        if (State.IsCompleted) return false;
        if (State.IsCancelled)
            throw new InvalidOperationException("A cancelled visit cannot be completed.");

        State = new FollowUpState(FollowUpState.Completed);
        CompletedAt = at;
        CompletedByConsultationId = consultationId;
        return true;
    }

    /// <summary>MA-2/MA-5 - Cancel a visit that is still on the calendar.</summary>
    /// <param name="reason">Short reason, at most 30 characters ("Discharged").</param>
    /// <exception cref="ArgumentException">When the reason is missing or too long.</exception>
    /// <exception cref="InvalidOperationException">When the visit is no longer scheduled.</exception>
    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A cancellation needs a reason.", nameof(reason));
        if (reason.Trim().Length > CancellationReasonMaxLength)
            throw new ArgumentException(
                $"A cancellation reason cannot exceed {CancellationReasonMaxLength} characters.", nameof(reason));
        if (!IsScheduled)
            throw new InvalidOperationException("Only a scheduled visit can be cancelled.");

        State = new FollowUpState(FollowUpState.Cancelled);
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = reason.Trim();
    }

    /// <summary>MA-2/MA-5 - Move a visit that is still on the calendar to another future moment.</summary>
    /// <param name="newMoment">The new moment; it has to be in the future.</param>
    /// <param name="preparation">
    ///     MA-5. New preparation codes of the closed list, replacing the current ones; null keeps them.
    /// </param>
    /// <exception cref="ArgumentException">When the new moment is not in the future, or a code is unknown.</exception>
    /// <exception cref="InvalidOperationException">When the visit is no longer scheduled.</exception>
    public void Reschedule(DateTimeOffset newMoment, IReadOnlyList<string>? preparation = null)
    {
        if (!IsScheduled)
            throw new InvalidOperationException("Only a scheduled visit can be rescheduled.");
        if (newMoment <= DateTimeOffset.UtcNow)
            throw new ArgumentException("A follow up is rescheduled to a moment that has not happened yet.",
                nameof(newMoment));
        var newPreparation = preparation is null
            ? null
            : PreparationInstruction.ListOf(preparation).Select(p => p.Value).ToList();

        ScheduledFor = newMoment;
        if (newPreparation is not null) _preparation = newPreparation;
    }
}

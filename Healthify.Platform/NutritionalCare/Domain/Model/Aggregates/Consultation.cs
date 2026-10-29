using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

/// <summary>
///     The guided consultation as a process manager inside Nutritional Care: it records
///     which step the practitioner is on and the identifiers of what each step produced, so the
///     consultation can be left and resumed.
/// </summary>
/// <remarks>
///     The assessment, diagnosis and plan keep owning their own invariants.
/// </remarks>
public partial class Consultation
{
    /// <summary>Required by EF Core.</summary>
    protected Consultation()
    {
    }

    public Consultation(StartConsultationCommand command, bool isFirstConsultation)
    {
        PatientId = command.PatientId;
        PractitionerId = command.PractitionerId;
        ScheduledFollowUpId = command.ScheduledFollowUpId;
        CurrentStep = new ConsultationStep(ConsultationStep.Measurement);
        State = new ConsultationState(ConsultationState.InProgress);
        IsFirstConsultation = isFirstConsultation;
        StartedAt = DateTimeOffset.UtcNow;
        LastSavedAt = StartedAt;
    }

    public ConsultationId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account.</summary>
    public int PatientId { get; private set; }

    /// <summary>Cross-context reference to the practitioner leading the consultation.</summary>
    public int PractitionerId { get; private set; }

    /// <summary>Cross-context reference to the Monitoring appointment it started from, if any.</summary>
    public int? ScheduledFollowUpId { get; private set; }

    public ConsultationStep CurrentStep { get; private set; } = null!;

    public ConsultationState State { get; private set; } = null!;

    /// <summary>Step 1. The closed assessment with the measurement of this consultation.</summary>
    public int? AssessmentId { get; private set; }

    /// <summary>Step 2.</summary>
    public int? DiagnosisId { get; private set; }

    /// <summary>Step 3. The draft plan version.</summary>
    public int? PlanId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>The "saved today" label of a consultation in progress: every saved step touches it.</summary>
    public DateTimeOffset LastSavedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The plan version published when the consultation closed.</summary>
    public int? PublishedPlanVersion { get; private set; }

    /// <summary>The "first consultation" label in the history of past consultations.</summary>
    public bool IsFirstConsultation { get; private set; }

    /// <summary>The Idempotency-Key of the publication that completed it, if the client sent one.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>What step 4 had chosen before publishing, so nothing the practitioner wrote is lost.</summary>
    public PublicationDraft? PublicationDraft { get; private set; }

    /// <summary>When it was discarded.</summary>
    public DateTimeOffset? AbandonedAt { get; private set; }

    public bool IsInProgress => State.IsInProgress;

    /// <summary>
    ///     Whether <paramref name="key" /> repeats the publication that completed this consultation (a retry
    ///     after a timeout).
    /// </summary>
    public bool IsReplayOf(IdempotencyKey? key)
    {
        return key is not null && State.Value == ConsultationState.Completed &&
               string.Equals(IdempotencyKey, key.Value, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Step 1 saved. Repeating the step attaches the new assessment that supersedes the
    ///     previous one; nothing already saved is deleted.
    /// </summary>
    public void AttachAssessment(int assessmentId)
    {
        // Business rule: Only An In-Progress Consultation Moves. It closes on publication.
        if (!IsInProgress)
            throw new InvalidOperationException("Only a consultation in progress can save a step.");
        if (assessmentId <= 0)
            throw new ArgumentException("The assessment identifier must be positive.", nameof(assessmentId));

        // The diagnosis of step 2 read the previous assessment, so it has to be issued again (the caller
        // discards the pending one). The draft plan is kept: step 3 recalculates it, or replaces it when it was
        // already prescribed on the previous diagnosis.
        AssessmentId = assessmentId;
        DiagnosisId = null;
        CurrentStep = new ConsultationStep(ConsultationStep.Diagnosis);
        Touch();
    }

    /// <summary>
    ///     Step 2 saved. Repeating the step attaches the diagnosis that superseded the previous one.
    /// </summary>
    public void AttachDiagnosis(int diagnosisId)
    {
        // Business rule: Only An In-Progress Consultation Moves.
        if (!IsInProgress)
            throw new InvalidOperationException("Only a consultation in progress can save a step.");
        // Business rule: Steps In Order. There is no diagnosis without the measurement of step 1.
        if (AssessmentId is null)
            throw new InvalidOperationException("The measurement of step 1 comes before the diagnosis.");
        if (diagnosisId <= 0)
            throw new ArgumentException("The diagnosis identifier must be positive.", nameof(diagnosisId));

        // A draft plan proposed on the previous diagnosis is recalculated on the new one at the next
        // target proposal, or replaced if it was already prescribed; publishing requires them to match.
        DiagnosisId = diagnosisId;
        CurrentStep = new ConsultationStep(ConsultationStep.Targets);
        Touch();
    }

    /// <summary>Step 3 saved: the draft plan version (proposal, then prescription).</summary>
    public void AttachPlanDraft(int planId)
    {
        if (!IsInProgress)
            throw new InvalidOperationException("Only a consultation in progress can save a step.");
        // Business rule: Steps In Order. Targets are calculated on the diagnosis of step 2.
        if (DiagnosisId is null)
            throw new InvalidOperationException("The diagnosis of step 2 comes before the targets.");
        if (planId <= 0) throw new ArgumentException("The plan identifier must be positive.", nameof(planId));

        PlanId = planId;
        CurrentStep = new ConsultationStep(ConsultationStep.Publication);
        Touch();
    }

    /// <summary>The draft of step 3 changed (recalculated or prescribed): it counts as saved today.</summary>
    public void MarkTargetsSaved()
    {
        if (!IsInProgress)
            throw new InvalidOperationException("Only a consultation in progress can save a step.");
        if (PlanId is null)
            throw new InvalidOperationException("There is no draft plan to save.");
        Touch();
    }

    /// <summary>
    ///     Step 4: the plan version of step 3 was published. The consultation closes
    ///     on publication and can no longer move.
    /// </summary>
    public void Complete(int planVersion, IdempotencyKey? idempotencyKey = null)
    {
        // Business rule: Only An In-Progress Consultation Moves.
        if (!IsInProgress)
            throw new InvalidOperationException("Only a consultation in progress can be completed.");
        // Business rule: Steps In Order. Nothing is published without the three previous steps.
        if (AssessmentId is null || DiagnosisId is null || PlanId is null)
            throw new InvalidOperationException("Measurement, diagnosis and targets come before the publication.");
        if (planVersion <= 0)
            throw new ArgumentException("The plan version must be positive.", nameof(planVersion));

        State = new ConsultationState(ConsultationState.Completed);
        CompletedAt = DateTimeOffset.UtcNow;
        PublishedPlanVersion = planVersion;
        IdempotencyKey = idempotencyKey?.Value;
        LastSavedAt = CompletedAt.Value;
    }

    /// <summary>
    ///     Step 4 before publishing: keep the chosen restrictions and guidelines. Saving again replaces the
    ///     draft; it is not part of the clinical record.
    /// </summary>
    public void SavePublicationDraft(PublicationDraft draft)
    {
        // Business rule: Only An In-Progress Consultation Moves.
        if (!IsInProgress)
            throw new InvalidOperationException("Only a consultation in progress can save a step.");
        // Business rule: Steps In Order. The publication comes after the targets of step 3.
        if (PlanId is null)
            throw new InvalidOperationException("The targets of step 3 come before the publication.");

        PublicationDraft = draft;
        Touch();
    }

    /// <summary>
    ///     Discard: the consultation stops. Everything it saved stays as history; the caller
    ///     discards its pending diagnosis and unpublished draft. A completed consultation is not discarded.
    /// </summary>
    public void Abandon()
    {
        if (!IsInProgress)
            throw new InvalidOperationException("Only a consultation in progress can be discarded.");

        State = new ConsultationState(ConsultationState.Abandoned);
        AbandonedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    private void Touch()
    {
        LastSavedAt = DateTimeOffset.UtcNow;
    }
}

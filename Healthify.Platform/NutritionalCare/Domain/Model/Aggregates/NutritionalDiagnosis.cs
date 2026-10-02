using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

/// <summary>
///     Clinical phase 2. The statement that grounds every plan, and the reasoning behind it.
/// </summary>
/// <remarks>
///     Nutritional Diagnosis Issued deliberately triggers no policy in any other context: the
///     patient does not read their diagnosis in the app, and exposing it would open a clinical
///     problem this platform does not intend to manage.
///     NC-4 adds the closed list code (EV-3), who chose it, and the body mass index it read. Historic
///     rows keep their free text statement and have no code.
/// </remarks>
public partial class NutritionalDiagnosis
{
    /// <summary>Required by EF Core.</summary>
    protected NutritionalDiagnosis()
    {
    }

    /// <summary>
    ///     Subflow 3.2 - stand-alone endpoint. Without a code it is the original free text diagnosis; with
    ///     one (NC-4), <paramref name="basis" /> writes the rationale when none is given.
    /// </summary>
    public NutritionalDiagnosis(IssueDiagnosisCommand command, ClinicalMeasurement? basis = null)
    {
        PatientId = command.PatientId;
        PractitionerId = command.PractitionerId;
        AssessmentId = command.AssessmentId;

        if (command.Code is null)
        {
            if (string.IsNullOrWhiteSpace(command.Statement))
                throw new ArgumentException("A diagnosis statement is required.", nameof(command));
            Statement = command.Statement.Trim();

            // Business rule: Clinical Rationale Required (Nutritional Care, Subflow 3.2)
            // The value object refuses an empty rationale, so a diagnosis without reasoning cannot exist.
            Rationale = new ClinicalRationale(command.Rationale ?? string.Empty);
        }
        else
        {
            var code = new DiagnosisCode(command.Code);
            AssignCoded(code, new DiagnosisSource(command.Source ?? DiagnosisSource.PractitionerSelected),
                command.AiGenerationId, command.Rationale, basis);
            Statement = string.IsNullOrWhiteSpace(command.Statement) ? code.Value : command.Statement.Trim();
        }

        IssuedAt = DateTimeOffset.UtcNow;
        SupersededAt = null;
    }

    public DiagnosisId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>Cross-context reference to the issuing practitioner.</summary>
    public int PractitionerId { get; private set; }

    /// <summary>The closed assessment this diagnosis reads. Same context, separate aggregate root.</summary>
    public int AssessmentId { get; private set; }

    /// <summary>Free text on historic rows; the code itself on coded diagnoses (NC-4).</summary>
    public string Statement { get; private set; } = null!;

    public ClinicalRationale Rationale { get; private set; } = null!;

    /// <summary>NC-4. The closed list code, or null on historic free text diagnoses.</summary>
    public DiagnosisCode? Code { get; private set; }

    /// <summary>NC-4. Who chose the code, or null on historic rows.</summary>
    public DiagnosisSource? Source { get; private set; }

    /// <summary>NC-4. The AI generation whose suggestion was accepted (IA-0 traceability).</summary>
    public long? AiGenerationId { get; private set; }

    /// <summary>NC-4. Snapshot of the body mass index of the measurement it reads. Never leaves this context.</summary>
    public decimal? BmiAtIssue { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }

    /// <summary>
    ///     NC-7. The consultation whose step 2 issued this diagnosis while it waits for the publication of
    ///     step 4. Null once it is active, and on every diagnosis that did not come from a consultation.
    /// </summary>
    /// <remarks>
    ///     Professional information that stays inside the consultation: a pending diagnosis is not the active
    ///     one, so no read model, facade or record shows it until the plan is published.
    /// </remarks>
    public int? PendingConsultationId { get; private set; }

    /// <summary>NC-7. When a pending diagnosis was left out: step 2 repeated, or the consultation discarded.</summary>
    public DateTimeOffset? DiscardedAt { get; private set; }

    /// <summary>
    ///     Business rule: One Active Diagnosis Per Patient (Subflow 3.2), read as "never two active at once"
    ///     (NC-7): a pending or discarded diagnosis is not active.
    /// </summary>
    public bool IsActive => SupersededAt is null && PendingConsultationId is null && DiscardedAt is null;

    /// <summary>NC-7. Issued in step 2 of a consultation that has not been published yet.</summary>
    public bool IsPending => PendingConsultationId is not null && DiscardedAt is null;

    public bool IsDiscarded => DiscardedAt is not null;

    /// <summary>NC-7. Whether this is the pending diagnosis of <paramref name="consultationId" />.</summary>
    public bool IsPendingFor(int consultationId)
    {
        return IsPending && PendingConsultationId == consultationId;
    }

    /// <summary>NC-4 - Step 2 of the guided consultation: a coded diagnosis on the measurement of step 1.</summary>
    /// <remarks>
    ///     NC-7. With <c>pendingForConsultationId</c>, the consultation that issues it, the diagnosis stays pending
    ///     until that consultation publishes its plan. Without it the diagnosis is active at once.
    /// </remarks>
    public static NutritionalDiagnosis FromConsultation(
        int patientId,
        int practitionerId,
        int assessmentId,
        DiagnosisCode code,
        DiagnosisSource source,
        long? aiGenerationId,
        string? rationale,
        ClinicalMeasurement basis,
        int? pendingForConsultationId = null)
    {
        if (pendingForConsultationId is <= 0)
            throw new ArgumentException("The consultation identifier must be positive.",
                nameof(pendingForConsultationId));

        var diagnosis = new NutritionalDiagnosis
        {
            PatientId = patientId,
            PractitionerId = practitionerId,
            AssessmentId = assessmentId,
            Statement = code.Value,
            IssuedAt = DateTimeOffset.UtcNow,
            SupersededAt = null,
            PendingConsultationId = pendingForConsultationId
        };
        diagnosis.AssignCoded(code, source, aiGenerationId, rationale, basis);
        return diagnosis;
    }

    /// <summary>Retires this diagnosis so a newer one can take its place. The row is kept.</summary>
    public void Supersede()
    {
        if (!IsActive) throw new InvalidOperationException("Only the active diagnosis can be superseded.");
        SupersededAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     NC-7 - The consultation published its plan: its pending diagnosis becomes the active one. The caller
    ///     supersedes the previous active diagnosis in the same transaction.
    /// </summary>
    public void Activate()
    {
        if (!IsPending) throw new InvalidOperationException("Only a pending diagnosis can be activated.");
        PendingConsultationId = null;
    }

    /// <summary>
    ///     NC-7 - Step 2 was repeated, or the consultation was discarded: the pending diagnosis is left out. The
    ///     row is kept, and the active diagnosis was never touched.
    /// </summary>
    public void Discard()
    {
        if (!IsPending) throw new InvalidOperationException("Only a pending diagnosis can be discarded.");
        DiscardedAt = DateTimeOffset.UtcNow;
    }

    private void AssignCoded(DiagnosisCode code, DiagnosisSource source, long? aiGenerationId, string? rationale,
        ClinicalMeasurement? basis)
    {
        if (source.IsAiSuggestionAccepted)
        {
            // Business rule: Accepted Suggestion Is Traceable (NC-4). The generation and the rationale
            // the AI proposed are kept together with the code.
            if (aiGenerationId is null or <= 0)
                throw new ArgumentException("An accepted AI suggestion needs its generation.", nameof(aiGenerationId));
            if (string.IsNullOrWhiteSpace(rationale))
                throw new ArgumentException("An accepted AI suggestion keeps its rationale.", nameof(rationale));
        }

        Code = code;
        Source = source;
        AiGenerationId = source.IsAiSuggestionAccepted ? aiGenerationId : null;
        BmiAtIssue = basis?.BmiKgM2;

        // Business rule: Clinical Rationale Required (Subflow 3.2), kept by NC-4: when the practitioner
        // picks the code without writing one, the rationale is written from the measurement.
        if (!string.IsNullOrWhiteSpace(rationale))
            Rationale = new ClinicalRationale(rationale);
        else if (basis is not null)
            Rationale = ClinicalRationale.FromMeasurement(basis.BmiKgM2, basis.WaistCircumferenceCm,
                basis.BodyFatPercentage);
        else
            throw new ArgumentException("A clinical rationale is required.", nameof(rationale));
    }
}

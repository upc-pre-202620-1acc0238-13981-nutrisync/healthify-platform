using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

/// <summary>
///     Clinical phase 3, and the root of the versioning. A plan moves through three states inside
///     one consultation: targets are proposed by arithmetic, prescribed by a person, and only then
///     published.
/// </summary>
/// <remarks>
///     What crosses the boundary to the patient is not this aggregate. The published contract carries
///     targets, guidelines and restrictions only: the diagnosis and the calculation basis never leave
///     this context.
///     The three composite value objects are stored as flat columns and rebuilt by the computed
///     properties below. Owned types would need their own key mapped onto a typed primary key, which
///     EF Core cannot reconcile, so this follows the same pattern the platform already uses for
///     optional and composite value objects.
/// </remarks>
public partial class NutritionPlan
{
    private List<Guideline> _guidelines = [];
    private List<string> _restrictions = [];
    private List<string> _legacyRestrictions = [];
    private List<string>? _droppedLegacyRestrictions;
    private List<PlanChange>? _changesFromPrevious;

    /// <summary>Required by EF Core.</summary>
    protected NutritionPlan()
    {
    }

    /// <summary>Subflow 3.3 - the plan comes into being with its proposal, never empty.</summary>
    public NutritionPlan(
        int patientId,
        int practitionerId,
        int diagnosisId,
        int version,
        CalculationBasis basis,
        TargetProposal proposal)
    {
        if (version <= 0) throw new ArgumentException("A plan version must be positive.", nameof(version));

        PatientId = patientId;
        PractitionerId = practitionerId;

        // Business rule: No Plan Without Diagnosis (Nutritional Care, Subflow 3.5)
        // The reference is required by the constructor, so a plan without a diagnosis cannot exist.
        DiagnosisId = diagnosisId;

        Version = version;

        // Business rule: Calculation Basis Always Recorded (Nutritional Care, Subflow 3.5)
        StoreCalculationBasis(basis);
        StoreProposal(proposal);

        PublishedAt = null;
        IsActive = false;
        SupersededAt = null;
    }

    public PlanId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>Cross-context reference to the prescribing practitioner.</summary>
    public int PractitionerId { get; private set; }

    /// <summary>The diagnosis that grounds this plan. Same context, separate aggregate root.</summary>
    public int DiagnosisId { get; private set; }

    public int Version { get; private set; }

    // Persisted projection of the CalculationBasis value object. Never leaves this bounded context.
    public Equation BasisEquation { get; private set; } = null!;
    public string BasisReferenceWeightKind { get; private set; } = null!;
    public decimal BasisReferenceWeightKg { get; private set; }
    public decimal BasisActivityFactor { get; private set; }
    public string BasisDeficitKind { get; private set; } = null!;
    public decimal BasisDeficitValue { get; private set; }
    public decimal BasisComputedBmr { get; private set; }
    public decimal BasisComputedTdee { get; private set; }

    // Persisted projection of the TargetProposal value object.
    public decimal ProposalEnergyKcal { get; private set; }
    public decimal ProposalProteinG { get; private set; }
    public decimal ProposalCarbG { get; private set; }
    public decimal ProposalFatG { get; private set; }

    // Persisted projection of the PrescribedTargets value object, absent until it is prescribed.
    public decimal? PrescribedEnergyKcal { get; private set; }
    public decimal? PrescribedProteinG { get; private set; }
    public decimal? PrescribedCarbG { get; private set; }
    public decimal? PrescribedFatG { get; private set; }
    public string? PrescribedOutcome { get; private set; }
    public string? PrescribedOverrideReason { get; private set; }

    /// <summary>
    ///     Why this version exists (Subflow 3.6). X-2: rebuilt from three columns, the sentence
    ///     (<see cref="ChangeReasonText" />), its code and the parameters of the code.
    /// </summary>
    public ChangeReason? ChangeReason =>
        ValueObjects.ChangeReason.Rehydrate(ChangeReasonText, ChangeReasonCode, ChangeReasonData);

    /// <summary>NOTE: technical field for EF (<c>change_reason</c>). Read <see cref="ChangeReason" />.</summary>
    public string? ChangeReasonText { get; private set; }

    /// <summary>NOTE: technical field for EF (X-2, <c>change_reason_code</c>). Read <see cref="ChangeReason" />.</summary>
    public string? ChangeReasonCode { get; private set; }

    /// <summary>NOTE: technical field for EF (X-2, <c>change_reason_data</c>). Read <see cref="ChangeReason" />.</summary>
    public ChangeReasonData? ChangeReasonData { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }

    /// <summary>Business rule: One Active Version Per Patient (Subflow 3.5).</summary>
    public bool IsActive { get; private set; }

    /// <summary>NC-6. Catalog codes and custom texts ("Otra indicación").</summary>
    public IReadOnlyList<Guideline> Guidelines => _guidelines;

    /// <summary>NC-6. <see cref="DietaryRestriction" /> codes.</summary>
    public IReadOnlyList<string> Restrictions => _restrictions;

    /// <summary>
    ///     NC-6. Free text restrictions written before the closed list that match no code. Kept and shown as
    ///     they were (the patient keeps seeing them), until a new version maps them to a code.
    /// </summary>
    public IReadOnlyList<string> LegacyRestrictions => _legacyRestrictions;

    /// <summary>
    ///     NC-6. The legacy restrictions of the previous version that this version did not carry over: the
    ///     record that they were left out on purpose, by a person, when this version was published.
    /// </summary>
    public IReadOnlyList<string> DroppedLegacyRestrictions => _droppedLegacyRestrictions ?? [];

    /// <summary>
    ///     NC-8. "Qué cambió en esta versión", calculated by <see cref="PlanVersionDiff" /> when the version is
    ///     published and never again. Null for versions published before NC-8.
    /// </summary>
    public IReadOnlyList<PlanChange>? ChangesFromPrevious => _changesFromPrevious;

    /// <summary>
    ///     NC-9. The practitioner's message to the patient for this version (<see cref="PatientFacingMessage" />, at
    ///     most 500 characters), or null. Written with the publication, the adjustment or the NC-10 acceptance, and
    ///     never changed once the version is published. Each version has its own: it is not carried over.
    /// </summary>
    public string? PatientMessage { get; private set; }

    public bool HasRecordedChanges => _changesFromPrevious is not null;

    /// <summary>Rebuilt from the stored columns. Never leaves this bounded context.</summary>
    public CalculationBasis CalculationBasis => new(BasisEquation, BasisReferenceWeightKind,
        BasisReferenceWeightKg, BasisActivityFactor, BasisDeficitKind, BasisDeficitValue,
        BasisComputedBmr, BasisComputedTdee);

    /// <summary>Rebuilt from the stored columns.</summary>
    public TargetProposal TargetProposal =>
        new(ProposalEnergyKcal, ProposalProteinG, ProposalCarbG, ProposalFatG);

    /// <summary>Rebuilt from the stored columns, or null while nothing has been prescribed.</summary>
    public PrescribedTargets? PrescribedTargets => PrescribedOutcome is null
        ? null
        : new PrescribedTargets(
            PrescribedEnergyKcal!.Value, PrescribedProteinG!.Value, PrescribedCarbG!.Value,
            PrescribedFatG!.Value, new PrescriptionOutcome(PrescribedOutcome),
            PrescribedOverrideReason is null ? null : new OverrideReason(PrescribedOverrideReason));

    /// <summary>
    ///     NC-2. When an unpublished draft was left out: its consultation was discarded, or it was prescribed on a
    ///     diagnosis the consultation replaced. The row is kept; it never was a version the patient received.
    /// </summary>
    public DateTimeOffset? DiscardedAt { get; private set; }

    public bool IsPrescribed => PrescribedOutcome is not null;
    public bool IsPublished => PublishedAt is not null;
    public bool IsSuperseded => SupersededAt is not null;
    public bool IsDiscarded => DiscardedAt is not null;

    private void StoreCalculationBasis(CalculationBasis basis)
    {
        BasisEquation = basis.Equation;
        BasisReferenceWeightKind = basis.ReferenceWeightKind;
        BasisReferenceWeightKg = basis.ReferenceWeightKg;
        BasisActivityFactor = basis.ActivityFactor;
        BasisDeficitKind = basis.DeficitKind;
        BasisDeficitValue = basis.DeficitValue;
        BasisComputedBmr = basis.ComputedBmr;
        BasisComputedTdee = basis.ComputedTdee;
    }

    private void StoreProposal(TargetProposal proposal)
    {
        ProposalEnergyKcal = proposal.EnergyKcal;
        ProposalProteinG = proposal.ProteinG;
        ProposalCarbG = proposal.CarbG;
        ProposalFatG = proposal.FatG;
    }

    /// <summary>
    ///     NC-5 - "Cambiar parámetros" in EV-4: the same draft version is calculated again, on the diagnosis
    ///     the consultation now has. Only an unprescribed draft can be recalculated: once a person has signed
    ///     the numbers, they are not replaced by arithmetic.
    /// </summary>
    public void Recalculate(int diagnosisId, CalculationBasis basis, TargetProposal proposal)
    {
        EnsureNotDiscarded();
        if (IsPublished)
            throw new InvalidOperationException("A published plan version cannot be recalculated.");
        if (IsPrescribed)
            throw new InvalidOperationException("A prescribed plan version cannot be recalculated.");

        // Business rule: No Plan Without Diagnosis (Subflow 3.5) and Calculation Basis Always Recorded
        // (Subflow 3.5) hold exactly as in the constructor.
        DiagnosisId = diagnosisId;
        StoreCalculationBasis(basis);
        StoreProposal(proposal);
    }

    /// <summary>Subflow 3.4 - Prescribe Targets.</summary>
    public void PrescribeTargets(PrescribedTargets targets)
    {
        // Business rule: Previous Proposal Required (Nutritional Care, Subflow 3.4)
        // Structurally guaranteed, because the proposal is set by the constructor: there is no way
        // to reach a plan that has not been proposed.
        EnsureNotDiscarded();
        if (IsPublished)
            throw new InvalidOperationException("A published plan version cannot be prescribed again.");
        if (IsPrescribed)
            throw new InvalidOperationException("This plan version already has prescribed targets.");

        // Business rule: Override Requires Reason (Nutritional Care, Subflow 3.4)
        // Enforced inside the PrescribedTargets value object, which refuses to exist without one.
        PrescribedEnergyKcal = targets.EnergyKcal;
        PrescribedProteinG = targets.ProteinG;
        PrescribedCarbG = targets.CarbG;
        PrescribedFatG = targets.FatG;
        PrescribedOutcome = targets.Outcome.Value;
        PrescribedOverrideReason = targets.OverrideReason?.Value;
    }

    /// <summary>Subflow 3.5 - Publish Nutrition Plan, with the closed catalogs of NC-6.</summary>
    public void PublishWith(IEnumerable<Guideline> guidelines, IEnumerable<DietaryRestriction> restrictions)
    {
        EnsureNotDiscarded();
        if (!IsPrescribed)
            throw new InvalidOperationException("A plan cannot be published before its targets are prescribed.");
        if (IsPublished)
            throw new InvalidOperationException("This plan version has already been published.");

        var distinctGuidelines = guidelines.Distinct().ToList();
        // Business rule: At Most Five Custom Guidelines Per Version (NC-6)
        if (distinctGuidelines.Count(g => g.IsCustom) > Guideline.MaximumCustomPerVersion)
            throw new ArgumentException(
                $"A plan version carries at most {Guideline.MaximumCustomPerVersion} custom guidelines.",
                nameof(guidelines));

        _guidelines.Clear();
        _guidelines.AddRange(distinctGuidelines);
        // Business rule: Closed Restriction List (NC-6). Only codes reach a new version.
        _restrictions.Clear();
        _restrictions.AddRange(restrictions.Select(r => r.Value).Distinct());

        PublishedAt = DateTimeOffset.UtcNow;
        IsActive = true;
    }

    /// <summary>
    ///     NC-7 - Why this version exists, written before it is published. Business rule: Change Reason Required
    ///     (Subflow 3.6) holds for every version after the first, whichever path publishes it.
    /// </summary>
    public void SetChangeReason(ChangeReason changeReason)
    {
        if (IsPublished)
            throw new InvalidOperationException("The change reason of a published plan version cannot change.");
        WriteChangeReason(changeReason);
    }

    /// <summary>X-2. The sentence, its code and its parameters, always together.</summary>
    private void WriteChangeReason(ChangeReason changeReason)
    {
        ChangeReasonText = changeReason.Value;
        ChangeReasonCode = changeReason.Code;
        ChangeReasonData = changeReason.Data;
    }

    /// <summary>
    ///     NC-9 - The message for the patient that goes out with this version, or none. Business rule: A Person
    ///     Confirms The Message (NC-9): it is set before publishing, by the practitioner's command, and a published
    ///     version keeps the message it was published with.
    /// </summary>
    public void SetPatientMessage(PatientFacingMessage? message)
    {
        if (IsPublished)
            throw new InvalidOperationException("The message of a published plan version cannot change.");
        PatientMessage = message?.Value;
    }

    /// <summary>
    ///     NC-6. This version replaces <paramref name="previous" />: legacy restrictions are not carried over
    ///     (only codes reach a new version), so the ones the practitioner did not map are recorded here as
    ///     left out. The previous version keeps them untouched.
    /// </summary>
    public void RecordLegacyRestrictionsLeftOut(NutritionPlan previous)
    {
        _droppedLegacyRestrictions = previous.LegacyRestrictions.Count == 0
            ? null
            : previous.LegacyRestrictions.ToList();
    }

    /// <summary>
    ///     NC-8 - What this version changed against <paramref name="previous" /> (null for the first version),
    ///     recorded once, when the version is published, so it is never calculated again and stays as it was.
    /// </summary>
    public void RecordChangesFromPrevious(NutritionPlan? previous)
    {
        if (!IsPublished)
            throw new InvalidOperationException("The changes of a plan version are recorded when it is published.");
        if (HasRecordedChanges)
            throw new InvalidOperationException("The changes of a published plan version are immutable.");
        _changesFromPrevious = PlanVersionDiff.Compare(previous, this).ToList();
    }

    /// <summary>Subflow 3.5 - Publish Nutrition Plan, from the original lists of strings.</summary>
    /// <remarks>
    ///     Deprecated by NC-6: a catalog code is that code and any other text a custom guideline; restrictions
    ///     must be codes. Same reading as the stand-alone endpoint.
    /// </remarks>
    public void Publish(IEnumerable<string> guidelines, IEnumerable<string> restrictions)
    {
        PublishWith(
            guidelines.Where(g => !string.IsNullOrWhiteSpace(g)).Select(Guideline.FromLegacyText).ToList(),
            restrictions.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => new DietaryRestriction(r)).ToList());
    }

    /// <summary>
    ///     NC-2 - Leave an unpublished draft out. A published version is never discarded: it is superseded.
    /// </summary>
    public void Discard()
    {
        if (IsPublished)
            throw new InvalidOperationException("A published plan version is superseded, never discarded.");
        EnsureNotDiscarded();
        DiscardedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureNotDiscarded()
    {
        if (IsDiscarded) throw new InvalidOperationException("This draft plan version was discarded.");
    }

    /// <summary>Subflow 3.6 - Previous Version Superseded Never Deleted.</summary>
    public void Supersede()
    {
        // Business rule: Previous Version Superseded Never Deleted (Nutritional Care, Subflow 3.6)
        // The row stays exactly as it was, dated. Plan Version History is only meaningful because
        // nothing here is ever removed.
        if (IsSuperseded) throw new InvalidOperationException("This plan version has already been superseded.");

        SupersededAt = DateTimeOffset.UtcNow;
        IsActive = false;
    }

    /// <summary>
    ///     Subflow 3.6 - Adjust Nutrition Plan. Produces the next version, carrying the calculation
    ///     basis of the version it replaces: an adjustment between visits changes the numbers, not
    ///     the arithmetic they came from.
    /// </summary>
    /// <param name="command">The new targets.</param>
    /// <param name="changeReason">Why the version exists.</param>
    /// <param name="guidelines">The guidelines of the new version.</param>
    /// <param name="restrictions">The restrictions of the new version.</param>
    /// <param name="patientMessage">NC-9. The message that goes out with the new version, or none.</param>
    public NutritionPlan CreateAdjustedVersion(AdjustNutritionPlanCommand command, ChangeReason changeReason,
        IReadOnlyList<Guideline> guidelines, IReadOnlyList<DietaryRestriction> restrictions,
        PatientFacingMessage? patientMessage = null)
    {
        if (!IsPublished)
            throw new InvalidOperationException("Only a published plan version can be adjusted.");

        var proposal = new TargetProposal(command.EnergyKcal, command.ProteinG, command.CarbG, command.FatG);

        var adjusted = new NutritionPlan(PatientId, PractitionerId, DiagnosisId, Version + 1,
            CalculationBasis, proposal);
        // Business rule: Change Reason Required (Nutritional Care, Subflow 3.6)
        adjusted.WriteChangeReason(changeReason);

        // The practitioner set these numbers directly rather than accepting a fresh calculation, so
        // the outcome is an override and the change reason is what justifies it. That also satisfies
        // the invariant that an override always carries a reason.
        adjusted.PrescribeTargets(new PrescribedTargets(
            command.EnergyKcal, command.ProteinG, command.CarbG, command.FatG,
            new PrescriptionOutcome(PrescriptionOutcome.Overridden),
            new OverrideReason(changeReason.Value)));

        adjusted.SetPatientMessage(patientMessage);
        adjusted.PublishWith(guidelines, restrictions);
        adjusted.RecordLegacyRestrictionsLeftOut(this);
        adjusted.RecordChangesFromPrevious(this);

        return adjusted;
    }
}

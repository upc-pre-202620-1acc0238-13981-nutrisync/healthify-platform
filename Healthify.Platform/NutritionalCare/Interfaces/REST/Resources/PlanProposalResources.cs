namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

/// <summary>
///     NC-10 - PR14.IA "Plan propuesto por IA". Professional information: the patient reads only the message, and only
///     once a practitioner assigns the plan.
/// </summary>
public record PlanAdjustmentProposalResource(
    int ProposalId,
    int ReviewItemId,
    string Title,
    decimal? CurrentEnergyKcal,
    decimal ProposedEnergyKcal,
    decimal ProposedProteinG,
    decimal ProposedCarbG,
    decimal ProposedFatG,
    IReadOnlyList<string> AddedGuidelines,
    IReadOnlyList<string> RemovedGuidelines,
    string PatientMessage,
    int RecheckAfterDays,
    string Rationale,
    DateTimeOffset GeneratedAt,
    string Status,
    int? AssignedPlanVersion,
    string? PractitionerLanguage = null,
    string? PatientLanguage = null)
{
    /// <summary>Identifier of the proposal.</summary>
    public int ProposalId { get; init; } = ProposalId;

    /// <summary>The review item it belongs to.</summary>
    public int ReviewItemId { get; init; } = ReviewItemId;

    /// <summary>"Ajustar la energía y reforzar las cenas".</summary>
    public string Title { get; init; } = Title;

    /// <summary>Energy of the version in force when the proposal is read ("1 796 →"), or null when there is none.</summary>
    public decimal? CurrentEnergyKcal { get; init; } = CurrentEnergyKcal;

    /// <summary>"→ 1 650 kcal".</summary>
    public decimal ProposedEnergyKcal { get; init; } = ProposedEnergyKcal;

    /// <summary>Proposed daily protein, in grams.</summary>
    public decimal ProposedProteinG { get; init; } = ProposedProteinG;

    /// <summary>Proposed daily carbohydrate, in grams.</summary>
    public decimal ProposedCarbG { get; init; } = ProposedCarbG;

    /// <summary>Proposed daily fat, in grams.</summary>
    public decimal ProposedFatG { get; init; } = ProposedFatG;

    /// <summary>"Nueva indicación": guideline catalog codes (the client translates them).</summary>
    public IReadOnlyList<string> AddedGuidelines { get; init; } = AddedGuidelines;

    /// <summary>Guideline codes of the version in force the proposal removes.</summary>
    public IReadOnlyList<string> RemovedGuidelines { get; init; } = RemovedGuidelines;

    /// <summary>"Mensaje para Ana". Editable in PR14.IA-A.</summary>
    public string PatientMessage { get; init; } = PatientMessage;

    /// <summary>"Seguimiento: Revisar de nuevo en 7 días".</summary>
    public int RecheckAfterDays { get; init; } = RecheckAfterDays;

    /// <summary>"Por qué".</summary>
    public string Rationale { get; init; } = Rationale;

    /// <summary>When it was generated.</summary>
    public DateTimeOffset GeneratedAt { get; init; } = GeneratedAt;

    /// <summary>Proposed, AcceptedAsIs, AcceptedWithEdits or Dismissed.</summary>
    public string Status { get; init; } = Status;

    /// <summary>The version assigned from it, or null.</summary>
    public int? AssignedPlanVersion { get; init; } = AssignedPlanVersion;

    /// <summary>
    ///     X-2. es or en: the language of <c>title</c> and <c>rationale</c> (the practitioner's when it was generated).
    ///     Null for proposals generated before X-2.
    /// </summary>
    public string? PractitionerLanguage { get; init; } = PractitionerLanguage;

    /// <summary>X-2. es or en: the language of <c>patientMessage</c> (the patient's). Null before X-2.</summary>
    public string? PatientLanguage { get; init; } = PatientLanguage;
}

/// <summary>
///     NC-10 - Payload of <c>POST /review-items/{id}/plan-proposal/acceptance</c>: <c>{ asIs: true }</c> (PR14.IA
///     "Resolver"), or <c>{ asIs: false, energyKcal, proteinG, carbG, fatG, guidelines[], patientMessage }</c>
///     (PR14.IA-A "Asignar plan ajustado").
/// </summary>
public record AcceptPlanProposalResource(
    bool AsIs,
    decimal? EnergyKcal = null,
    decimal? ProteinG = null,
    decimal? CarbG = null,
    decimal? FatG = null,
    IReadOnlyList<string>? Guidelines = null,
    string? PatientMessage = null)
{
    /// <summary>True: assign the proposal as it is. False: assign it with the fields below.</summary>
    public bool AsIs { get; init; } = AsIs;

    /// <summary>Edited daily energy. Required when asIs is false; never below the calorie floor of the patient.</summary>
    public decimal? EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Edited daily protein, in grams.</summary>
    public decimal? ProteinG { get; init; } = ProteinG;

    /// <summary>Edited daily carbohydrate, in grams.</summary>
    public decimal? CarbG { get; init; } = CarbG;

    /// <summary>Edited daily fat, in grams.</summary>
    public decimal? FatG { get; init; } = FatG;

    /// <summary>
    ///     The guideline catalog codes of the new version, the whole list. Custom guidelines in force are kept.
    /// </summary>
    public IReadOnlyList<string>? Guidelines { get; init; } = Guidelines;

    /// <summary>The message for the patient (at most 500 characters), or null for none.</summary>
    public string? PatientMessage { get; init; } = PatientMessage;
}

/// <summary>NC-10 - What an accepted proposal leaves: <c>{ reviewItem, planVersion }</c>.</summary>
public record PlanProposalAcceptanceResource(ReviewItemResource ReviewItem, NutritionPlanResource PlanVersion)
{
    /// <summary>The item, resolved: "Plan v4 asignado (propuesta IA aceptada tal cual)".</summary>
    public ReviewItemResource ReviewItem { get; init; } = ReviewItem;

    /// <summary>The version now in force, already published to the patient.</summary>
    public NutritionPlanResource PlanVersion { get; init; } = PlanVersion;
}

namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

/// <summary>
///     NC-8 - One version of the plan, for the patient (PT4 "Plan versión 3 · vigente", PT4.1).
/// </summary>
/// <remarks>
///     A resource of its own, separate from <see cref="NutritionPlanResource" />: it carries no calculation basis,
///     no diagnosis id and no override reason, and must never be extended with them (Diagnosis And Basis Never
///     Leave The Context).
/// </remarks>
public record PatientPlanVersionResource(
    int Version,
    DateTimeOffset PublishedAt,
    bool IsActive,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<GuidelineItemResource> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<PlanChangeResource> ChangesFromPrevious,
    string? PatientMessage,
    IReadOnlyList<string>? LegacyRestrictions = null)
{
    /// <summary>Version number (1, 2, 3…).</summary>
    public int Version { get; init; } = Version;

    /// <summary>When the practitioner published it.</summary>
    public DateTimeOffset PublishedAt { get; init; } = PublishedAt;

    /// <summary>True for the version in force ("Vigente").</summary>
    public bool IsActive { get; init; } = IsActive;

    /// <summary>Daily energy target in kilocalories.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Daily protein target in grams.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>Daily carbohydrate target in grams.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>Daily fat target in grams.</summary>
    public decimal FatG { get; init; } = FatG;

    /// <summary>Guidelines: a catalog code (translate it) or a custom text (show it as written) each.</summary>
    public IReadOnlyList<GuidelineItemResource> Guidelines { get; init; } = Guidelines;

    /// <summary>Restriction codes (LactoseFree, GlutenFree, Vegan…).</summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>"Qué cambió en esta versión". Empty for the first version.</summary>
    public IReadOnlyList<PlanChangeResource> ChangesFromPrevious { get; init; } = ChangesFromPrevious;

    /// <summary>NC-9. The practitioner's message for this version, or null.</summary>
    public string? PatientMessage { get; init; } = PatientMessage;

    /// <summary>NC-6. Free text restrictions written before the closed list. Show them as written.</summary>
    public IReadOnlyList<string> LegacyRestrictions { get; init; } = LegacyRestrictions ?? [];
}

/// <summary>NC-8. One line of "Qué cambió en esta versión"; the client writes the sentence in es/en.</summary>
public record PlanChangeResource(string Type, string? Code, string? Custom, string? Macro, decimal? From, decimal? To)
{
    /// <summary>
    ///     GuidelineAdded, GuidelineRemoved, RestrictionAdded, RestrictionRemoved, EnergyChanged, MacroChanged or
    ///     NoTargetChanges.
    /// </summary>
    public string Type { get; init; } = Type;

    /// <summary>Guideline or restriction code, for the guideline and restriction types.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>Custom guideline text, or a legacy restriction left out. Show it as written.</summary>
    public string? Custom { get; init; } = Custom;

    /// <summary>Protein, Carb or Fat, for MacroChanged.</summary>
    public string? Macro { get; init; } = Macro;

    /// <summary>Previous value (kcal or grams), for EnergyChanged and MacroChanged.</summary>
    public decimal? From { get; init; } = From;

    /// <summary>New value (kcal or grams), for EnergyChanged and MacroChanged.</summary>
    public decimal? To { get; init; } = To;
}

namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-8. One line of "Qué cambió en esta versión" (PT4): what this version changed against the previous
///     one, in terms the patient already receives (targets, guidelines and restrictions). Never the diagnosis,
///     the calculation basis or the override reason.
/// </summary>
/// <remarks>
///     The client writes the sentence in es/en from the type and the codes. A custom guideline travels as its
///     text, as the published contract already carries it.
/// </remarks>
public sealed record PlanChange
{
    public const string GuidelineAdded = "GuidelineAdded";
    public const string GuidelineRemoved = "GuidelineRemoved";
    public const string RestrictionAdded = "RestrictionAdded";
    public const string RestrictionRemoved = "RestrictionRemoved";
    public const string EnergyChanged = "EnergyChanged";
    public const string MacroChanged = "MacroChanged";
    public const string NoTargetChanges = "NoTargetChanges";

    public const string Protein = "Protein";
    public const string Carb = "Carb";
    public const string Fat = "Fat";

    private PlanChange(string type, string? code, string? custom, string? macro, decimal? from, decimal? to)
    {
        Type = type;
        Code = code;
        Custom = custom;
        Macro = macro;
        From = from;
        To = to;
    }

    public static IReadOnlyList<string> Types { get; } =
    [
        GuidelineAdded, GuidelineRemoved, RestrictionAdded, RestrictionRemoved, EnergyChanged, MacroChanged,
        NoTargetChanges
    ];

    public static IReadOnlyList<string> Macros { get; } = [Protein, Carb, Fat];

    public string Type { get; }

    /// <summary>The guideline or restriction code, for the guideline and restriction types.</summary>
    public string? Code { get; }

    /// <summary>A custom guideline text, or a legacy restriction text left out (NC-6).</summary>
    public string? Custom { get; }

    /// <summary><see cref="Protein" />, <see cref="Carb" /> or <see cref="Fat" />, for <see cref="MacroChanged" />.</summary>
    public string? Macro { get; }

    public decimal? From { get; }
    public decimal? To { get; }

    public static PlanChange GuidelineWasAdded(Guideline guideline)
    {
        return new PlanChange(GuidelineAdded, guideline.Code, guideline.Custom, null, null, null);
    }

    public static PlanChange GuidelineWasRemoved(Guideline guideline)
    {
        return new PlanChange(GuidelineRemoved, guideline.Code, guideline.Custom, null, null, null);
    }

    public static PlanChange RestrictionWasAdded(string code)
    {
        return new PlanChange(RestrictionAdded, code, null, null, null, null);
    }

    public static PlanChange RestrictionWasRemoved(string code)
    {
        return new PlanChange(RestrictionRemoved, code, null, null, null, null);
    }

    /// <summary>NC-6. A free text restriction of the previous version that this version did not carry over.</summary>
    public static PlanChange LegacyRestrictionWasRemoved(string text)
    {
        return new PlanChange(RestrictionRemoved, null, text, null, null, null);
    }

    public static PlanChange EnergyWasChanged(decimal from, decimal to)
    {
        return new PlanChange(EnergyChanged, null, null, null, from, to);
    }

    public static PlanChange MacroWasChanged(string macro, decimal from, decimal to)
    {
        if (!Macros.Contains(macro)) throw new ArgumentException($"'{macro}' is not a macronutrient.", nameof(macro));
        return new PlanChange(MacroChanged, null, null, macro, from, to);
    }

    public static PlanChange NoTargetsChanged()
    {
        return new PlanChange(NoTargetChanges, null, null, null, null, null);
    }

    /// <summary>Rebuilds a stored change without validating it again: history is read as it was written.</summary>
    public static PlanChange Rehydrate(string type, string? code, string? custom, string? macro, decimal? from,
        decimal? to)
    {
        return new PlanChange(type, code, custom, macro, from, to);
    }
}

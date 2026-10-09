namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-6. One indication of the plan (EV-5 "Indicaciones"): a code of the catalog, which the client
///     translates, or a custom text ("Otra indicación"), which is clinical data and is never translated.
/// </summary>
/// <remarks>Stored as <c>{ "code": "ReduceSalt" }</c> or <c>{ "custom": "Caminar 20 min" }</c>.</remarks>
public sealed record Guideline
{
    public const string PrioritizeVegetables = "PrioritizeVegetables";
    public const string Drink2LWater = "Drink2LWater";
    public const string AvoidSugaryDrinks = "AvoidSugaryDrinks";
    public const string ProteinAtBreakfast = "ProteinAtBreakfast";
    public const string ReduceSalt = "ReduceSalt";
    public const string EatEvery3To4Hours = "EatEvery3To4Hours";

    /// <summary>PR14.IA-A "Cena con proteína y vegetales".</summary>
    public const string ProteinAndVegetablesAtDinner = "ProteinAndVegetablesAtDinner";

    public const int MinimumCustomLength = 3;
    public const int MaximumCustomLength = 140;

    /// <summary>Business rule: At Most Five Custom Guidelines Per Version (NC-6).</summary>
    public const int MaximumCustomPerVersion = 5;

    private Guideline(string? code, string? custom)
    {
        Code = code;
        Custom = custom;
    }

    /// <summary>Every catalog code, in the order of EV-5.</summary>
    public static IReadOnlyList<string> Codes { get; } =
    [
        PrioritizeVegetables, Drink2LWater, AvoidSugaryDrinks, ProteinAtBreakfast, ReduceSalt, EatEvery3To4Hours,
        ProteinAndVegetablesAtDinner
    ];

    /// <summary>The catalog code, or null for a custom guideline.</summary>
    public string? Code { get; }

    /// <summary>The custom text, or null for a catalog guideline.</summary>
    public string? Custom { get; }

    public bool IsCustom => Custom is not null;

    /// <summary>A guideline of the catalog. Business rule: Closed Guideline Catalog (NC-6).</summary>
    public static Guideline FromCode(string code)
    {
        var match = Codes.FirstOrDefault(c => c.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match is null
            ? throw new ArgumentException(
                $"'{code}' is not a guideline of the catalog. Allowed: {string.Join(", ", Codes)}.", nameof(code))
            : new Guideline(match, null);
    }

    /// <summary>"Otra indicación": 3 to 140 characters.</summary>
    public static Guideline CustomText(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length is < MinimumCustomLength or > MaximumCustomLength)
            throw new ArgumentException(
                $"A custom guideline must have between {MinimumCustomLength} and {MaximumCustomLength} characters.",
                nameof(text));
        return new Guideline(null, trimmed);
    }

    /// <summary>
    ///     Stand-alone endpoints and legacy rows: a catalog code becomes that code, any other text a custom
    ///     guideline. Deprecated by NC-6 for new clients, which send codes and custom texts apart.
    /// </summary>
    public static Guideline FromLegacyText(string text)
    {
        var match = Codes.FirstOrDefault(c => c.Equals(text?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match is null ? CustomText(text!) : new Guideline(match, null);
    }

    /// <summary>
    ///     Rebuilds a stored guideline without validating it again: rows written before NC-6 may hold texts
    ///     outside today's limits, and history is read as it was written.
    /// </summary>
    public static Guideline Rehydrate(string? code, string? custom)
    {
        return code is not null ? new Guideline(code, null) : new Guideline(null, custom ?? string.Empty);
    }

    /// <summary>The code, or the custom text: the shape of the original free text list.</summary>
    public override string ToString()
    {
        return Code ?? Custom!;
    }
}

namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-6. A restriction of the plan, chosen from the closed list of EV-5 ("Puedes elegir varias"). Phase
///     11 removed "Otra restricción", so there is no free text restriction any more.
/// </summary>
/// <remarks>
///     The client translates the codes. Restrictions written as free text before NC-6 that match no code
///     are kept apart as legacy restrictions (see <c>NutritionPlan.LegacyRestrictions</c>), never lost.
/// </remarks>
public sealed record DietaryRestriction
{
    public const string LactoseFree = "LactoseFree";
    public const string GlutenFree = "GlutenFree";
    public const string Vegan = "Vegan";
    public const string Vegetarian = "Vegetarian";
    public const string TreeNutFree = "TreeNutFree";
    public const string ShellfishFree = "ShellfishFree";
    public const string Kosher = "Kosher";
    public const string Halal = "Halal";

    public DietaryRestriction(string value)
    {
        var match = All.FirstOrDefault(c => c.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a valid restriction. Allowed: {string.Join(", ", All)}.", nameof(value));
    }

    /// <summary>Every code, in the order of EV-5.</summary>
    public static IReadOnlyList<string> All { get; } =
        [LactoseFree, GlutenFree, Vegan, Vegetarian, TreeNutFree, ShellfishFree, Kosher, Halal];

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

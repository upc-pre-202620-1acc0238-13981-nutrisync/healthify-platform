namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     A restriction of the plan, chosen from the closed list of step 4, where several may
///     be chosen. Phase 11 removed the "other restriction" field, so there is no free text restriction any more.
/// </summary>
/// <remarks>
///     The client translates the codes. Restrictions written as free text before the closed list that match no code
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

    /// <summary>Every code, in the order of step 4.</summary>
    public static IReadOnlyList<string> All { get; } =
        [LactoseFree, GlutenFree, Vegan, Vegetarian, TreeNutFree, ShellfishFree, Kosher, Halal];

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

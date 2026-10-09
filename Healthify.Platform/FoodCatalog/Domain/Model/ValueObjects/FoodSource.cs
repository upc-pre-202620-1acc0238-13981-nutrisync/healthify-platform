namespace Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;

/// <summary>
///     IN-7. Where a reference food came from: translated from an external provider or seeded (<c>Imported</c>),
///     added by a practitioner (<c>LocalOverride</c>), or created from the nutrients the AI estimated for a dish
///     nothing else carried (<c>AiEstimated</c>).
/// </summary>
/// <remarks>
///     Traceability only. It is stored and never exposed in a resource nor in an ACL item: an estimated food is a
///     food like any other for the patient and for the practitioner. What it changes is which writes a food accepts
///     (an import never overwrites a local override nor an AI-estimated food).
/// </remarks>
public sealed record FoodSource
{
    public const string Imported = "Imported";
    public const string LocalOverride = "LocalOverride";
    public const string AiEstimated = "AiEstimated";

    public const int MaxLength = 20;

    private static readonly string[] Known = [Imported, LocalOverride, AiEstimated];

    public FoodSource(string value)
    {
        var match = Known.FirstOrDefault(k => string.Equals(k, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException($"Unknown food source '{value}'.", nameof(value));
    }

    public string Value { get; }

    public bool IsAiEstimated => Value == AiEstimated;

    public override string ToString()
    {
        return Value;
    }
}

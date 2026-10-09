namespace Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;

/// <summary>
///     The name a reference food carries inside this platform.
/// </summary>
/// <remarks>
///     Business rule: Taxonomy Translation Mandatory (Food Catalog, Subflow 6.1). This is the result
///     of the translation, never the provider's own label: whatever an external catalog calls a
///     product stops at the anti-corruption layer, and what continues into the domain is this.
/// </remarks>
public sealed record LocalName
{
    public const int MaxLength = 200;

    public LocalName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A reference food must carry a local name.", nameof(value));

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            throw new ArgumentException($"A local name cannot exceed {MaxLength} characters.", nameof(value));

        Value = trimmed;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

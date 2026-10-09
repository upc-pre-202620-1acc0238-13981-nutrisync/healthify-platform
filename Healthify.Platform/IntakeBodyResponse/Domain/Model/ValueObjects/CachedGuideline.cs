namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     NC-6. One guideline of the cached contract: a catalog code, which the device translates, or a custom
///     text written by the practitioner, which is clinical data and is never translated.
/// </summary>
/// <remarks>
///     This context does not interpret guidelines, so it does not validate the code against the catalog of
///     Nutritional Care: it keeps what the published contract carried.
/// </remarks>
public sealed record CachedGuideline
{
    public CachedGuideline(string? code, string? custom)
    {
        if (string.IsNullOrWhiteSpace(code) == string.IsNullOrWhiteSpace(custom))
            throw new ArgumentException("A guideline is either a catalog code or a custom text.", nameof(code));
        Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        Custom = string.IsNullOrWhiteSpace(custom) ? null : custom.Trim();
    }

    public string? Code { get; }
    public string? Custom { get; }

    /// <summary>The code, or the custom text: the shape of the original list of strings.</summary>
    public override string ToString()
    {
        return Code ?? Custom!;
    }
}

/// <summary>
///     NC-8. One line of "Qué cambió en esta versión" as the published contract carried it. This context does
///     not interpret it: the device writes the sentence from the type and the codes, offline (PT4).
/// </summary>
public sealed record CachedPlanChange
{
    public CachedPlanChange(string type, string? code, string? custom, string? macro, decimal? from, decimal? to)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("A plan change must say what changed.", nameof(type));
        Type = type.Trim();
        Code = code;
        Custom = custom;
        Macro = macro;
        From = from;
        To = to;
    }

    public string Type { get; }
    public string? Code { get; }
    public string? Custom { get; }
    public string? Macro { get; }
    public decimal? From { get; }
    public decimal? To { get; }
}

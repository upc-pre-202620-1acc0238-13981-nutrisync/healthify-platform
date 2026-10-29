namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     The protocol of a clinical measurement as the checklist of step 1: fasting, no shoes, light
///     clothing, empty bladder, same scale.
/// </summary>
/// <remarks>
///     Business rule: Measurement Protocol Recorded (Subflow 3.1), restated for the checklist: at least
///     one box must be ticked. It plays the role the free text <see cref="MeasurementProtocol" /> plays
///     on measurements taken before the guided consultation.
/// </remarks>
public sealed record MeasurementProtocolChecklist
{
    public const string Fasting = "Fasting";
    public const string NoShoes = "NoShoes";
    public const string LightClothing = "LightClothing";
    public const string EmptyBladder = "EmptyBladder";
    public const string SameScale = "SameScale";

    /// <summary>Every check, in the order step 1 shows them. The stored list follows this order.</summary>
    public static IReadOnlyList<string> All { get; } = [Fasting, NoShoes, LightClothing, EmptyBladder, SameScale];

    public MeasurementProtocolChecklist(IEnumerable<string>? checks)
    {
        var given = (checks ?? []).Select(c => c?.Trim() ?? string.Empty).ToList();
        if (given.Count == 0)
            throw new ArgumentException("At least one protocol check must be ticked.", nameof(checks));

        var unknown = given.Where(c => !All.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException(
                $"Unknown protocol checks: {string.Join(", ", unknown)}. Allowed: {string.Join(", ", All)}.",
                nameof(checks));

        Checks = All.Where(a => given.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>True for a code of the closed list, in any letter case.</summary>
    public static bool IsKnown(string? code)
    {
        return code is not null && All.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The ticked checks, without duplicates, in canonical order.</summary>
    public IReadOnlyList<string> Checks { get; }

    /// <summary>
    ///     Readable text kept in the historic <c>protocol</c> column, so readers of the free text protocol
    ///     never find it empty.
    /// </summary>
    public string ToSummary()
    {
        return string.Join(", ", Checks);
    }
}

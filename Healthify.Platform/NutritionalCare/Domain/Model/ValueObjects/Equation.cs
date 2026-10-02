namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     The published equation the practitioner chose for the basal metabolic rate. Which one to use
///     is irreducible clinical judgement, so the aggregate never picks it: it arrives in the command.
/// </summary>
public sealed record Equation
{
    public const string MifflinStJeor = "MifflinStJeor";
    public const string HarrisBenedict = "HarrisBenedict";
    public const string FaoWhoUnu = "FaoWhoUnu";
    public const string KatchMcArdle = "KatchMcArdle";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { MifflinStJeor, HarrisBenedict, FaoWhoUnu, KatchMcArdle };

    public Equation(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a supported equation. Allowed: {MifflinStJeor}, {HarrisBenedict}, " +
                $"{FaoWhoUnu}, {KatchMcArdle}.", nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    /// <summary>Katch-McArdle works from lean body mass, so it needs a body fat percentage.</summary>
    public bool RequiresBodyFatPercentage => Value == KatchMcArdle;

    public override string ToString()
    {
        return Value;
    }
}

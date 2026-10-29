namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     Which kind of signal opened a review item. Two come from Monitoring; the third is the scheduled recheck, a
///     reminder the practitioner set when they assigned an adjusted plan. None of them can change a plan.
/// </summary>
public sealed record SignalType
{
    public const string SustainedDeviation = "SustainedDeviation";
    public const string ConsistencyEscalation = "ConsistencyEscalation";

    /// <summary>
    ///     "Check again in 7 days" as an item of the inbox, opened by the clock when
    ///     <c>ResolvedAt + RecheckAfterDays</c> arrives.
    /// </summary>
    public const string ScheduledRecheck = "ScheduledRecheck";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { SustainedDeviation, ConsistencyEscalation, ScheduledRecheck };

    public SignalType(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid signal type. Allowed: {SustainedDeviation}, {ConsistencyEscalation}, " +
                $"{ScheduledRecheck}.", nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    /// <summary>Only a sustained deviation gets an AI plan proposal.</summary>
    public bool IsSustainedDeviation => Value == SustainedDeviation;

    public override string ToString()
    {
        return Value;
    }
}

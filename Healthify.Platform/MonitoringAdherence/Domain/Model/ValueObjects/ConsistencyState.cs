namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     Where the consistency index currently stands.
/// </summary>
/// <remarks>
///     Business rule: States Are Normal Watch Alert (Subflow 5.7). Three states, and none of them is
///     a judgement about the person. Alert means the two series disagree by more than the configured
///     threshold, which is a statement about data, not about character. What happens next is that
///     the patient is asked, not that anyone is told on.
/// </remarks>
public sealed record ConsistencyState
{
    public const string Normal = "Normal";
    public const string Watch = "Watch";
    public const string Alert = "Alert";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Normal, Watch, Alert };

    public ConsistencyState(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid consistency state. Allowed: {Normal}, {Watch}, {Alert}.",
                nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsNormal => Value == Normal;
    public bool IsWatch => Value == Watch;
    public bool IsAlert => Value == Alert;

    public override string ToString()
    {
        return Value;
    }
}

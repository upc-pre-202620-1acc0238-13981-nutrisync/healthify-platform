namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>NC-2/NC-3. Whether a consultation can still be resumed. It closes on publication.</summary>
public sealed record ConsultationState
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Abandoned = "Abandoned";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { InProgress, Completed, Abandoned };

    public ConsultationState(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid consultation state. Allowed: {InProgress}, {Completed}, {Abandoned}.",
                nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsInProgress => Value == InProgress;

    public override string ToString()
    {
        return Value;
    }
}

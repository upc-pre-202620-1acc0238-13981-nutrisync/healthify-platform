namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Whether the practitioner signed the proposal as it stood, or replaced its numbers.</summary>
public sealed record PrescriptionOutcome
{
    public const string AcceptedAsProposed = "AcceptedAsProposed";
    public const string Overridden = "Overridden";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { AcceptedAsProposed, Overridden };

    public PrescriptionOutcome(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid prescription outcome. Allowed: {AcceptedAsProposed}, {Overridden}.",
                nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsOverridden => Value == Overridden;

    public override string ToString()
    {
        return Value;
    }
}

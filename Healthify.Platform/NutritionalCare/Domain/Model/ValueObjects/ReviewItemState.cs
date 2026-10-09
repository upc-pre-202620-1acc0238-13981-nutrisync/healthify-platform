namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Where a review item sits in the inbox. Automation ends at Open; a human moves it.</summary>
public sealed record ReviewItemState
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { Open, Resolved };

    public ReviewItemState(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException($"'{value}' is not a valid review item state. Allowed: {Open}, {Resolved}.",
                nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsOpen => Value == Open;

    public override string ToString()
    {
        return Value;
    }
}

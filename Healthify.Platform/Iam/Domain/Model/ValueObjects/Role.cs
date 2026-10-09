namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>
///     The role a person holds in the platform. Patient and Practitioner never issue the same
///     commands, so this value decides what the rest of the platform will accept from the session.
/// </summary>
/// <remarks>
///     Hotspot from the event storming (Iam, Subflow 1.2):
///     TODO: can a single human hold both roles, for instance a nutritionist who is also another
///     practitioner patient? The event storming leaves this open. Assumed interpretation, the most
///     conservative one: one account holds exactly one role, declared at registration and immutable.
///     Holding both would mean two accounts. Source: event storming, Subflow 1.2 hotspot.
/// </remarks>
public sealed record Role
{
    public const string Patient = "Patient";
    public const string Practitioner = "Practitioner";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Patient, Practitioner };

    public Role(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid role. Allowed: {Patient}, {Practitioner}.", nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsPatient => Value == Patient;
    public bool IsPractitioner => Value == Practitioner;

    public override string ToString()
    {
        return Value;
    }
}

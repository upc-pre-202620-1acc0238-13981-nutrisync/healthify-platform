namespace Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

/// <summary>
///     Why a care link was revoked. A closed set: a link is revoked either because the patient
///     withdrew consent (Subflow 2.5) or because the patient switched practitioners (CR-1).
/// </summary>
/// <remarks>
///     It records which path ended the link, never a justification from the patient: the rule No
///     Justification Required (Subflow 2.5) still holds. The patient explains nothing in either case.
/// </remarks>
public sealed record RevocationReason
{
    public const string ConsentWithdrawnValue = "ConsentWithdrawn";
    public const string SwitchedPractitionerValue = "SwitchedPractitioner";

    // Declared before the instances below: static initializers run in textual order, and the
    // constructor reads this set.
    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { ConsentWithdrawnValue, SwitchedPractitionerValue };

    /// <summary>The patient withdrew consent (Subflow 2.5).</summary>
    public static readonly RevocationReason ConsentWithdrawn = new(ConsentWithdrawnValue);

    /// <summary>The patient redeemed another practitioner's invitation and confirmed the switch (CR-1).</summary>
    public static readonly RevocationReason SwitchedPractitioner = new(SwitchedPractitionerValue);

    public RevocationReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid revocation reason. Allowed: {ConsentWithdrawnValue}, " +
                $"{SwitchedPractitionerValue}.", nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

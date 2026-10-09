namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>
///     The shell the client application mounts for a session. One shell per session, derived from the
///     role claim: two navigation shells over a single project.
/// </summary>
public sealed record NavigationShell
{
    public const string PatientShell = "PatientShell";
    public const string PractitionerShell = "PractitionerShell";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { PatientShell, PractitionerShell };

    public NavigationShell(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid navigation shell. Allowed: {PatientShell}, {PractitionerShell}.",
                nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    /// <summary>The only shell a given role may mount.</summary>
    public static NavigationShell ForRole(Role role)
    {
        return new NavigationShell(role.IsPractitioner ? PractitionerShell : PatientShell);
    }

    public bool MatchesRole(Role role)
    {
        return Value == (role.IsPractitioner ? PractitionerShell : PatientShell);
    }

    public override string ToString()
    {
        return Value;
    }
}

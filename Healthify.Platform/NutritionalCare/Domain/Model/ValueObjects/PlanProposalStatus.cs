namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-10. Where an AI plan proposal stands: Proposed until a practitioner decides; AcceptedAsIs or
///     AcceptedWithEdits when they assign it ("Resolver" / "Asignar plan ajustado"); Dismissed when they resolve the
///     item without assigning it. Only Proposed moves.
/// </summary>
public sealed record PlanProposalStatus
{
    public const string Proposed = "Proposed";
    public const string AcceptedAsIs = "AcceptedAsIs";
    public const string AcceptedWithEdits = "AcceptedWithEdits";
    public const string Dismissed = "Dismissed";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { Proposed, AcceptedAsIs, AcceptedWithEdits, Dismissed };

    public PlanProposalStatus(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid plan proposal status. Allowed: {Proposed}, {AcceptedAsIs}, " +
                $"{AcceptedWithEdits}, {Dismissed}.", nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsProposed => Value == Proposed;

    public override string ToString()
    {
        return Value;
    }
}

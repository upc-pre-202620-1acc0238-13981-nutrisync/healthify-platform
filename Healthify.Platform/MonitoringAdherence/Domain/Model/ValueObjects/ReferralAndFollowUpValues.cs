namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     The speciality a patient is being sent to.
/// </summary>
/// <remarks>
///     Business rule: Specialty And Reason Required (Subflow 5.10). Free text on purpose. A closed
///     list of specialities would be a clinical taxonomy this platform has no business owning, and
///     the referral is read by a person, not matched by a machine.
/// </remarks>
public sealed record Specialty
{
    public const int MaximumLength = 120;

    public Specialty(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A referral must say which speciality it is to.", nameof(value));
        if (value.Trim().Length > MaximumLength)
            throw new ArgumentException($"A speciality cannot exceed {MaximumLength} characters.",
                nameof(value));

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     Why the patient is being referred.
/// </summary>
/// <remarks>
///     Business rule: Specialty And Reason Required (Subflow 5.10). A referral without a reason is a
///     dead end for whoever receives it, so the reason is part of the value rather than an optional
///     note beside it.
/// </remarks>
public sealed record ReferralReason
{
    public const int MaximumLength = 1000;

    public ReferralReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A referral must say why it is being made.", nameof(value));
        if (value.Trim().Length > MaximumLength)
            throw new ArgumentException($"A referral reason cannot exceed {MaximumLength} characters.",
                nameof(value));

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     Where a scheduled visit stands.
/// </summary>
/// <remarks>
///     Business rule: Missed Visit Does Not Close The Care Link (Subflow 5.10). <c>Missed</c> is the
///     end of this aggregate's story and nothing else's: no command anywhere reads it to revoke a
///     link, and there is no path from a missed visit to a discharge.
///     <c>Completed</c> is reached when a consultation of the guided flow is published for the visit
///     (MA-2, policy on Consultation Completed). <c>Cancelled</c> is reached by the practitioner or by a
///     discharge (MA-2/MA-5, CR-4); it is not a judgement on the patient either.
/// </remarks>
public sealed record FollowUpState
{
    public const string Scheduled = "Scheduled";
    public const string Completed = "Completed";
    public const string Missed = "Missed";
    public const string Cancelled = "Cancelled";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Scheduled, Completed, Missed, Cancelled };

    public FollowUpState(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid follow up state. Allowed: {Scheduled}, {Completed}, {Missed}, {Cancelled}.",
                nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsScheduled => Value == Scheduled;
    public bool IsCompleted => Value == Completed;
    public bool IsMissed => Value == Missed;
    public bool IsCancelled => Value == Cancelled;

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     MA-2. How the patient should prepare for the visit (PR17 "¿Cómo debe prepararse?", PT25.1 "CÓMO
///     PREPARARTE").
/// </summary>
/// <remarks>
///     A closed list on purpose: the patient reads these in their app as instructions their practitioner
///     chose, so each one has a fixed, reviewed wording on the client instead of free text.
/// </remarks>
public sealed record PreparationInstruction
{
    public const string Fasting = "Fasting";
    public const string LightClothing = "LightClothing";
    public const string BringBloodTests = "BringBloodTests";
    public const string EmptyBladder = "EmptyBladder";

    private static readonly string[] Allowed = [Fasting, LightClothing, BringBloodTests, EmptyBladder];

    public PreparationInstruction(string value)
    {
        var match = Allowed.FirstOrDefault(a => a.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a preparation instruction. Allowed: {string.Join(", ", Allowed)}.",
            nameof(value));
    }

    public string Value { get; }

    /// <summary>
    ///     The instructions of one visit: each one validated, duplicates removed, in the order given.
    ///     Null or empty means "sin indicaciones de preparación" (PT25.1.V).
    /// </summary>
    public static IReadOnlyList<PreparationInstruction> ListOf(IEnumerable<string>? values)
    {
        return (values ?? []).Select(v => new PreparationInstruction(v)).Distinct().ToList();
    }

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>MA-2. Whether the visit is in person (the default, PT25 "presencial") or remote.</summary>
public sealed record ConsultationModality
{
    public const string InPerson = "InPerson";
    public const string Remote = "Remote";

    private static readonly string[] Allowed = [InPerson, Remote];

    public ConsultationModality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Value = InPerson;
            return;
        }

        var match = Allowed.FirstOrDefault(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a consultation modality. Allowed: {string.Join(", ", Allowed)}.", nameof(value));
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

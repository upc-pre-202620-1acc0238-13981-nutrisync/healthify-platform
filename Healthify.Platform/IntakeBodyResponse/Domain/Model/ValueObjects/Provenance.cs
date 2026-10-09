namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     How an entry came to exist: from a photo, typed in by hand, or declared off plan.
/// </summary>
/// <remarks>
///     Business rule: Confidence And Provenance Always Exposed (Subflow 4.2). Provenance is not
///     metadata, it is part of the reading. A number that came from a photo estimate and a number the
///     patient typed carry different weight, and every read model of this context shows which is
///     which so that nobody has to guess.
///     <c>OffPlan</c> is not a lesser value. It exists so that declaring a meal outside the plan
///     costs one tap, because selective omission is the largest source of measurement error and the
///     antidote is making it cost nothing to say.
///     Since IN-1, <c>OffPlan</c> is a legacy value: whether a meal was in the plan is answered by
///     <see cref="PlanAdherence" />, and provenance describes only how the entry was logged. Rows
///     stored with it are still read, and only the deprecated off-plan endpoint and the legacy
///     synchronisation path still create them (see <c>DiaryEntry.LegacyOffPlan</c>).
/// </remarks>
public sealed record Provenance
{
    public const string Photo = "Photo";
    public const string Manual = "Manual";
    public const string OffPlan = "OffPlan";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Photo, Manual, OffPlan };

    public Provenance(string value)
    {
        // Business rule: Provenance Required (Subflows 4.2, 4.3 and 4.4)
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("An entry must declare its provenance.", nameof(value));
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid provenance. Allowed: {Photo}, {Manual}, {OffPlan}.", nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsPhoto => Value == Photo;
    public bool IsManual => Value == Manual;
    public bool IsOffPlan => Value == OffPlan;

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     Where an entry sits between the device that created it and this server.
/// </summary>
/// <remarks>
///     Business rules: Entry Stays Pending Without Connectivity and Command Queued In Outbox
///     (Subflow 4.6). An entry created online is <c>Synced</c> from the start. An entry that reaches
///     the server through the synchronisation batch was <c>Pending</c> on the device, is written as
///     such, and is moved to <c>Synced</c> as its own committed step, so that an interrupted batch
///     leaves a real queue behind instead of losing what it had already accepted.
/// </remarks>
public sealed record SyncState
{
    public const string Pending = "Pending";
    public const string Synced = "Synced";
    public const string Conflicted = "Conflicted";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Pending, Synced, Conflicted };

    public SyncState(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid sync state. Allowed: {Pending}, {Synced}, {Conflicted}.",
                nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsPending => Value == Pending;
    public bool IsSynced => Value == Synced;
    public bool IsConflicted => Value == Conflicted;

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     The patient's answer to «¿Esta comida estaba en tu plan?»: in the plan, off the plan, or not
///     answered yet.
/// </summary>
/// <remarks>
///     IN-1. Being off the plan is an attribute of an ordinary entry with a food and a portion, and
///     that entry counts towards the day's intake like any other. The answer is descriptive: it helps
///     the practitioner understand the week, and nothing computes a deviation or a penalty from it.
///     <c>NotAnswered</c> exists only for entries still waiting for the patient's confirmation and
///     for rows recorded before the question existed. Every confirmation made through the
///     interactive endpoints requires an answer, and once given it is never rewritten.
/// </remarks>
public sealed record PlanAdherence
{
    public const string InPlan = "InPlan";
    public const string OffPlan = "OffPlan";
    public const string NotAnswered = "NotAnswered";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { InPlan, OffPlan, NotAnswered };

    public PlanAdherence(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid plan adherence. Allowed: {InPlan}, {OffPlan}, {NotAnswered}.",
                nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsInPlan => Value == InPlan;
    public bool IsOffPlan => Value == OffPlan;
    public bool IsAnswered => Value != NotAnswered;

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     IN-6. Where an entry logged in a group came from: today only <c>MealIdea</c>, an idea of IA-3 the patient
///     chose to log («Registrar esta comida»). Null for every other entry.
/// </summary>
/// <remarks>Descriptive only: it qualifies nothing and changes nothing the day adds up to.</remarks>
public sealed record EntryOrigin
{
    public const string MealIdea = "MealIdea";

    public const int MaximumLength = 30;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { MealIdea };

    public EntryOrigin(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Allowed.Contains(value.Trim()))
            throw new ArgumentException($"'{value}' is not a valid entry origin. Allowed: {MealIdea}.",
                nameof(value));

        Value = Allowed.First(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsMealIdea => Value == MealIdea;

    public override string ToString()
    {
        return Value;
    }
}

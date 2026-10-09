namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-11. What raised a review item, as numbers the client turns into a sentence in its own language (PR13/PR14:
///     "registró en promedio 40 % menos de su meta de energía en 5 de sus últimos 9 días registrados"), instead of the
///     one-language <c>Evidence</c> text, which is kept.
/// </summary>
/// <remarks>
///     Evidence, not a verdict: a size, a direction and counts of logged days. Business rule: Only Logged Days Count
///     (Monitoring, Subflow 5.6): the denominator is logged days, never calendar days, so an unlogged day can never
///     read as a deviated one. Every field is optional because each signal type fills its own: a sustained deviation
///     the four first and the kcal (X-2), a scheduled recheck (NC-10) the counts since the adjustment and the date of
///     it, a consistency escalation (X-2) the index, its state, since when and for how many weeks it has been in alert
///     and when the patient acknowledged the prompt.
/// </remarks>
public sealed record ReviewItemEvidence
{
    public ReviewItemEvidence(
        decimal? averagePercentFromTarget,
        int? deviatedDays,
        int? loggedDaysConsidered,
        string? direction,
        DateOnly? adjustedOn = null,
        int? adjustedPlanVersion = null,
        decimal? averageEnergyKcalFromTarget = null,
        decimal? consistencyKgPerWeek = null,
        string? consistencyState = null,
        DateOnly? alertSinceOn = null,
        int? weeksInAlert = null,
        DateOnly? shownToPatientOn = null)
    {
        if (deviatedDays < 0)
            throw new ArgumentException("Deviated days cannot be negative.", nameof(deviatedDays));
        if (loggedDaysConsidered < 0)
            throw new ArgumentException("Logged days cannot be negative.", nameof(loggedDaysConsidered));
        // Business rule: Only Logged Days Count (Subflow 5.6). A deviated day is a logged day.
        if (deviatedDays is not null && loggedDaysConsidered is not null && deviatedDays > loggedDaysConsidered)
            throw new ArgumentException("Deviated days are logged days: they cannot exceed the logged days.",
                nameof(deviatedDays));
        if (direction is not null && direction != Above && direction != Below)
            throw new ArgumentException($"'{direction}' is not a direction. Allowed: {Above}, {Below}.",
                nameof(direction));
        if (adjustedPlanVersion <= 0)
            throw new ArgumentException("A plan version is positive.", nameof(adjustedPlanVersion));
        if (weeksInAlert < 0)
            throw new ArgumentException("Weeks in alert cannot be negative.", nameof(weeksInAlert));

        AveragePercentFromTarget = averagePercentFromTarget is null
            ? null
            : decimal.Round(averagePercentFromTarget.Value, 1);
        DeviatedDays = deviatedDays;
        LoggedDaysConsidered = loggedDaysConsidered;
        Direction = direction;
        AdjustedOn = adjustedOn;
        AdjustedPlanVersion = adjustedPlanVersion;
        AverageEnergyKcalFromTarget = averageEnergyKcalFromTarget is null
            ? null
            : decimal.Round(averageEnergyKcalFromTarget.Value, 1);
        ConsistencyKgPerWeek = consistencyKgPerWeek is null ? null : decimal.Round(consistencyKgPerWeek.Value, 3);
        ConsistencyState = consistencyState;
        AlertSinceOn = alertSinceOn;
        WeeksInAlert = weeksInAlert;
        ShownToPatientOn = shownToPatientOn;
    }

    public const string Above = "Above";
    public const string Below = "Below";

    /// <summary>Mean distance from the energy target, signed: −40 is 40 % below it.</summary>
    public decimal? AveragePercentFromTarget { get; }

    /// <summary>How many of the logged days deviated in <see cref="Direction" />.</summary>
    public int? DeviatedDays { get; }

    /// <summary>How many logged days were read. Unlogged days are not counted.</summary>
    public int? LoggedDaysConsidered { get; }

    /// <summary>Above or Below the target.</summary>
    public string? Direction { get; }

    /// <summary>NC-10. Scheduled recheck: the day the plan was adjusted ("tras ajuste del 8 sept.").</summary>
    public DateOnly? AdjustedOn { get; }

    /// <summary>NC-10. Scheduled recheck: the version assigned on that day.</summary>
    public int? AdjustedPlanVersion { get; }

    /// <summary>X-2. Sustained deviation: mean distance from the energy target in kcal per day, signed like the percent.</summary>
    public decimal? AverageEnergyKcalFromTarget { get; }

    /// <summary>X-2. Consistency escalation: unexplained weight movement, kg per week (the index).</summary>
    public decimal? ConsistencyKgPerWeek { get; }

    /// <summary>X-2. Consistency escalation: the state of the index (Alert).</summary>
    public string? ConsistencyState { get; }

    /// <summary>X-2. Consistency escalation: the day the index entered the alert.</summary>
    public DateOnly? AlertSinceOn { get; }

    /// <summary>X-2. Consistency escalation: whole weeks in alert when it was escalated.</summary>
    public int? WeeksInAlert { get; }

    /// <summary>X-2. Consistency escalation: the day the patient acknowledged the prompt (MA-7).</summary>
    public DateOnly? ShownToPatientOn { get; }

    /// <summary>
    ///     A sustained deviation, from what Monitoring publishes: <paramref name="magnitude" /> is the unsigned mean
    ///     fraction (0.40), so the sign comes from the direction. Null when the producer predates NC-11 (no counts).
    /// </summary>
    /// <remarks>X-2: <paramref name="energyKcal" /> is the unsigned kcal per day, null from producers before X-2.</remarks>
    public static ReviewItemEvidence? FromSustainedDeviation(decimal magnitude, string direction, int deviatedDays,
        int loggedDaysConsidered, decimal? energyKcal = null)
    {
        if (loggedDaysConsidered <= 0) return null;
        var normalized = string.Equals(direction, Above, StringComparison.OrdinalIgnoreCase) ? Above
            : string.Equals(direction, Below, StringComparison.OrdinalIgnoreCase) ? Below
            : direction;
        var sign = normalized == Above ? 1m : -1m;
        var percent = Math.Abs(magnitude) * 100m * sign;
        var kcal = energyKcal is null ? (decimal?)null : Math.Abs(energyKcal.Value) * sign;
        return new ReviewItemEvidence(percent, deviatedDays, loggedDaysConsidered, normalized,
            averageEnergyKcalFromTarget: kcal);
    }

    /// <summary>
    ///     X-2. A consistency escalation, from what Monitoring publishes. Null when the producer predates X-2 (no
    ///     state on the event): the English text evidence still opens the item.
    /// </summary>
    public static ReviewItemEvidence? FromConsistencyEscalation(decimal kgPerWeek, string? state,
        DateOnly? alertSinceOn, int? weeksInAlert, DateOnly? shownToPatientOn)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        return new ReviewItemEvidence(null, null, null, null, consistencyKgPerWeek: kgPerWeek,
            consistencyState: state, alertSinceOn: alertSinceOn, weeksInAlert: weeksInAlert,
            shownToPatientOn: shownToPatientOn);
    }
}

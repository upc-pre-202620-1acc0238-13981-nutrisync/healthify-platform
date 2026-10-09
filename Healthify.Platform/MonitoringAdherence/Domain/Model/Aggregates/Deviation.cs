using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     A distance between what was prescribed and what was recorded, large enough and repeated
///     enough to be worth a name.
/// </summary>
/// <remarks>
///     Business rules: Never Evaluated Under Seven Days and Only Logged Days Count (Subflow 5.6). The
///     second one is the ethical half. A deviation is computed from the days the patient actually
///     wrote in, and the days they did not write in are stepped over rather than read as zero
///     intake, because a silent day says nothing about eating and reading it as a bad day is how a
///     platform teaches people to stop logging.
///     Business rule: Sustained If Persists Across Majority Of Window (Subflow 5.6). One day off
///     target is not a deviation, it is a Tuesday. Only a deviation that persists across the
///     majority of the horizon becomes a signal, and only a sustained one ever leaves this context.
///     Nothing here is a verdict about a person. The aggregate records a direction, a size and how
///     many days it held, and there is no field on it that grades anybody.
/// </remarks>
public partial class Deviation
{
    /// <summary>Required by EF Core.</summary>
    protected Deviation()
    {
    }

    /// <summary>Subflow 5.6 - Detect Deviation. Built by <see cref="DetectFrom" />, which owns the rules.</summary>
    /// <param name="windowRef">The window this deviation was read from.</param>
    /// <param name="patientId">Whose window it is.</param>
    /// <param name="magnitude">How far from target the deviating days landed.</param>
    /// <param name="direction">Which way they landed.</param>
    /// <param name="loggedDaysConsidered">How many logged days the horizon held.</param>
    /// <param name="deviatingDaysConsidered">How many of them landed outside the band in this direction.</param>
    public Deviation(
        WindowId windowRef,
        int patientId,
        DeviationMagnitude magnitude,
        DeviationDirection direction,
        int loggedDaysConsidered,
        int deviatingDaysConsidered)
    {
        ArgumentNullException.ThrowIfNull(windowRef);
        ArgumentNullException.ThrowIfNull(magnitude);
        ArgumentNullException.ThrowIfNull(direction);

        // Business rule: Only Logged Days Count (Monitoring and Adherence, Subflow 5.6)
        if (loggedDaysConsidered <= 0)
            throw new ArgumentException("A deviation cannot be read from a horizon with no logged days.",
                nameof(loggedDaysConsidered));

        WindowRef = windowRef;
        PatientId = patientId;
        Magnitude = magnitude;
        Direction = direction;
        LoggedDaysConsidered = loggedDaysConsidered;
        DeviatingDaysConsidered = deviatingDaysConsidered;
        DetectedAt = DateTimeOffset.UtcNow;
        IsSustained = false;
    }

    public DeviationId Id { get; private set; } = null!;

    /// <summary>The window this deviation was read from.</summary>
    public WindowId WindowRef { get; private set; } = null!;

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. Copied from the window so that the
    ///     practitioner-facing read model can be answered without loading every window first.
    /// </summary>
    public int PatientId { get; private set; }

    // Persisted projection of the DeviationMagnitude value object.
    public decimal MagnitudeRelativeValue { get; private set; }
    public decimal MagnitudeEnergyKcal { get; private set; }

    public DeviationDirection Direction { get; private set; } = null!;

    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>Business rule: Sustained If Persists Across Majority Of Window (Subflow 5.6).</summary>
    public bool IsSustained { get; private set; }

    /// <summary>NOTE: technical field, not part of the domain model. When it became sustained.</summary>
    public DateTimeOffset? SustainedAt { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The two counts the majority rule was
    ///     decided with, kept on the row so the evidence sent to the review inbox is not recomputed
    ///     from a window that has moved on since.
    /// </summary>
    public int LoggedDaysConsidered { get; private set; }

    /// <summary>NOTE: technical field, not part of the domain model. See above.</summary>
    public int DeviatingDaysConsidered { get; private set; }

    /// <summary>Rebuilt from the stored columns.</summary>
    public DeviationMagnitude Magnitude
    {
        get => new(MagnitudeRelativeValue, MagnitudeEnergyKcal);
        private set
        {
            MagnitudeRelativeValue = value.RelativeValue;
            MagnitudeEnergyKcal = value.AbsoluteEnergyKcal;
        }
    }

    /// <summary>
    ///     Subflow 5.6 - Detect Deviation, rules and all.
    /// </summary>
    /// <remarks>
    ///     Business rule: Only Logged Days Count (Monitoring and Adherence, Subflow 5.6). The first
    ///     thing this method does is throw away the unlogged days, and the counts it keeps are counts
    ///     of logged days. A week in which the patient logged twice is a week with two days of
    ///     evidence, not five days of failure.
    ///     The direction is whichever way the majority of the deviating days went. When they are
    ///     evenly split there is no single deviation to report, and reporting the larger of two
    ///     opposite tendencies would be reading a pattern into noise.
    /// </remarks>
    /// <param name="windowRef">The window being read.</param>
    /// <param name="patientId">Whose window it is.</param>
    /// <param name="horizon">The days in the horizon, logged and unlogged alike.</param>
    /// <returns>The deviation, or null when the horizon does not hold one.</returns>
    public static Deviation? DetectFrom(WindowId windowRef, int patientId,
        IReadOnlyList<DailyCompliance> horizon)
    {
        var logged = horizon.Where(d => d.IsLogged).ToList();
        if (logged.Count == 0) return null;

        var above = logged.Where(d => d.Direction == DeviationDirection.Above).ToList();
        var below = logged.Where(d => d.Direction == DeviationDirection.Below).ToList();

        if (above.Count == below.Count) return null;

        var deviating = above.Count > below.Count ? above : below;
        var direction = new DeviationDirection(
            above.Count > below.Count ? DeviationDirection.Above : DeviationDirection.Below);

        var meanRelative = deviating.Average(d => Math.Abs(d.RelativeDeviation));
        var meanEnergy = deviating.Average(d => Math.Abs(d.ObservedEnergyKcal - d.TargetEnergyKcal));

        return new Deviation(windowRef, patientId, new DeviationMagnitude(meanRelative, meanEnergy),
            direction, logged.Count, deviating.Count);
    }

    /// <summary>Detect Deviation ran again over a horizon that has moved. The same deviation, restated.</summary>
    /// <param name="magnitude">The size just measured.</param>
    /// <param name="loggedDaysConsidered">How many logged days the horizon held.</param>
    /// <param name="deviatingDaysConsidered">How many of them deviated in this direction.</param>
    /// <returns>False when nothing changed, so nothing is published twice.</returns>
    public bool Restate(DeviationMagnitude magnitude, int loggedDaysConsidered,
        int deviatingDaysConsidered)
    {
        ArgumentNullException.ThrowIfNull(magnitude);

        if (MagnitudeRelativeValue == magnitude.RelativeValue
            && MagnitudeEnergyKcal == magnitude.AbsoluteEnergyKcal
            && LoggedDaysConsidered == loggedDaysConsidered
            && DeviatingDaysConsidered == deviatingDaysConsidered)
            return false;

        Magnitude = magnitude;
        LoggedDaysConsidered = loggedDaysConsidered;
        DeviatingDaysConsidered = deviatingDaysConsidered;
        DetectedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Subflow 5.6 - Flag Sustained Deviation.</summary>
    /// <remarks>
    ///     Business rule: Sustained If Persists Across Majority Of Window (Monitoring and Adherence,
    ///     Subflow 5.6). The majority is measured against the logged days, not against the calendar,
    ///     which is the same decision as everywhere else in this context: days nobody wrote in are
    ///     not evidence of anything and do not dilute the days that are.
    ///     A deviation becomes sustained once and stays sustained. The transition is what leaves this
    ///     context, and it leaves once.
    /// </remarks>
    /// <param name="sustainedRatio">From <c>Monitoring:SustainedDeviationRatio</c>.</param>
    /// <returns>True only on the transition, so the signal crosses the boundary exactly once.</returns>
    public bool MarkSustained(decimal sustainedRatio)
    {
        if (IsSustained) return false;
        if (LoggedDaysConsidered <= 0) return false;

        var ratio = (decimal)DeviatingDaysConsidered / LoggedDaysConsidered;
        if (ratio < sustainedRatio) return false;

        IsSustained = true;
        SustainedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    ///     What Monitoring observed, in one sentence, for the review inbox of the clinical context.
    /// </summary>
    /// <remarks>
    ///     Evidence, not a verdict. It states a direction, a size and a count, and it says nothing
    ///     about what the practitioner should do, because deciding that is the whole reason a person
    ///     sits at the other end of it.
    /// </remarks>
    public string Evidence()
    {
        var direction = Direction.IsAbove ? "above" : "below";
        return $"Mean energy {MagnitudeRelativeValue:P1} {direction} the prescribed target " +
               $"({MagnitudeEnergyKcal:0.#} kcal per day) on {DeviatingDaysConsidered} of " +
               $"{LoggedDaysConsidered} logged days in the evaluation horizon.";
    }
}

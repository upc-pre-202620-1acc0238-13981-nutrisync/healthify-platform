using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     The prescribed daily numbers as they stood at one moment in time, frozen.
/// </summary>
/// <remarks>
///     Business rules: Each Day Evaluated Against That Day Snapshot and Later Adjustment Never
///     Rewrites Evaluated Days (Subflow 5.2). A window keeps every snapshot it was ever given rather
///     than one current set of targets, because a plan adjusted on Thursday must not turn Monday
///     into a bad day retroactively. Interpretation only ever moves forward.
///     <see cref="TakenAt" /> is the moment these targets came into force, taken from the published
///     contract, not the moment this context happened to receive it. That is what makes the question
///     "which targets applied on this day" answerable: the snapshot in force on a day is the most
///     recent one whose <see cref="TakenAt" /> is not after it.
///     What is not here is the whole point. There is no diagnosis, no clinical rationale and no
///     calculation basis, because none of those ever leave the context that owns them.
/// </remarks>
/// <param name="PlanVersion">Version of the published contract this snapshot froze.</param>
/// <param name="EnergyKcal">Daily energy target in kilocalories.</param>
/// <param name="ProteinG">Daily protein target in grams.</param>
/// <param name="CarbG">Daily carbohydrate target in grams.</param>
/// <param name="FatG">Daily fat target in grams.</param>
/// <param name="TakenAt">The moment these targets came into force.</param>
public sealed record TargetsSnapshot(
    int PlanVersion,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    DateTimeOffset TakenAt)
{
    /// <summary>Version of the published contract this snapshot froze.</summary>
    public int PlanVersion { get; init; } = PlanVersion > 0
        ? PlanVersion
        : throw new ArgumentException("A targets snapshot must carry the plan version it froze.",
            nameof(PlanVersion));

    /// <summary>Daily energy target in kilocalories. A day cannot be evaluated without it.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal > 0m
        ? decimal.Round(EnergyKcal, 2)
        : throw new ArgumentException("A targets snapshot must carry a positive energy target.",
            nameof(EnergyKcal));

    /// <summary>Daily protein target in grams.</summary>
    public decimal ProteinG { get; init; } = decimal.Round(ProteinG, 2);

    /// <summary>Daily carbohydrate target in grams.</summary>
    public decimal CarbG { get; init; } = decimal.Round(CarbG, 2);

    /// <summary>Daily fat target in grams.</summary>
    public decimal FatG { get; init; } = decimal.Round(FatG, 2);

    /// <summary>The moment these targets came into force.</summary>
    public DateTimeOffset TakenAt { get; init; } = TakenAt;

    /// <summary>The first calendar day these targets can be used to evaluate.</summary>
    /// <remarks>
    ///     The day of the practice, not the UTC date: targets published at 20:00 in Lima are already
    ///     past midnight in UTC, and taking that date would leave the patient's own evening without
    ///     targets in force, so the day would never be evaluated.
    /// </remarks>
    /// <param name="calendar">The clinical calendar the days of the window are counted in.</param>
    public DateOnly EffectiveFromIn(IFollowUpCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return calendar.LocalDateOf(TakenAt);
    }
}

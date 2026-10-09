namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     IA-2/IA-4/IA-5. The slot of the day a diary entry falls in, from the clock of the patient's device
///     (<c>LocalTimestamp</c>): Breakfast, Lunch, Dinner or Other.
/// </summary>
/// <remarks>
///     DECISIÓN IA-2: the MD names the slots ("desayuno, almuerzo, cena, otros, según LocalTimestamp") but not their
///     hours. Assumed: Breakfast 05:00–10:59, Lunch 11:00–15:59, Dinner 18:00–22:59, Other the rest (snacks, late
///     night). Descriptive only: a slot never decides an outcome.
/// </remarks>
public static class MealSlot
{
    public const string Breakfast = "Breakfast";
    public const string Lunch = "Lunch";
    public const string Dinner = "Dinner";
    public const string Other = "Other";

    /// <summary>The three main slots, in the order of the day. Other is never "missing".</summary>
    public static IReadOnlyList<string> MainSlots { get; } = [Breakfast, Lunch, Dinner];

    /// <summary>The slot of a moment, read on the clock it was declared with.</summary>
    public static string Of(DateTimeOffset localTimestamp)
    {
        return localTimestamp.Hour switch
        {
            >= 5 and < 11 => Breakfast,
            >= 11 and < 16 => Lunch,
            >= 18 and < 23 => Dinner,
            _ => Other
        };
    }
}

/// <summary>IA-2/IA-4/IA-5. How many days of a period had at least one counted entry in each slot.</summary>
/// <param name="Breakfast">Days with something counted at breakfast.</param>
/// <param name="Lunch">Days with something counted at lunch.</param>
/// <param name="Dinner">Days with something counted at dinner.</param>
/// <param name="Other">Days with something counted outside the three main slots.</param>
public sealed record MealSlotDays(int Breakfast, int Lunch, int Dinner, int Other);

/// <summary>
///     IA-2/IA-4/IA-5. One calendar day of a period, as the AI input describes it: no entry, no food name, no photo.
/// </summary>
/// <param name="Date">The day the patient was living.</param>
/// <param name="Weekday">Mon … Sun.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged (MA-6; a day never evaluated is Unlogged).</param>
/// <param name="EnergyKcal">What the confirmed entries added up to; null on an unlogged day.</param>
/// <param name="TargetEnergyKcal">The energy target in force that day; null when the day was never evaluated.</param>
/// <param name="OffPlanEntryCount">Entries the patient answered as off the plan (descriptive, MA-1).</param>
/// <param name="Slots">The slots with at least one counted entry.</param>
public sealed record PeriodDayFact(
    DateOnly Date,
    string Weekday,
    string Outcome,
    decimal? EnergyKcal,
    decimal? TargetEnergyKcal,
    int OffPlanEntryCount,
    IReadOnlyList<string> Slots);

/// <summary>
///     IA-2/IA-4/IA-5. The facts of a period, computed without AI: the compliance of MA-6, the slots of the diary and
///     the home weight trend of IN-5. They are passed to the prompt as facts, and the output validator checks every
///     number of the generated text against them.
/// </summary>
/// <remarks>
///     Business rule: The AI Does Not Count (IA-2). "5 de 7 días", "registraste 6 de 7 días" and "tu peso bajó
///     0,3 kg" come from here, never from the model.
///     Business rule: Unlogged Is Not Non Compliant. The denominator is the calendar days of the period
///     (DECISIÓN §12-#7) and a day nobody wrote in is Unlogged, never Short.
///     There is no diagnosis, no calculation basis and no BMI here, and no single day's weight: only the change and
///     the slope of the smoothed series.
/// </remarks>
public sealed record MonitoringPeriodFacts
{
    public MonitoringPeriodFacts(DateOnly from, DateOnly to, int metDays, int exceededDays, int shortDays,
        int unloggedDays, int offPlanEntryCount, int offPlanDays, MealSlotDays mealSlotDays,
        IReadOnlyList<string> shortWeekdays, string? dominantMissingSlotOnShortDays, decimal? weightChangeKg,
        decimal? weightSlopeKgPerWeek, int selfWeighInCount, IReadOnlyList<PeriodDayFact> days)
    {
        if (to < from) throw new ArgumentException("The period ends before it starts.", nameof(to));
        var total = to.DayNumber - from.DayNumber + 1;
        if (new[] { metDays, exceededDays, shortDays, unloggedDays, offPlanEntryCount, offPlanDays, selfWeighInCount }
            .Any(n => n < 0))
            throw new ArgumentException("A count of a period cannot be negative.", nameof(metDays));
        if (metDays + exceededDays + shortDays + unloggedDays != total)
            throw new ArgumentException("Every calendar day of a period has exactly one outcome.", nameof(metDays));
        if (offPlanDays > total) throw new ArgumentException("More off-plan days than days.", nameof(offPlanDays));

        From = from;
        To = to;
        TotalDays = total;
        MetDays = metDays;
        ExceededDays = exceededDays;
        ShortDays = shortDays;
        UnloggedDays = unloggedDays;
        OffPlanEntryCount = offPlanEntryCount;
        OffPlanDays = offPlanDays;
        MealSlotDays = mealSlotDays ?? throw new ArgumentNullException(nameof(mealSlotDays));
        ShortWeekdays = shortWeekdays ?? [];
        DominantMissingSlotOnShortDays = dominantMissingSlotOnShortDays;
        WeightChangeKg = weightChangeKg is null ? null : decimal.Round(weightChangeKg.Value, 1);
        WeightSlopeKgPerWeek = weightSlopeKgPerWeek is null ? null : decimal.Round(weightSlopeKgPerWeek.Value, 1);
        SelfWeighInCount = selfWeighInCount;
        Days = days ?? [];
    }

    public DateOnly From { get; }
    public DateOnly To { get; }

    /// <summary>Calendar days of the period: the denominator of "5 de 7 días".</summary>
    public int TotalDays { get; }

    public int MetDays { get; }
    public int ExceededDays { get; }
    public int ShortDays { get; }

    /// <summary>Days without a confirmed entry ("Sin registro"), evaluated or not.</summary>
    public int UnloggedDays { get; }

    /// <summary>Days with something logged: Met, Exceeded or Short.</summary>
    public int LoggedDays => MetDays + ExceededDays + ShortDays;

    /// <summary>Entries answered as off the plan (descriptive, MA-1).</summary>
    public int OffPlanEntryCount { get; }

    /// <summary>Days with at least one entry off the plan.</summary>
    public int OffPlanDays { get; }

    public MealSlotDays MealSlotDays { get; }

    /// <summary>IA-5. The weekdays (Mon … Sun) that landed below the target ("el jueves y el sábado").</summary>
    public IReadOnlyList<string> ShortWeekdays { get; }

    /// <summary>
    ///     IA-5 ("sobre todo en la cena"). The main slot most often without a counted entry on the short days, or null
    ///     when there were no short days or none of them missed a slot.
    /// </summary>
    /// <remarks>
    ///     DECISIÓN IA-5: the diary published to this context carries no energy per entry, so "where the energy was
    ///     missing" is read as the slot that most often had nothing counted on those days.
    /// </remarks>
    public string? DominantMissingSlotOnShortDays { get; }

    /// <summary>Change of the smoothed home series over the period (IN-5), one decimal; null without a trend.</summary>
    public decimal? WeightChangeKg { get; }

    /// <summary>Slope of the smoothed home series (IN-5), kg per week, one decimal; null without a trend.</summary>
    public decimal? WeightSlopeKgPerWeek { get; }

    /// <summary>Points of the home series in the range of the trend.</summary>
    public int SelfWeighInCount { get; }

    /// <summary>Every calendar day of the period, oldest first.</summary>
    public IReadOnlyList<PeriodDayFact> Days { get; }

    /// <summary>
    ///     The facts of a period from the evaluations of MA-6, the counted entries of each day and the trend of IN-5.
    /// </summary>
    /// <param name="from">First day, included.</param>
    /// <param name="to">Last day, included.</param>
    /// <param name="evaluated">The evaluated days of the patient (any range; the period is cut here).</param>
    /// <param name="countedEntryTimes">Local timestamps of the entries counted towards the targets, per day.</param>
    /// <param name="weightChangeKg">IN-5 change over the range of the trend, or null.</param>
    /// <param name="weightSlopeKgPerWeek">IN-5 slope, or null.</param>
    /// <param name="selfWeighInCount">IN-5 points in the range.</param>
    public static MonitoringPeriodFacts Compute(DateOnly from, DateOnly to, IEnumerable<DailyCompliance> evaluated,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<DateTimeOffset>> countedEntryTimes, decimal? weightChangeKg,
        decimal? weightSlopeKgPerWeek, int selfWeighInCount)
    {
        ArgumentNullException.ThrowIfNull(evaluated);
        ArgumentNullException.ThrowIfNull(countedEntryTimes);

        var series = evaluated.ToList();
        // MA-6: one outcome per calendar day (a day never evaluated is Unlogged) and the same days counted.
        var summary = ComplianceSummary.Of(series, from, to);
        var outcomes = ComplianceSummary.DaysOf(series, from, to);
        var lastEvaluation = series
            .Where(d => d.Date >= from && d.Date <= to)
            .GroupBy(d => d.Date)
            .ToDictionary(g => g.Key, g => g.Last());

        var days = outcomes.Select(day =>
        {
            lastEvaluation.TryGetValue(day.Date, out var evaluation);
            var logged = day.Outcome != DailyCompliance.Unlogged;
            var slots = countedEntryTimes.TryGetValue(day.Date, out var times)
                ? times.Select(MealSlot.Of).Distinct()
                    .OrderBy(s => s == MealSlot.Other ? 3 : MealSlot.MainSlots.ToList().IndexOf(s)).ToList()
                : [];
            return new PeriodDayFact(day.Date, WeekdayOf(day.Date), day.Outcome,
                logged ? decimal.Round(evaluation?.ObservedEnergyKcal ?? 0m, 0) : null,
                evaluation is null ? null : decimal.Round(evaluation.TargetEnergyKcal, 0),
                logged ? evaluation?.OffPlanEntryCount ?? 0 : 0, slots);
        }).ToList();

        var shortDays = days.Where(d => d.Outcome == DailyCompliance.Short).ToList();
        var dominantMissing = shortDays.Count == 0
            ? null
            : MealSlot.MainSlots
                .Select(slot => (Slot: slot, Missing: shortDays.Count(d => !d.Slots.Contains(slot))))
                .Where(s => s.Missing > 0)
                .OrderByDescending(s => s.Missing)
                .Select(s => s.Slot)
                .FirstOrDefault();

        return new MonitoringPeriodFacts(from, to, summary.Met, summary.Exceeded, summary.Short, summary.Unlogged,
            days.Sum(d => d.OffPlanEntryCount), days.Count(d => d.OffPlanEntryCount > 0),
            new MealSlotDays(
                days.Count(d => d.Slots.Contains(MealSlot.Breakfast)),
                days.Count(d => d.Slots.Contains(MealSlot.Lunch)),
                days.Count(d => d.Slots.Contains(MealSlot.Dinner)),
                days.Count(d => d.Slots.Contains(MealSlot.Other))),
            shortDays.Select(d => d.Weekday).ToList(), dominantMissing, weightChangeKg, weightSlopeKgPerWeek,
            selfWeighInCount, days);
    }

    /// <summary>
    ///     Every number a generated text about this period may contain: the counts, the weight figures (absolute,
    ///     one decimal) and the calendar numbers of the period ("semana del 8 al 14").
    /// </summary>
    public IReadOnlySet<decimal> AllowedNumbers()
    {
        var numbers = new HashSet<decimal>
        {
            TotalDays, MetDays, ExceededDays, ShortDays, UnloggedDays, LoggedDays, OffPlanEntryCount, OffPlanDays,
            MealSlotDays.Breakfast, MealSlotDays.Lunch, MealSlotDays.Dinner, MealSlotDays.Other, SelfWeighInCount,
            ShortWeekdays.Count, From.Day, To.Day, From.Month, To.Month, From.Year, To.Year
        };
        if (WeightChangeKg is { } change) numbers.Add(Math.Abs(change));
        if (WeightSlopeKgPerWeek is { } slope) numbers.Add(Math.Abs(slope));
        return numbers;
    }

    /// <summary>The counts an "N de M días" sentence may name as its N (M is always <see cref="TotalDays" />).</summary>
    public IReadOnlySet<int> DayCounts()
    {
        return new HashSet<int> { MetDays, ExceededDays, ShortDays, UnloggedDays, LoggedDays, OffPlanDays,
            MealSlotDays.Breakfast, MealSlotDays.Lunch, MealSlotDays.Dinner, MealSlotDays.Other };
    }

    private static string WeekdayOf(DateOnly date)
    {
        return date.DayOfWeek.ToString()[..3];
    }
}

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     What one day looked like once the recorded intake was set beside the targets in force that day.
/// </summary>
/// <remarks>
///     Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Subflow 5.4). Look at
///     the four outcomes. Three of them describe eating; the fourth describes an empty diary, and it
///     is deliberately not one of the other three. Absence of data is not evidence of anything, and
///     a platform that files it as a bad day teaches people that not logging is safer than logging.
///     <c>Unlogged</c> days are excluded from every deviation calculation and never escalate.
///     The domain value the event storming names is the pair { Date, Outcome }, and that pair is all
///     the ACL contract of this context ever publishes. The remaining members are
///     NOTE: technical fields, not part of the domain model. They record what the day was actually
///     evaluated with, so that a plan adjusted later cannot silently change what this day meant, and
///     so that Detect Deviation can work from the stored series alone.
/// </remarks>
/// <param name="Date">The calendar day the patient was living, never the server day.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
/// <param name="ObservedEnergyKcal">What the confirmed entries of that day added up to.</param>
/// <param name="TargetEnergyKcal">The energy target in force that day.</param>
/// <param name="PlanVersion">Version of the contract the day was evaluated against.</param>
/// <param name="EntryCount">How many entries the diary held that day.</param>
/// <param name="EvaluatedAt">When this evaluation was made.</param>
/// <param name="OffPlanEntryCount">MA-1. How many of the day's entries the patient answered as off the plan.</param>
/// <param name="OffPlanEnergyKcal">MA-1. The part of the observed energy those entries confirmed.</param>
public sealed record DailyCompliance(
    DateOnly Date,
    string Outcome,
    decimal ObservedEnergyKcal,
    decimal TargetEnergyKcal,
    int PlanVersion,
    int EntryCount,
    DateTimeOffset EvaluatedAt,
    int OffPlanEntryCount = 0,
    decimal OffPlanEnergyKcal = 0m)
{
    public const string Met = "Met";
    public const string Exceeded = "Exceeded";
    public const string Short = "Short";
    public const string Unlogged = "Unlogged";

    /// <summary>
    ///     How far from the energy target a day may land and still count as met, as a fraction of
    ///     the target.
    /// </summary>
    /// <remarks>
    ///     TODO: ambiguity - the event storming names the three eating outcomes (met, exceeded,
    ///     short) but never says where the band between them lies. Interpretation assumed: a
    ///     symmetric ten per cent of the energy target, which is wide enough that ordinary portion
    ///     estimation error does not move a day out of Met. The value is NOT calibrated and it is
    ///     the same family of risk as the consistency threshold. Source: event storming v3,
    ///     section 5, Subflow 5.4, and technical document section 10.2 risk 1.
    /// </remarks>
    public const decimal ToleranceRatio = 0.10m;

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Met, Exceeded, Short, Unlogged };

    /// <summary>The calendar day the patient was living, never the server day.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>Met, Exceeded, Short or Unlogged.</summary>
    public string Outcome { get; init; } = Allowed.Contains(Outcome)
        ? Allowed.First(a => a.Equals(Outcome, StringComparison.OrdinalIgnoreCase))
        : throw new ArgumentException(
            $"'{Outcome}' is not a valid daily compliance outcome. Allowed: {Met}, {Exceeded}, {Short}, {Unlogged}.",
            nameof(Outcome));

    /// <summary>NOTE: technical field, not part of the domain model.</summary>
    public decimal ObservedEnergyKcal { get; init; } = decimal.Round(ObservedEnergyKcal, 2);

    /// <summary>NOTE: technical field, not part of the domain model.</summary>
    public decimal TargetEnergyKcal { get; init; } = decimal.Round(TargetEnergyKcal, 2);

    /// <summary>NOTE: technical field, not part of the domain model.</summary>
    public int PlanVersion { get; init; } = PlanVersion;

    /// <summary>NOTE: technical field, not part of the domain model.</summary>
    public int EntryCount { get; init; } = EntryCount;

    /// <summary>NOTE: technical field, not part of the domain model.</summary>
    public DateTimeOffset EvaluatedAt { get; init; } = EvaluatedAt;

    /// <summary>
    ///     MA-1. Descriptive only: helps the practitioner understand the week. It plays no part in the
    ///     outcome, which is decided on the total confirmed energy, and nothing computes a deviation
    ///     from it. Zero on days evaluated before MA-1 and on unlogged days.
    /// </summary>
    public int OffPlanEntryCount { get; init; } = OffPlanEntryCount;

    /// <summary>MA-1. Descriptive only, like <see cref="OffPlanEntryCount" />.</summary>
    public decimal OffPlanEnergyKcal { get; init; } = decimal.Round(OffPlanEnergyKcal, 2);

    /// <summary>A day the patient wrote something in. The one that counts towards a deviation.</summary>
    public bool IsLogged => Outcome != Unlogged;

    /// <summary>True when the day landed outside the tolerance band. Never true for an unlogged day.</summary>
    public bool IsOutsideBand => Outcome is Exceeded or Short;

    /// <summary>How far the day landed from its target, as a signed fraction of the target.</summary>
    public decimal RelativeDeviation => !IsLogged || TargetEnergyKcal <= 0m
        ? 0m
        : decimal.Round((ObservedEnergyKcal - TargetEnergyKcal) / TargetEnergyKcal, 4);

    /// <summary>Above or Below, or null when the day is inside the band or was never logged.</summary>
    public string? Direction => Outcome switch
    {
        Exceeded => DeviationDirection.Above,
        Short => DeviationDirection.Below,
        _ => null
    };

    /// <summary>
    ///     Subflow 5.4 - the whole of Compared Against That Day Snapshot, in one place.
    /// </summary>
    /// <remarks>
    ///     Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Subflow 5.4). The
    ///     first branch is the rule. The decision uses whether the diary holds anything at all, not
    ///     whether the recorded intake adds up to much: a patient who logged three meals and never
    ///     confirmed the photo estimates has recorded their day, and a day that added up to little
    ///     is a different fact from a day nobody wrote in.
    /// </remarks>
    /// <param name="date">The day the patient was living.</param>
    /// <param name="hasAnyEntry">Whether the diary holds anything at all for that day.</param>
    /// <param name="entryCount">How many entries the diary held.</param>
    /// <param name="observedEnergyKcal">What the confirmed entries added up to.</param>
    /// <param name="snapshot">The targets in force that day.</param>
    /// <param name="offPlanEntryCount">MA-1. Descriptive; does not move the outcome.</param>
    /// <param name="offPlanEnergyKcal">MA-1. Descriptive; does not move the outcome.</param>
    public static DailyCompliance Evaluate(
        DateOnly date,
        bool hasAnyEntry,
        int entryCount,
        decimal observedEnergyKcal,
        TargetsSnapshot snapshot,
        int offPlanEntryCount = 0,
        decimal offPlanEnergyKcal = 0m)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!hasAnyEntry)
            return new DailyCompliance(date, Unlogged, 0m, snapshot.EnergyKcal, snapshot.PlanVersion,
                0, DateTimeOffset.UtcNow);

        var tolerance = snapshot.EnergyKcal * ToleranceRatio;
        var difference = observedEnergyKcal - snapshot.EnergyKcal;

        var outcome = difference > tolerance
            ? Exceeded
            : difference < -tolerance
                ? Short
                : Met;

        // MA-1: the off-plan figures ride along as description. They are deliberately absent from the
        // decision above: eating off the plan is told to the practitioner, never scored.
        return new DailyCompliance(date, outcome, observedEnergyKcal, snapshot.EnergyKcal,
            snapshot.PlanVersion, entryCount, DateTimeOffset.UtcNow, offPlanEntryCount, offPlanEnergyKcal);
    }

    /// <summary>Whether two evaluations of the same day say the same thing. Used to stay idempotent.</summary>
    public bool SaysTheSameAs(DailyCompliance other)
    {
        return other.Date == Date
               && other.Outcome == Outcome
               && other.ObservedEnergyKcal == ObservedEnergyKcal
               && other.TargetEnergyKcal == TargetEnergyKcal
               && other.PlanVersion == PlanVersion
               && other.EntryCount == EntryCount
               && other.OffPlanEntryCount == OffPlanEntryCount
               && other.OffPlanEnergyKcal == OffPlanEnergyKcal;
    }
}

/// <summary>
///     RM-2 ("Cumplimiento 5 de 7 días"). The evaluated days of a range counted by outcome.
/// </summary>
/// <remarks>
///     Business rule: Unlogged Is Not Non Compliant. The denominator is the calendar days of the range, and a
///     day with no evaluation at all counts as Unlogged, never as Short: an absence of data is not a bad day.
///     DECISIÓN §12-#7: denominator of calendar days, with a separate "Sin registro" state.
///     MA-6: the range endpoint returns this summary next to the day by day list of <see cref="DaysOf" />.
/// </remarks>
public sealed record ComplianceSummary(int Met, int Exceeded, int Short, int Unlogged, int TotalDays)
{
    /// <summary>MA-6. The longest range read at once: a month.</summary>
    public const int MaximumRangeDays = 31;

    /// <summary>MA-6. Whether <paramref name="from" />..<paramref name="to" /> is a range that can be read.</summary>
    public static bool IsValidRange(DateOnly from, DateOnly to)
    {
        return to >= from && to.DayNumber - from.DayNumber + 1 <= MaximumRangeDays;
    }

    /// <summary>
    ///     MA-6. Every calendar day of the range, oldest first, with the outcome of its last evaluation. A day never
    ///     evaluated is Unlogged (PAC-2 paints it grey, "Sin registro"), never Short.
    /// </summary>
    public static IReadOnlyList<ComplianceDay> DaysOf(IEnumerable<DailyCompliance> days, DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentException("The range ends before it starts.", nameof(to));

        var outcomes = days
            .Where(d => d.Date >= from && d.Date <= to)
            .GroupBy(d => d.Date)
            .ToDictionary(g => g.Key, g => g.Last().Outcome);

        return Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(offset => from.AddDays(offset))
            .Select(date => new ComplianceDay(date, outcomes.GetValueOrDefault(date, DailyCompliance.Unlogged)))
            .ToList();
    }

    /// <summary>Days with something logged: Met, Exceeded or Short.</summary>
    public int Logged => Met + Exceeded + Short;

    /// <summary>
    ///     Counts <paramref name="days" /> between <paramref name="from" /> and <paramref name="to" />, both included.
    ///     When a day was evaluated twice (a window handover), its last evaluation counts.
    /// </summary>
    public static ComplianceSummary Of(IEnumerable<DailyCompliance> days, DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentException("The range ends before it starts.", nameof(to));

        var outcomes = days
            .Where(d => d.Date >= from && d.Date <= to)
            .GroupBy(d => d.Date)
            .Select(g => g.Last().Outcome)
            .ToList();
        var total = to.DayNumber - from.DayNumber + 1;
        var met = outcomes.Count(o => o == DailyCompliance.Met);
        var exceeded = outcomes.Count(o => o == DailyCompliance.Exceeded);
        var shortDays = outcomes.Count(o => o == DailyCompliance.Short);

        return new ComplianceSummary(met, exceeded, shortDays, total - met - exceeded - shortDays, total);
    }
}

/// <summary>MA-6. One calendar day of a range: the pair { Date, Outcome } this context publishes.</summary>
/// <param name="Date">The calendar day the patient was living.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged (also for a day never evaluated).</param>
public sealed record ComplianceDay(DateOnly Date, string Outcome);

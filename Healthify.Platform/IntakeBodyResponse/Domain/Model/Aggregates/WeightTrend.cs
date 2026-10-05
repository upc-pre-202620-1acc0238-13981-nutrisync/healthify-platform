using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

/// <summary>
///     The smoothed weight series of one patient. The unit this context publishes about body weight.
/// </summary>
/// <remarks>
///     Business rule: Daily Figure Never Exposed As Headline (Subflow 4.5). A single morning reading
///     moves several hundred grams with hydration, salt and the hour it was taken. Showing that
///     number as the headline turns ordinary noise into a weekly verdict, so what leaves this context
///     is the series, and the individual readings stay behind it as data.
///     Business rule: Only Protocol Compliant Weigh Ins Smooth The Trend (Subflow 4.5) is enforced by
///     <see cref="Recalculate" />, which receives every reading and does the excluding itself. Putting
///     the filter here rather than in the caller is what makes the rule a property of the model
///     instead of a habit of whoever calls it.
///     The patient is the root: one trend each, recalculated in place.
/// </remarks>
public partial class WeightTrend
{
    /// <summary>Readings averaged into each point. Seven days absorbs a weekly rhythm without hiding a change.</summary>
    public const int DefaultWindowSize = 7;

    private List<WeightTrendPoint> _points = [];

    /// <summary>Required by EF Core.</summary>
    protected WeightTrend()
    {
    }

    public WeightTrend(int patientId, int windowSize = DefaultWindowSize)
    {
        if (windowSize <= 0)
            throw new ArgumentException("The smoothing window must be positive.", nameof(windowSize));

        PatientId = patientId;
        WindowSize = windowSize;
        LastRecalculatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The patient is the aggregate root: one trend each.</summary>
    public int PatientId { get; private set; }

    public int WindowSize { get; private set; }
    public DateTimeOffset LastRecalculatedAt { get; private set; }

    /// <summary>The smoothed series, oldest first.</summary>
    public IReadOnlyList<WeightTrendPoint> Points => _points;

    public bool HasPoints => _points.Count > 0;

    /// <summary>RM-2/IN-5. Where the smoothed series went from <paramref name="from" /> on.</summary>
    public WeightTrendSummary SummarizeSince(DateOnly from)
    {
        return WeightTrendSummary.Of(_points, from);
    }

    /// <summary>
    ///     IN-5. The last <paramref name="weeks" /> weeks up to <paramref name="today" />: slope and change of the
    ///     smoothed points, and how many readings of those days stayed out of the smoothing.
    /// </summary>
    /// <param name="today">Last day of the range, inclusive.</param>
    /// <param name="weeks">Weeks the range spans; clamped to 1..52.</param>
    /// <param name="weighIns">Every reading this patient has recorded, the same input as <see cref="Recalculate" />.</param>
    /// <param name="protocol">The protocol in force; null means the default (fasted only).</param>
    public WeightTrendRange SummarizeLastWeeks(DateOnly today, int weeks, IEnumerable<SelfWeighIn> weighIns,
        SelfWeighInProtocol? protocol = null)
    {
        protocol ??= SelfWeighInProtocol.Default;
        var span = WeightTrendRange.ClampWeeks(weeks);
        var from = today.AddDays(-7 * span);

        // Business rule: Excluded Weigh Ins Are Kept As Data (Subflow 4.5). A count of the readings of the
        // range that did not smooth it; never which ones, never a verdict.
        var excluded = weighIns.Count(w => w.LocalDate >= from && w.LocalDate <= today
                                           && !w.FollowsProtocolUnder(protocol));

        var summary = WeightTrendSummary.Of(_points.Where(p => p.Date <= today), from);
        return new WeightTrendRange(from, today, span, excluded, summary);
    }

    /// <summary>
    ///     IA-2. The smoothed points of an explicit range of local days, <paramref name="from" /> to
    ///     <paramref name="to" /> (both included): slope, change and the readings of those days that stayed out of the
    ///     smoothing. Unlike <see cref="SummarizeLastWeeks" />, it does not depend on the day it is asked: the weekly
    ///     summary of a week says the same whenever it is generated.
    /// </summary>
    /// <param name="from">First local day, inclusive.</param>
    /// <param name="to">Last local day, inclusive.</param>
    /// <param name="weighIns">Every reading this patient has recorded, the same input as <see cref="Recalculate" />.</param>
    /// <param name="protocol">The protocol in force; null means the default (fasted only).</param>
    /// <exception cref="ArgumentException">When the range ends before it starts.</exception>
    public WeightTrendRange SummarizeBetween(DateOnly from, DateOnly to, IEnumerable<SelfWeighIn> weighIns,
        SelfWeighInProtocol? protocol = null)
    {
        if (to < from) throw new ArgumentException("The range ends before it starts.", nameof(to));
        protocol ??= SelfWeighInProtocol.Default;
        var weeks = (int)Math.Ceiling((to.DayNumber - from.DayNumber + 1) / 7m);

        // Business rule: Excluded Weigh Ins Are Kept As Data (Subflow 4.5). A count, never which ones.
        var excluded = weighIns.Count(w => w.LocalDate >= from && w.LocalDate <= to
                                           && !w.FollowsProtocolUnder(protocol));

        var summary = WeightTrendSummary.Of(_points.Where(p => p.Date <= to), from);
        return new WeightTrendRange(from, to, weeks, excluded, summary);
    }

    /// <summary>
    ///     Subflow 4.5 - Recalculate Weight Trend.
    /// </summary>
    /// <remarks>
    ///     Rebuilt from scratch on every run rather than appended to, because a late arriving reading
    ///     changes the points around it and an appended series would quietly disagree with its own
    ///     inputs.
    ///     The smoothing is a trailing mean over the last <see cref="WindowSize" /> readings. It is
    ///     deliberately the plainest thing that works: anyone can recompute a point by hand from the
    ///     readings, which matters more here than sensitivity does.
    /// </remarks>
    /// <param name="weighIns">Every reading this patient has recorded, in any order.</param>
    /// <param name="protocol">IN-3. The configured protocol; null means the default (fasted only).</param>
    /// <returns>The identifiers of the readings that were kept but did not smooth the trend.</returns>
    public IReadOnlyList<int> Recalculate(IReadOnlyList<SelfWeighIn> weighIns,
        SelfWeighInProtocol? protocol = null)
    {
        protocol ??= SelfWeighInProtocol.Default;

        // Business rules: Only Protocol Compliant Weigh Ins Smooth The Trend and Excluded Weigh Ins
        // Are Kept As Data (Subflow 4.5). Excluding a reading from the smoothing is the entire
        // consequence of falling outside the protocol. Nothing is deleted and nobody is told off.
        var excluded = weighIns.Where(w => !w.FollowsProtocolUnder(protocol)).Select(w => w.Id.Value).ToList();

        // One reading per day: the last one taken that day is the one that counts, because a second
        // reading on the same day is a correction, not a new observation.
        var daily = weighIns
            .Where(w => w.FollowsProtocolUnder(protocol))
            .GroupBy(w => w.LocalDate)
            .Select(g => new
            {
                Date = g.Key,
                ValueKg = g.OrderByDescending(w => w.LocalTimestamp).First().ValueKg
            })
            .OrderBy(d => d.Date)
            .ToList();

        var points = new List<WeightTrendPoint>(daily.Count);

        for (var i = 0; i < daily.Count; i++)
        {
            var from = Math.Max(0, i - WindowSize + 1);
            var window = daily.GetRange(from, i - from + 1);
            var mean = window.Sum(d => d.ValueKg) / window.Count;

            points.Add(new WeightTrendPoint(daily[i].Date, mean));
        }

        _points.Clear();
        _points.AddRange(points);
        LastRecalculatedAt = DateTimeOffset.UtcNow;

        return excluded;
    }
}

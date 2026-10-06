using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     The period over which what was prescribed is compared against what was recorded.
/// </summary>
/// <remarks>
///     One window per care relationship: it opens when the link is established and closes when the
///     link is revoked. Everything the interpretation needs lives on it, and it holds three series
///     that are deliberately never merged into one another: the targets that were in force at each
///     moment, the day-by-day outcome, and the clinical anthropometry.
///     Business rule: Later Adjustment Never Rewrites Evaluated Days (Subflow 5.2). Taking a new
///     snapshot appends; it does not touch a single day that has already been evaluated. A plan
///     adjusted on Thursday cannot turn Monday into a bad day retroactively, and the only way to
///     guarantee that is for the evaluation of Monday to carry the numbers it was made with.
///     Business rule: Gap Excluded From Deviation Calculation (Subflow 5.9). Unlogged days sit in
///     the series so the practitioner can see the silence, and every calculation in this class steps
///     over them.
///     The composite value objects are stored as flat columns or as JSON through their backing
///     fields and rebuilt by the computed properties below, following the pattern the platform
///     already uses.
/// </remarks>
public partial class EvaluationWindow
{
    /// <summary>
    ///     Invariant 1: no deviation is ever evaluated over a window shorter than this.
    /// </summary>
    /// <remarks>
    ///     A one-day deviation is not a deviation, it is a Tuesday. The configured window length may
    ///     be longer than this floor; it may never be shorter.
    /// </remarks>
    public const int MinimumDays = 7;

    private List<AnthropometryPoint> _anthropometrySeries = [];
    private List<DailyCompliance> _dailyComplianceSeries = [];
    private List<TargetsSnapshot> _targetsSnapshots = [];

    /// <summary>Required by EF Core.</summary>
    protected EvaluationWindow()
    {
    }

    /// <summary>Subflow 5.1 - Open Evaluation Window.</summary>
    /// <param name="command">Who the window is for, and which link opened it.</param>
    /// <param name="windowDays">The configured span, from <c>Monitoring:EvaluationWindowDays</c>.</param>
    /// <param name="from">The day the window starts counting.</param>
    public EvaluationWindow(OpenEvaluationWindowCommand command, int windowDays, DateOnly from)
    {
        // Business rule: Minimum Seven Day Window (Monitoring and Adherence, Subflow 5.1)
        if (windowDays < MinimumDays)
            throw new ArgumentException(
                $"An evaluation window is never shorter than {MinimumDays} days.", nameof(windowDays));

        PatientId = command.PatientId;
        CareLinkId = command.CareLinkId;
        WindowDays = windowDays;
        FromDate = from.ToDateTime(TimeOnly.MinValue);
        ToDate = from.AddDays(windowDays - 1).ToDateTime(TimeOnly.MinValue);
        State = new WindowState(WindowState.Open);
    }

    public WindowId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The link whose establishment opened
    ///     this window, kept so that a window can be traced back to the relationship it belongs to.
    /// </summary>
    public int CareLinkId { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The configured span this window was
    ///     opened with, kept on the row so that a later change of configuration cannot silently
    ///     reinterpret a window that is already running.
    /// </summary>
    public int WindowDays { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The wall-clock storage of
    ///     <see cref="From" />; the database has no calendar-date type that survives the round trip
    ///     untouched, so the day is stored at midnight and the calendar value is rebuilt below.
    /// </summary>
    public DateTime FromDate { get; private set; }

    /// <summary>NOTE: technical field, not part of the domain model. Storage of <see cref="To" />.</summary>
    public DateTime ToDate { get; private set; }

    public WindowState State { get; private set; } = null!;

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The last day a logging gap was
    ///     announced for, so that the time-driven policy of Subflow 5.9 says it once and not once
    ///     every twelve hours.
    /// </summary>
    public DateTime? LastLoggingGapFlaggedOn { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. When the patient was last reminded,
    ///     for the same reason.
    /// </summary>
    public DateTimeOffset? LastPatientRemindedAt { get; private set; }

    /// <summary>The first day this window counts.</summary>
    public DateOnly From => DateOnly.FromDateTime(FromDate);

    /// <summary>The last day this window counts. It grows as days are evaluated, and stops on closing.</summary>
    public DateOnly To => DateOnly.FromDateTime(ToDate);

    public bool IsOpen => State.IsOpen;

    /// <summary>Every snapshot this window was ever given, oldest first. Nothing is ever removed.</summary>
    public IReadOnlyList<TargetsSnapshot> TargetsSnapshots => _targetsSnapshots;

    /// <summary>The most recent snapshot, or null while no contract has been published.</summary>
    public TargetsSnapshot? TargetsSnapshot => _targetsSnapshots.Count == 0 ? null : _targetsSnapshots[^1];

    /// <summary>The day-by-day outcome, oldest first.</summary>
    public IReadOnlyList<DailyCompliance> DailyComplianceSeries => _dailyComplianceSeries;

    /// <summary>
    ///     The clinical weight series. The readings taken by the patient at home are not in here and
    ///     never will be.
    /// </summary>
    public IReadOnlyList<AnthropometryPoint> AnthropometrySeries => _anthropometrySeries;

    /// <summary>Business rule: Only Logged Days Count (Subflow 5.6).</summary>
    public int LoggedDaysCount => _dailyComplianceSeries.Count(d => d.IsLogged);

    /// <summary>What the window has seen so far, added up. Computed, never stored.</summary>
    public IntakeSummary IntakeSummary => IntakeSummary.From(_dailyComplianceSeries);

    /// <summary>Subflow 5.2 - Snapshot Active Targets.</summary>
    /// <remarks>
    ///     Business rule: Later Adjustment Never Rewrites Evaluated Days (Monitoring and Adherence,
    ///     Subflow 5.2). Note what this method does not do. It appends, and it touches nothing in the
    ///     daily series. Every day already evaluated keeps the numbers it was evaluated with.
    /// </remarks>
    /// <param name="snapshot">The targets that have just come into force.</param>
    /// <returns>False when this version is already the current snapshot, which keeps the policy idempotent.</returns>
    public bool TakeSnapshot(TargetsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (TargetsSnapshot is not null && TargetsSnapshot.PlanVersion == snapshot.PlanVersion)
            return false;

        _targetsSnapshots.Add(snapshot);
        _targetsSnapshots.Sort((a, b) => a.TakenAt.CompareTo(b.TakenAt));
        return true;
    }

    /// <summary>
    ///     Business rule: Each Day Evaluated Against That Day Snapshot (Monitoring and Adherence,
    ///     Subflow 5.2). The targets in force on a given day are the most recent ones that had
    ///     already come into force by then. A snapshot taken afterwards does not apply to it.
    /// </summary>
    /// <param name="date">The day being evaluated.</param>
    public TargetsSnapshot? SnapshotInForceOn(DateOnly date)
    {
        return _targetsSnapshots
            .Where(s => s.EffectiveFrom <= date)
            .OrderByDescending(s => s.TakenAt)
            .FirstOrDefault();
    }

    /// <summary>Subflow 5.3 - Append Anthropometry Point.</summary>
    /// <remarks>
    ///     Business rules: Clinical Measurement Outranks Self Weigh In and Two Series Never Merged
    ///     (Monitoring and Adherence, Subflow 5.3). The parameter type is the enforcement: an
    ///     <see cref="AnthropometryPoint" /> cannot be constructed with any source other than a
    ///     clinical measurement, so there is no way to reach this list with a bathroom-scale reading.
    ///     Two measurements on the same day means the later one corrects the earlier one.
    /// </remarks>
    /// <param name="point">The clinical reading to append.</param>
    /// <returns>False when the same reading is already there, which keeps the policy idempotent.</returns>
    public bool AppendAnthropometryPoint(AnthropometryPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var existing = _anthropometrySeries.FindIndex(p => p.Date == point.Date);
        if (existing >= 0)
        {
            if (_anthropometrySeries[existing].ValueKg == point.ValueKg) return false;
            _anthropometrySeries[existing] = point;
        }
        else
        {
            _anthropometrySeries.Add(point);
            _anthropometrySeries.Sort((a, b) => a.Date.CompareTo(b.Date));
        }

        return true;
    }

    /// <summary>Subflows 5.4 and 5.5 - the result of evaluating one day is written here.</summary>
    /// <remarks>
    ///     Business rule: Late Entry Re Evaluates Its Own Day Only (Monitoring and Adherence,
    ///     Subflow 5.5). One day goes in and one day comes out. There is no branch in this method
    ///     that touches any other date, which is what stops an entry arriving a week late from
    ///     rewriting the week around it.
    /// </remarks>
    /// <param name="day">The evaluation of a single day.</param>
    /// <returns>False when the day already said exactly this, so nothing is published twice.</returns>
    public bool RecordDayEvaluation(DailyCompliance day)
    {
        ArgumentNullException.ThrowIfNull(day);

        var existing = _dailyComplianceSeries.FindIndex(d => d.Date == day.Date);
        if (existing >= 0)
        {
            if (_dailyComplianceSeries[existing].SaysTheSameAs(day)) return false;
            _dailyComplianceSeries[existing] = day;
        }
        else
        {
            _dailyComplianceSeries.Add(day);
            _dailyComplianceSeries.Sort((a, b) => a.Date.CompareTo(b.Date));
        }

        // The window keeps counting for as long as it is open, so a day evaluated past its current
        // end moves the end rather than falling outside it.
        if (day.Date > To) ToDate = day.Date.ToDateTime(TimeOnly.MinValue);

        return true;
    }

    /// <summary>
    ///     Subflow 5.4 - the days nobody wrote anything in are marked, not left out.
    /// </summary>
    /// <remarks>
    ///     Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Monitoring and
    ///     Adherence, Subflow 5.4). The rule says a day without entries is marked, so something has
    ///     to mark it, and no event can announce a day in which nothing happened. This runs when a
    ///     later day is evaluated and fills the silence between them, so the series a practitioner
    ///     reads shows the gaps instead of hiding them, and every calculation in this context steps
    ///     over them rather than reading them as zero intake.
    ///     TODO: ambiguity - the event storming names the outcome but not what writes it.
    ///     Interpretation assumed: filling in on the next evaluated day, which is the least the rule
    ///     can mean and adds no schedule of its own. Days before the first published contract are
    ///     left out entirely, because a day with no targets in force was never evaluable.
    ///     Source: event storming v3, section 5, Subflow 5.4.
    /// </remarks>
    /// <param name="date">The day about to be evaluated. Everything before it is filled in.</param>
    /// <returns>The days that were just marked unlogged.</returns>
    public IReadOnlyList<DailyCompliance> MarkUnloggedDaysBefore(DateOnly date)
    {
        var added = new List<DailyCompliance>();
        if (_targetsSnapshots.Count == 0) return added;

        var firstEvaluable = _targetsSnapshots.Min(s => s.EffectiveFrom);
        var start = firstEvaluable < From ? From : firstEvaluable;

        for (var day = start; day < date; day = day.AddDays(1))
        {
            if (_dailyComplianceSeries.Any(d => d.Date == day)) continue;

            var snapshot = SnapshotInForceOn(day);
            if (snapshot is null) continue;

            added.Add(DailyCompliance.Evaluate(day, false, 0, 0m, snapshot));
        }

        if (added.Count == 0) return added;

        _dailyComplianceSeries.AddRange(added);
        _dailyComplianceSeries.Sort((a, b) => a.Date.CompareTo(b.Date));
        return added;
    }

    /// <summary>The evaluation of one day, or null when that day has never been evaluated.</summary>
    /// <param name="date">The day to look up.</param>
    public DailyCompliance? DayEvaluation(DateOnly date)
    {
        return _dailyComplianceSeries.FirstOrDefault(d => d.Date == date);
    }

    /// <summary>The last day this window has actually counted, given the day the caller stands on.</summary>
    /// <remarks>
    ///     Business rule: Closed Window Stops Counting Days (Monitoring and Adherence, Subflow 5.11).
    ///     A closed window counts through the day it closed, and an open one counts through today.
    ///     Note that this is deliberately not <see cref="To" />. <see cref="To" /> is where the window
    ///     is heading: it starts as the configured minimum span and moves out as days are evaluated.
    ///     Reading it as the last counted day would make a window look seven days old on the day it
    ///     opened, and invariant 1 would be satisfied by arithmetic rather than by time passing.
    /// </remarks>
    /// <param name="today">The day the caller is standing on.</param>
    public DateOnly CountingThrough(DateOnly today)
    {
        if (!IsOpen) return To;
        return today < From ? From : today;
    }

    /// <summary>How many days this window has actually spanned.</summary>
    /// <param name="today">The day the caller is standing on.</param>
    public int SpanDays(DateOnly today)
    {
        return CountingThrough(today).DayNumber - From.DayNumber + 1;
    }

    /// <summary>
    ///     Business rule: Never Evaluated Under Seven Days (Monitoring and Adherence, Subflow 5.6),
    ///     and invariant 1 of this bounded context.
    /// </summary>
    /// <param name="today">The day the caller is standing on.</param>
    public bool HasMinimumSpan(DateOnly today)
    {
        return SpanDays(today) >= Math.Max(MinimumDays, WindowDays);
    }

    /// <summary>
    ///     The most recent stretch of the window, which is what a deviation is judged over.
    /// </summary>
    /// <remarks>
    ///     TODO: hotspot (event storming 5, hotspot 4) - rolling window or fixed week? Interpretation
    ///     assumed: the aggregate spans the whole care relationship, and interpretation runs over a
    ///     rolling horizon of the configured length ending on the most recent counted day. A fixed
    ///     weekly window is easier for a practitioner to read; a rolling one is more sensitive and,
    ///     more importantly here, it is the only one that does not leave a patient flagged for
    ///     something they did two months ago. Parameter: Monitoring:EvaluationWindowDays.
    ///     Source: event storming v3, section 5, hotspot 4.
    /// </remarks>
    /// <param name="today">The day the caller is standing on.</param>
    public IReadOnlyList<DailyCompliance> HorizonDays(DateOnly today)
    {
        if (_dailyComplianceSeries.Count == 0) return [];

        var last = CountingThrough(today);
        var first = last.AddDays(-(Math.Max(MinimumDays, WindowDays) - 1));

        return _dailyComplianceSeries.Where(d => d.Date >= first && d.Date <= last).ToList();
    }

    /// <summary>The most recent day the patient wrote something in, or null when there is none.</summary>
    public DateOnly? LastLoggedDate()
    {
        return _dailyComplianceSeries.Where(d => d.IsLogged)
            .Select(d => (DateOnly?)d.Date)
            .DefaultIfEmpty(null)
            .Max();
    }

    /// <summary>Subflow 5.9 - Flag Logging Gap.</summary>
    /// <remarks>
    ///     Business rules: Gap Is Not A Deviation, Gap Excluded From Deviation Calculation and Gap
    ///     Never Escalates (Monitoring and Adherence, Subflow 5.9). All three are kept by what this
    ///     method does not do: it writes a date and nothing else. It creates no deviation, touches no
    ///     compliance day, and there is no field it could set that anything downstream escalates on.
    /// </remarks>
    /// <param name="asOf">The day the gap was noticed.</param>
    /// <returns>False when the gap has already been announced for that day.</returns>
    public bool FlagLoggingGap(DateOnly asOf)
    {
        if (LastLoggingGapFlaggedOn is not null &&
            DateOnly.FromDateTime(LastLoggingGapFlaggedOn.Value) >= asOf) return false;

        LastLoggingGapFlaggedOn = asOf.ToDateTime(TimeOnly.MinValue);
        return true;
    }

    /// <summary>Subflow 5.9 - Remind Patient.</summary>
    /// <remarks>
    ///     Business rule: Reminder Is Local And Non Accusatory (Monitoring and Adherence, Subflow
    ///     5.9). The tone lives in the localized message, and the reach lives here: this records that
    ///     the patient was reminded and publishes an event nothing outside this context subscribes to.
    /// </remarks>
    /// <returns>False when the patient has already been reminded about this gap.</returns>
    public bool RemindPatient()
    {
        if (LastLoggingGapFlaggedOn is null) return false;

        var flaggedFor = new DateTimeOffset(LastLoggingGapFlaggedOn.Value, TimeSpan.Zero);
        if (LastPatientRemindedAt is not null && LastPatientRemindedAt >= flaggedFor) return false;

        LastPatientRemindedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Subflow 5.11 - Close Evaluation Window.</summary>
    /// <remarks>
    ///     Business rules: Closed Window Stops Counting Days and Evaluated Data Is Preserved
    ///     (Monitoring and Adherence, Subflow 5.11). Nothing is deleted here and nothing is
    ///     recalculated. The window stops counting and everything it holds stays readable, because a
    ///     care relationship that ended is still a care relationship that happened.
    /// </remarks>
    /// <param name="closedAt">The moment the care link was revoked.</param>
    public void Close(DateTimeOffset closedAt)
    {
        if (!IsOpen) throw new InvalidOperationException("This evaluation window is already closed.");

        State = new WindowState(WindowState.Closed);
        ClosedAt = closedAt;

        var closedOn = DateOnly.FromDateTime(closedAt.UtcDateTime);
        ToDate = (closedOn < From ? From : closedOn).ToDateTime(TimeOnly.MinValue);
    }
}

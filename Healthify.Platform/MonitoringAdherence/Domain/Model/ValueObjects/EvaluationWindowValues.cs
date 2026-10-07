namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     Whether an evaluation window is still counting days.
/// </summary>
/// <remarks>
///     NOTE: the aggregate inventory lists <c>State</c> as a field of Evaluation Window without
///     naming its type. This is that field's type, not an additional concept.
///     Business rules: Closed Window Stops Counting Days and Evaluated Data Is Preserved
///     (Subflow 5.11), and Closed Windows Never Reopened (Subflow 5.5). A closed window is a
///     finished reading, not a deleted one.
/// </remarks>
public sealed record WindowState
{
    public const string Open = "Open";
    public const string Closed = "Closed";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Open, Closed };

    public WindowState(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid window state. Allowed: {Open}, {Closed}.", nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsOpen => Value == Open;
    public bool IsClosed => Value == Closed;

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     What the window has seen so far, added up.
/// </summary>
/// <remarks>
///     NOTE: the aggregate inventory lists <c>IntakeSummary</c> as a field of Evaluation Window
///     without naming its shape. This is that field's type, not an additional concept. It is
///     computed from the daily compliance series rather than stored, so it can never disagree with
///     the series it summarises.
///     Business rule: Gap Excluded From Deviation Calculation (Subflow 5.9). Logged and unlogged
///     days are counted separately and never added together, so that nothing downstream can treat a
///     silent week as a week of eating nothing.
/// </remarks>
/// <param name="LoggedDays">Days the patient wrote something in.</param>
/// <param name="UnloggedDays">Days with an empty diary. Never a deviation.</param>
/// <param name="TotalEnergyKcal">What the logged days added up to.</param>
/// <param name="MeanObservedEnergyKcal">Mean energy across the logged days.</param>
/// <param name="MeanTargetEnergyKcal">Mean target across the same logged days.</param>
public sealed record IntakeSummary(
    int LoggedDays,
    int UnloggedDays,
    decimal TotalEnergyKcal,
    decimal MeanObservedEnergyKcal,
    decimal MeanTargetEnergyKcal)
{
    /// <summary>Days the patient wrote something in.</summary>
    public int LoggedDays { get; init; } = LoggedDays;

    /// <summary>Days with an empty diary. Never a deviation.</summary>
    public int UnloggedDays { get; init; } = UnloggedDays;

    /// <summary>What the logged days added up to.</summary>
    public decimal TotalEnergyKcal { get; init; } = decimal.Round(TotalEnergyKcal, 2);

    /// <summary>Mean energy across the logged days.</summary>
    public decimal MeanObservedEnergyKcal { get; init; } = decimal.Round(MeanObservedEnergyKcal, 2);

    /// <summary>Mean target across the same logged days.</summary>
    public decimal MeanTargetEnergyKcal { get; init; } = decimal.Round(MeanTargetEnergyKcal, 2);

    /// <summary>An empty summary. What a window that has evaluated nothing yet reports.</summary>
    public static IntakeSummary Empty => new(0, 0, 0m, 0m, 0m);

    /// <summary>Adds up a series of evaluated days, counting only the logged ones towards intake.</summary>
    public static IntakeSummary From(IEnumerable<DailyCompliance> days)
    {
        var all = days.ToList();
        var logged = all.Where(d => d.IsLogged).ToList();

        if (logged.Count == 0)
            return new IntakeSummary(0, all.Count, 0m, 0m, 0m);

        return new IntakeSummary(
            logged.Count,
            all.Count - logged.Count,
            logged.Sum(d => d.ObservedEnergyKcal),
            logged.Sum(d => d.ObservedEnergyKcal) / logged.Count,
            logged.Sum(d => d.TargetEnergyKcal) / logged.Count);
    }
}

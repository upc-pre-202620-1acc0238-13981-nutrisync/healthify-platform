namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     What the on-device estimator thinks was eaten. A proposal, never a fact.
/// </summary>
/// <remarks>
///     Business rule: Estimate Stored As Proposal Only (Subflow 4.2). The estimator runs on the
///     patient device; this server persists what it proposed and nothing more. The proposal is never
///     promoted to intake on its own, and it is never removed once a confirmation exists beside it.
/// </remarks>
public sealed record ProposedEstimate
{
    public ProposedEstimate(int referenceFoodId, decimal portionGrams, Confidence confidence,
        DateTimeOffset estimatedAt)
    {
        // Business rule: Food Resolved From Local Catalog (Subflow 4.2). The identifier is checked
        // against the catalog by the command service; here it only has to be an identifier.
        if (referenceFoodId <= 0)
            throw new ArgumentException("An estimate must name a reference food.", nameof(referenceFoodId));
        if (portionGrams <= 0m)
            throw new ArgumentException("A portion must be greater than zero grams.", nameof(portionGrams));

        ReferenceFoodId = referenceFoodId;
        PortionGrams = decimal.Round(portionGrams, 2);
        Confidence = confidence;
        EstimatedAt = estimatedAt;
    }

    /// <summary>Cross-context reference to the Food Catalog. A plain int, resolved through the ACL.</summary>
    public int ReferenceFoodId { get; }

    public decimal PortionGrams { get; }

    /// <summary>Business rule: Confidence Always Attached (Subflow 4.2).</summary>
    public Confidence Confidence { get; }

    public DateTimeOffset EstimatedAt { get; }
}

/// <summary>
///     What the patient said was eaten, after seeing the proposal or instead of one.
/// </summary>
/// <remarks>
///     Business rule: Proposal Kept Alongside Confirmation (Subflow 4.2). This never replaces a
///     <see cref="ProposedEstimate" />: the two coexist on the same entry, which is what makes the
///     difference between what a model guessed and what a person said readable afterwards.
///     It carries no confidence, and that absence is the point. The patient is not a probabilistic
///     estimator, and attaching a number to their statement would invent precision nobody claimed.
/// </remarks>
public sealed record ConfirmedEstimate
{
    public ConfirmedEstimate(int referenceFoodId, decimal portionGrams, DateTimeOffset confirmedAt)
    {
        if (referenceFoodId <= 0)
            throw new ArgumentException("A confirmation must name a reference food.",
                nameof(referenceFoodId));
        if (portionGrams <= 0m)
            throw new ArgumentException("A portion must be greater than zero grams.", nameof(portionGrams));

        ReferenceFoodId = referenceFoodId;
        PortionGrams = decimal.Round(portionGrams, 2);
        ConfirmedAt = confirmedAt;
    }

    /// <summary>Cross-context reference to the Food Catalog. A plain int, resolved through the ACL.</summary>
    public int ReferenceFoodId { get; }

    public decimal PortionGrams { get; }
    public DateTimeOffset ConfirmedAt { get; }
}

/// <summary>
///     What the patient declared about the conditions of a self weigh-in.
/// </summary>
/// <remarks>
///     Business rules: Protocol Compliance Declared and Only Protocol Compliant Weigh Ins Smooth The
///     Trend (Subflow 4.5). The patient declares it; nothing here verifies it, and nothing here judges
///     it. A reading that falls outside the protocol is kept in full and simply does not smooth the
///     trend, because the day someone weighed themselves after lunch is data about their week, not a
///     failure.
///     IN-3: the protocol is now «¿Te pesaste en ayunas?» alone (PT12). Same time of day and same scale
///     are no longer asked: they are null on new readings and kept as declared on older ones.
/// </remarks>
public sealed record ProtocolCompliance
{
    public ProtocolCompliance(bool fastedState, bool? sameTimeOfDay = null, bool? sameScale = null)
    {
        FastedState = fastedState;
        SameTimeOfDay = sameTimeOfDay;
        SameScale = sameScale;
    }

    public bool FastedState { get; }

    /// <summary>IN-3: no longer asked. Null on readings recorded after IN-3.</summary>
    public bool? SameTimeOfDay { get; }

    /// <summary>IN-3: no longer asked. Null on readings recorded after IN-3.</summary>
    public bool? SameScale { get; }

    /// <summary>IN-3: under the default protocol, fasted is all it takes to smooth the trend.</summary>
    public bool FollowsProtocol => FollowsUnder(SelfWeighInProtocol.Default);

    /// <summary>Whether this declaration meets every condition <paramref name="protocol" /> asks for.</summary>
    public bool FollowsUnder(SelfWeighInProtocol protocol)
    {
        return protocol.IsFollowedBy(this);
    }
}

/// <summary>
///     IN-3. The conditions a self weigh-in must declare to smooth the trend. Read from
///     <c>Intake:SelfWeighInProtocol</c>; <see cref="Default" /> is fasted only.
/// </summary>
/// <remarks>
///     Business rule: Only Protocol Compliant Weigh Ins Smooth The Trend (Subflow 4.5). The protocol is a
///     list so that the questions removed by IN-3 can come back by configuration. A condition the
///     patient was not asked (null) is not met: a reading cannot follow a rule nobody put to it.
/// </remarks>
public sealed record SelfWeighInProtocol
{
    public const string Fasted = "Fasted";
    public const string SameTimeOfDay = "SameTimeOfDay";
    public const string SameScale = "SameScale";

    private static readonly string[] KnownConditions = [Fasted, SameTimeOfDay, SameScale];

    /// <summary>IN-3: «¿Te pesaste en ayunas?» and nothing else.</summary>
    public static readonly SelfWeighInProtocol Default = new([Fasted]);

    public SelfWeighInProtocol(IEnumerable<string> conditions)
    {
        var list = new List<string>();
        foreach (var raw in conditions ?? throw new ArgumentException("The protocol is required.",
                     nameof(conditions)))
        {
            var known = KnownConditions.FirstOrDefault(k =>
                string.Equals(k, raw?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (known is null)
                throw new ArgumentException($"Unknown protocol condition '{raw}'.", nameof(conditions));
            if (!list.Contains(known)) list.Add(known);
        }

        if (list.Count == 0)
            throw new ArgumentException("The protocol needs at least one condition.", nameof(conditions));

        Conditions = list;
    }

    /// <summary>The conditions in the order they were configured, each once.</summary>
    public IReadOnlyList<string> Conditions { get; }

    public bool IsFollowedBy(ProtocolCompliance declaration)
    {
        return Conditions.All(condition => condition switch
        {
            Fasted => declaration.FastedState,
            SameTimeOfDay => declaration.SameTimeOfDay == true,
            SameScale => declaration.SameScale == true,
            _ => false
        });
    }

    public bool Equals(SelfWeighInProtocol? other)
    {
        return other is not null && Conditions.SequenceEqual(other.Conditions);
    }

    public override int GetHashCode()
    {
        return Conditions.Aggregate(0, (hash, condition) => HashCode.Combine(hash, condition));
    }
}

/// <summary>
///     One point of the smoothed weight series.
/// </summary>
/// <remarks>
///     Business rule: Daily Figure Never Exposed As Headline (Subflow 4.5). The series is the unit
///     this context publishes. A single morning reading moves with hydration, salt and the hour, and
///     showing it as the headline turns normal noise into a verdict about the week.
/// </remarks>
public sealed record WeightTrendPoint
{
    public WeightTrendPoint(DateOnly date, decimal smoothedValueKg)
    {
        if (smoothedValueKg <= 0m)
            throw new ArgumentException("A trend point must carry a positive weight.",
                nameof(smoothedValueKg));

        Date = date;
        SmoothedValueKg = decimal.Round(smoothedValueKg, 2);
    }

    public DateOnly Date { get; }
    public decimal SmoothedValueKg { get; }
}

/// <summary>
///     RM-2/IN-5. Where the smoothed series went over a range: "−0,3 kg/sem · Tendencia · 4 semanas".
/// </summary>
/// <remarks>
///     Business rule: Daily Figure Never Exposed As Headline (Subflow 4.5). The summary is a slope and a change
///     between smoothed points, never the reading of a day. The slope is a simple least squares line over the
///     smoothed points against time in weeks; with fewer than two points there is no direction, so both
///     figures are null.
///     IN-5: the weight trend endpoint exposes it, over <see cref="WeightTrendRange" />, next to the excluded
///     readings count.
/// </remarks>
/// <param name="SlopeKgPerWeek">Kilograms per week, rounded to two decimals; negative means going down.</param>
/// <param name="ChangeKg">Last smoothed point minus the first one of the range.</param>
/// <param name="PointCount">How many smoothed points the range holds.</param>
public sealed record WeightTrendSummary(decimal? SlopeKgPerWeek, decimal? ChangeKg, int PointCount)
{
    private const decimal DaysPerWeek = 7m;

    /// <summary>The summary of <paramref name="points" /> dated on or after <paramref name="from" />.</summary>
    public static WeightTrendSummary Of(IEnumerable<WeightTrendPoint> points, DateOnly from)
    {
        var range = points.Where(p => p.Date >= from).OrderBy(p => p.Date).ToList();
        if (range.Count < 2) return new WeightTrendSummary(null, null, range.Count);

        var origin = range[0].Date.DayNumber;
        var xs = range.Select(p => (p.Date.DayNumber - origin) / DaysPerWeek).ToList();
        var ys = range.Select(p => p.SmoothedValueKg).ToList();
        var meanX = xs.Average();
        var meanY = ys.Average();
        var covariance = xs.Zip(ys, (x, y) => (x - meanX) * (y - meanY)).Sum();
        var variance = xs.Sum(x => (x - meanX) * (x - meanX));

        var slope = variance == 0m ? (decimal?)null : decimal.Round(covariance / variance, 2);
        return new WeightTrendSummary(slope, ys[^1] - ys[0], range.Count);
    }
}

/// <summary>
///     IN-5. The last weeks of the trend as PT13, PT20 and PAC-1 read them: «Resumen: subió 0,6 kg en 4 semanas» and
///     «Algunos registros no siguieron el protocolo y no están en esta línea, pero sí quedaron guardados».
/// </summary>
/// <remarks>
///     Business rule: Daily Figure Never Exposed As Headline (Subflow 4.5). Slope and change come from the smoothed
///     points of the range; there is no «peso de hoy» here.
///     Business rule: Excluded Weigh Ins Are Kept As Data (Subflow 4.5). <see cref="ExcludedReadingsCount" /> is
///     what lets the client explain the gap without naming a culprit: a count, not a list of readings.
/// </remarks>
/// <param name="From">First day of the range, inclusive.</param>
/// <param name="To">Last day of the range, inclusive.</param>
/// <param name="Weeks">How many weeks the range spans.</param>
/// <param name="ExcludedReadingsCount">Readings of the range that were kept but did not smooth the trend.</param>
/// <param name="Summary">Slope and change of the smoothed points of the range.</param>
public sealed record WeightTrendRange(
    DateOnly From,
    DateOnly To,
    int Weeks,
    int ExcludedReadingsCount,
    WeightTrendSummary Summary)
{
    public const int DefaultWeeks = 4;
    public const int MaxWeeks = 52;

    /// <summary>
    ///     DECISIÓN IN-5: a number of weeks outside 1..<see cref="MaxWeeks" /> is clamped rather than refused; the
    ///     query string is a view preference, not a clinical input.
    /// </summary>
    public static int ClampWeeks(int? weeks)
    {
        return Math.Clamp(weeks ?? DefaultWeeks, 1, MaxWeeks);
    }
}

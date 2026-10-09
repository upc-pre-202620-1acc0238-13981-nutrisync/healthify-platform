using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     How well the weight series and the recorded intake series agree with each other.
/// </summary>
/// <remarks>
///     The patient is the root: one index each, recalculated in place.
///     Invariant 2 of this bounded context, and business rules Patient First Always and Patient
///     Prompt Required Before Escalation (Subflows 5.7 and 5.8). The order is compiled in:
///     <see cref="Escalate" /> refuses to run while <see cref="ShownToPatientAt" /> is null, so a
///     practitioner can never learn about an alert the patient has not been asked about first. The
///     patient knows this escalation exists and is told before it happens. This is not covert
///     surveillance.
///     MA-7: issuing the prompt (<see cref="PromptIssuedAt" />) and the patient seeing it
///     (<see cref="ShownToPatientAt" />) are two moments. Only the second, acknowledged by the app when it painted
///     the card (PT3), counts as shown.
///     Business rule: Escalation Notifies Never Modifies The Plan (Subflow 5.8). Nothing on this
///     class references a plan, and the event it leads to opens an item in a human inbox.
///     Business rules: Both Series Required and No Index Without Both Series (Subflow 5.7).
///     <see cref="Recompute" /> returns false and writes nothing when either series is missing. An
///     index computed from one series would be a number with nothing to compare against, and a
///     number with nothing to compare against is the kind of thing people act on anyway.
/// </remarks>
public partial class ConsistencyIndex
{
    /// <summary>
    ///     The conventional energy content of a kilogram of body mass, used to turn a recorded
    ///     energy balance into an expected weight movement.
    /// </summary>
    /// <remarks>
    ///     The 7700 kcal per kilogram rule of thumb. It is an approximation and it is the same
    ///     approximation for everybody, which is what makes it usable as a consistency check rather
    ///     than as a prediction.
    /// </remarks>
    public const decimal EnergyKcalPerKg = 7700m;

    /// <summary>Required by EF Core.</summary>
    protected ConsistencyIndex()
    {
    }

    /// <summary>Subflow 5.7 - the index of one patient, before it has been computed for the first time.</summary>
    /// <param name="patientId">The patient this index belongs to.</param>
    public ConsistencyIndex(int patientId)
    {
        PatientId = patientId;
        Value = 0m;
        State = new ConsistencyState(ConsistencyState.Normal);
        LastRecomputedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The patient is the aggregate root: one index each.</summary>
    public int PatientId { get; private set; }

    /// <summary>
    ///     Unexplained weight movement, in kilograms per week.
    /// </summary>
    /// <remarks>
    ///     TODO: UNCALIBRATED - the threshold of the Consistency Index is the largest technical risk
    ///     in the project (technical document section 10.2, risk 1). Parameter:
    ///     Monitoring:ConsistencyAlertThreshold, shipped as 1.5 kilograms per week: Watch at the
    ///     threshold and Alert at twice it. That number is derived rather than validated, and
    ///     <see cref="NextState" /> is where the derivation is written down. Zero is still read as
    ///     "not configured", and then the value is computed and recorded and no alert is raised.
    ///     TODO: ambiguity - no document specifies the formula, only that the index compares the
    ///     weight trend against the recorded intake. Interpretation assumed: the distance between the
    ///     weight movement the smoothed trend actually shows and the movement the recorded surplus or
    ///     deficit against target implies, over the days both series cover, normalised per week. Note
    ///     the consequence, because it matters for calibration: a patient who follows the plan
    ///     exactly still produces a non-zero value equal to the weekly change the plan intends, since
    ///     the intended trajectory is part of the calculation basis and that never leaves the
    ///     clinical context. The threshold has to be calibrated above it.
    ///     Source: event storming v3, section 5, Subflow 5.7 and hotspot 1.
    /// </remarks>
    public decimal Value { get; private set; }

    public ConsistencyState State { get; private set; } = null!;

    /// <summary>When the index first left <c>Normal</c> in the episode it is currently in.</summary>
    public DateTimeOffset? FirstFlaggedAt { get; private set; }

    /// <summary>
    ///     Business rule: Prompt Date Recorded (Subflow 5.7). MA-7: when the patient acknowledged the prompt (the app
    ///     painted the card in PT3). Before MA-7 it was set when the prompt was issued.
    /// </summary>
    public DateTimeOffset? ShownToPatientAt { get; private set; }

    /// <summary>MA-7. When the prompt was issued for the current episode; the patient may not have seen it yet.</summary>
    public DateTimeOffset? PromptIssuedAt { get; private set; }

    /// <summary>MA-7. A prompt was issued and the patient has not acknowledged it: PT3 shows the card.</summary>
    public bool IsPatientPromptPending => PromptIssuedAt is not null && ShownToPatientAt is null;

    /// <summary>Business rule: Patient Prompt Required Before Escalation (Subflow 5.8).</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. When the current uninterrupted run in
    ///     <c>Alert</c> began, which is what the three-week rule of Subflow 5.8 is measured from.
    ///     <see cref="FirstFlaggedAt" /> cannot serve: it marks leaving Normal, and an index can sit
    ///     in Watch for a month before it reaches Alert.
    /// </summary>
    public DateTimeOffset? AlertSinceAt { get; private set; }

    /// <summary>NOTE: technical field, not part of the domain model.</summary>
    public DateTimeOffset LastRecomputedAt { get; private set; }

    public bool IsInAlert => State.IsAlert;

    /// <summary>
    ///     Subflow 5.7 - Recompute Consistency Index.
    /// </summary>
    /// <remarks>
    ///     Business rules: Both Series Required, No Index Without Both Series and States Are Normal
    ///     Watch Alert (Monitoring and Adherence, Subflow 5.7).
    ///     TODO: hotspot (event storming 5, hotspot 3) - what if the patient logs meals but never
    ///     weighs in? Interpretation assumed: no index at all. This method returns false and the
    ///     stored value is left as it was, so the read model shows the last thing that was true
    ///     rather than an index computed from half the evidence. The patient is not told off for it
    ///     and nothing escalates. Source: event storming v3, section 5, hotspot 3.
    ///     The same reading is applied to the period and not only to the existence of the two series.
    ///     They rarely cover the same stretch of calendar - the trend reaches back as far as the
    ///     patient has been weighing themselves, and the evaluated days begin when the care link was
    ///     established - so the calculation is confined to the overlap. Weight the patient moved
    ///     before anybody was recording their intake is movement no recorded intake can explain, and
    ///     subtracting one from the other across periods that do not match would report it as
    ///     unexplained, which is precisely the accusation this index exists not to make. Outside the
    ///     overlap there is half the evidence again, so the rule for it is the rule above.
    ///     The weight series is the smoothed trend of the readings the patient takes at home, which
    ///     is what this context is handed through the published contract of the recording context.
    ///     The clinical anthropometry series lives on the evaluation window and is deliberately not
    ///     mixed in here: Two Series Never Merged (Subflow 5.3) applies to this calculation too.
    /// </remarks>
    /// <param name="weightSeries">The smoothed weight trend, oldest first.</param>
    /// <param name="intakeSeries">The evaluated days. Unlogged days are stepped over.</param>
    /// <param name="alertThreshold">From <c>Monitoring:ConsistencyAlertThreshold</c>. Zero means unconfigured.</param>
    /// <returns>True when the index has just entered <c>Alert</c>, which is the only moment an alert is raised.</returns>
    public bool Recompute(
        IReadOnlyList<(DateOnly Date, decimal ValueKg)> weightSeries,
        IReadOnlyList<DailyCompliance> intakeSeries,
        decimal alertThreshold)
    {
        // Business rules: Both Series Required and No Index Without Both Series (Subflow 5.7)
        if (!CanCompute(weightSeries, intakeSeries)) return false;

        var ordered = weightSeries.OrderBy(p => p.Date).ToList();

        // Business rule: Only Logged Days Count (Subflow 5.6), applied here for the same reason: a
        // day nobody wrote in carries no energy balance and must not be read as one.
        var logged = intakeSeries.Where(d => d.IsLogged).ToList();
        if (logged.Count == 0) return false;

        // Business rules: Both Series Required and No Index Without Both Series (Subflow 5.7), read
        // as a statement about the period the two series share and not only about their existence.
        // Everything below this line lives inside that overlap.
        var overlapFrom = Later(ordered[0].Date, logged.Min(d => d.Date));
        var overlapTo = Earlier(ordered[^1].Date, logged.Max(d => d.Date));
        if (overlapTo < overlapFrom) return false;

        var trend = ordered.Where(p => p.Date >= overlapFrom && p.Date <= overlapTo).ToList();
        if (trend.Count < 2) return false;

        var first = trend[0];
        var last = trend[^1];

        // The two sides of the subtraction below have to describe the same days, so the energy
        // balance is taken between the trend endpoints rather than across the whole overlap.
        var shared = logged.Where(d => d.Date >= first.Date && d.Date <= last.Date).ToList();

        if (shared.Count == 0) return false;

        var observedChangeKg = last.ValueKg - first.ValueKg;
        var impliedChangeKg = shared.Sum(d => d.ObservedEnergyKcal - d.TargetEnergyKcal) / EnergyKcalPerKg;

        var spanDays = last.Date.DayNumber - first.Date.DayNumber + 1;
        var weeks = Math.Max(1m, spanDays / 7m);

        Value = decimal.Round(Math.Abs(observedChangeKg - impliedChangeKg) / weeks, 4);
        LastRecomputedAt = DateTimeOffset.UtcNow;

        return MoveTo(NextState(alertThreshold));
    }

    /// <summary>Business rules: Both Series Required and No Index Without Both Series (Subflow 5.7).</summary>
    /// <param name="weightSeries">The smoothed weight trend.</param>
    /// <param name="intakeSeries">The evaluated days.</param>
    public static bool CanCompute(
        IReadOnlyList<(DateOnly Date, decimal ValueKg)> weightSeries,
        IReadOnlyList<DailyCompliance> intakeSeries)
    {
        return weightSeries.Count >= 2 && intakeSeries.Any(d => d.IsLogged);
    }

    /// <summary>The later of two days. <see cref="DateOnly" /> carries no such comparison of its own.</summary>
    private static DateOnly Later(DateOnly a, DateOnly b)
    {
        return a > b ? a : b;
    }

    /// <summary>The earlier of two days.</summary>
    private static DateOnly Earlier(DateOnly a, DateOnly b)
    {
        return a < b ? a : b;
    }

    /// <summary>Subflow 5.7 - Prompt Patient.</summary>
    /// <remarks>
    ///     Business rules: Patient First Always and Prompt Date Recorded (Monitoring and Adherence,
    ///     Subflow 5.7). The date is recorded because it is the precondition of the escalation, not
    ///     because anybody is keeping count. Invitation Tone Never Accusation lives in the localized
    ///     message this prompt is rendered with, which is the only place a tone can live.
    /// </remarks>
    /// <returns>False when the patient has already been asked about this episode.</returns>
    /// <remarks>MA-7: issuing the prompt no longer counts as showing it; see <see cref="MarkShownToPatient" />.</remarks>
    public bool PromptPatient()
    {
        if (PromptIssuedAt is not null || ShownToPatientAt is not null) return false;

        PromptIssuedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    ///     MA-7 - The patient saw the prompt: the app painted the card in PT3 and acknowledged it. This is the date
    ///     Patient Prompt Required Before Escalation reads.
    /// </summary>
    /// <param name="at">When the card was shown.</param>
    /// <returns>False when it was already acknowledged in this episode (the first moment is kept).</returns>
    /// <exception cref="InvalidOperationException">When no prompt was issued in this episode.</exception>
    public bool MarkShownToPatient(DateTimeOffset at)
    {
        if (ShownToPatientAt is not null) return false;
        if (PromptIssuedAt is null)
            throw new InvalidOperationException("There is no prompt for the patient to acknowledge.");

        ShownToPatientAt = at;
        return true;
    }

    /// <summary>
    ///     MA-7. Whether at least <paramref name="days" /> days have passed since the patient saw the prompt, so they had
    ///     time to answer it before anybody else is told.
    /// </summary>
    public bool ShownToPatientAtLeast(int days, DateTimeOffset asOf)
    {
        return ShownToPatientAt is not null && asOf - ShownToPatientAt.Value >= TimeSpan.FromDays(Math.Max(0, days));
    }

    /// <summary>Business rules of Subflow 5.8, asked as one question.</summary>
    /// <param name="escalationWeeks">From <c>Monitoring:ConsistencyEscalationWeeks</c>.</param>
    /// <param name="asOf">The moment the caller is standing on.</param>
    public bool ThreeWeeksInAlertElapsed(int escalationWeeks, DateTimeOffset asOf)
    {
        if (!IsInAlert || AlertSinceAt is null) return false;
        return asOf - AlertSinceAt.Value >= TimeSpan.FromDays(7 * Math.Max(1, escalationWeeks));
    }

    /// <summary>Subflow 5.8 - Escalate To Practitioner.</summary>
    /// <remarks>
    ///     Business rules: Patient Prompt Required Before Escalation and Escalation Notifies Never
    ///     Modifies The Plan (Monitoring and Adherence, Subflow 5.8). The first guard below is the
    ///     first rule, in the aggregate rather than in a service, so no caller can skip it. The
    ///     second rule is kept by what this method does: it writes a date. Everything downstream of
    ///     it opens an item in a human inbox and stops there.
    /// </remarks>
    /// <returns>False when the escalation has already happened.</returns>
    public bool Escalate()
    {
        // Business rule: Patient Prompt Required Before Escalation (Monitoring and Adherence, 5.8)
        if (ShownToPatientAt is null)
            throw new InvalidOperationException(
                "The patient is asked before the practitioner is told. This alert has not been shown yet.");

        if (EscalatedAt is not null) return false;

        EscalatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    ///     X-2. Whole weeks the index has been in alert at <paramref name="asOf" />, for the evidence the client words
    ///     in its language ("en alerta desde hace 3 semanas"). Null when it is not in alert.
    /// </summary>
    public int? WeeksInAlertAt(DateTimeOffset asOf)
    {
        if (!IsInAlert || AlertSinceAt is null) return null;
        return Math.Max(0, (int)Math.Floor((asOf - AlertSinceAt.Value).TotalDays / 7));
    }

    /// <summary>
    ///     What Monitoring observed, in one sentence, for the review inbox of the clinical context.
    /// </summary>
    /// <remarks>X-2: an English sentence kept as the legacy fallback; the event also carries its numbers.</remarks>
    public string Evidence()
    {
        var shown = ShownToPatientAt?.ToString("yyyy-MM-dd") ?? "not shown";
        return $"Consistency index {Value:0.###} kg per week of unexplained weight movement, in " +
               $"{State.Value} since {AlertSinceAt:yyyy-MM-dd}. Shown to the patient on {shown}.";
    }

    /// <summary>
    ///     Business rule: States Are Normal Watch Alert (Subflow 5.7), and Alert Threshold, which is
    ///     the uncalibrated one.
    /// </summary>
    /// <remarks>
    ///     TODO: UNCALIBRATED - the threshold of the Consistency Index is the largest technical risk
    ///     in the project (technical document section 10.2, risk 1). Two bands are needed and one
    ///     number is configured, so Watch begins at the threshold and Alert at twice it. That second
    ///     relationship is an assumption, not a finding.
    ///     Where the shipped 1.5 comes from, since the number has to start somewhere: an adherent
    ///     patient does not score zero. Their recorded intake sits on target, so the implied change in
    ///     <see cref="Recompute" /> is zero while their weight still moves at the rate the plan
    ///     intends, and the index comes out equal to that rate. What bounds that rate is the deficit
    ///     strategy of the clinical context: its largest admissible deficit is 1500 kcal a day, which
    ///     is 1500 * 7 / 7700 = 1.36 kg per week. 1.5 is that worst case plus a margin for the noise
    ///     the endpoints of the smoothed trend still carry, and so it is the lowest threshold that
    ///     cannot flag an adherent patient under any plan this platform is able to issue. That makes
    ///     it a floor to escalate from and not a calibration: the calibrated number is the one the
    ///     cohort's own distribution of this value produces, and until that exists this is an
    ///     argument rather than evidence.
    ///     A threshold of zero or less is read as not configured. The index is still computed and
    ///     recorded, and the state stays Normal, so nothing is ever raised against a patient on the
    ///     strength of a number nobody has validated. Parameter: Monitoring:ConsistencyAlertThreshold.
    /// </remarks>
    private string NextState(decimal alertThreshold)
    {
        if (alertThreshold <= 0m) return ConsistencyState.Normal;
        if (Value >= alertThreshold * 2m) return ConsistencyState.Alert;
        return Value >= alertThreshold ? ConsistencyState.Watch : ConsistencyState.Normal;
    }

    /// <returns>True only when the index has just entered <c>Alert</c>.</returns>
    private bool MoveTo(string nextState)
    {
        var wasInAlert = State.IsAlert;
        var previous = State.Value;
        State = new ConsistencyState(nextState);

        if (State.IsNormal)
        {
            // The episode is over. The dates that belong to it are cleared so that a later episode
            // is measured from its own beginning and the patient is asked again rather than assumed
            // to have already been asked.
            FirstFlaggedAt = null;
            AlertSinceAt = null;
            ShownToPatientAt = null;
            PromptIssuedAt = null;
            return false;
        }

        if (previous == ConsistencyState.Normal) FirstFlaggedAt = DateTimeOffset.UtcNow;
        if (State.IsAlert && !wasInAlert) AlertSinceAt = DateTimeOffset.UtcNow;
        if (!State.IsAlert) AlertSinceAt = null;

        return State.IsAlert && !wasInAlert;
    }
}

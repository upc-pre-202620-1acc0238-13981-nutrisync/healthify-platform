namespace Healthify.Platform.ReadModels.Interfaces.REST.Resources;

/// <summary>
///     One entry of the diary, as the practitioner reads it. Read model Patient Monitoring Panel.
/// </summary>
/// <remarks>
///     Confidence and provenance are on this resource and there is no shape of it that omits them. A
///     portion a model guessed from a photo and a portion the patient typed are different kinds of
///     evidence, and a panel that showed them as the same number would invite the reader to mistake a
///     guess for a measurement.
///     The proposed and the confirmed halves travel side by side rather than collapsed into one
///     reading, because what a model said and what a person said are separate facts.
///     There is nothing on this resource that qualifies the entry: no compliance flag, no deviation
///     and no grade. And there is no counterpart that writes: the practitioner reads the diary and
///     interprets it, and no command anywhere lets them change it.
/// </remarks>
/// <param name="DiaryEntryId">Identifier of the entry.</param>
/// <param name="LocalTimestamp">The moment the patient declared, exactly as their device gave it.</param>
/// <param name="Provenance">Photo, Manual or OffPlan.</param>
/// <param name="SyncState">Pending, Synced or Conflicted.</param>
/// <param name="ProposedReferenceFoodId">What the estimator proposed, or null while it has not run.</param>
/// <param name="ProposedFoodName">Local name of the proposed food, or null.</param>
/// <param name="ProposedPortionGrams">The proposed portion in grams, or null.</param>
/// <param name="ProposedConfidence">How sure the estimator was, between 0 and 1, or null.</param>
/// <param name="ConfirmedReferenceFoodId">What the patient confirmed, or null while they have not.</param>
/// <param name="ConfirmedFoodName">Local name of the confirmed food, or null.</param>
/// <param name="ConfirmedPortionGrams">The confirmed portion in grams, or null.</param>
/// <param name="ConfirmedAt">When the patient spoke, or null.</param>
/// <param name="FoodName">RM-3. The food the entry shows: the confirmed one, or the proposed one while it waits.</param>
/// <param name="PlanAdherence">RM-3 (IN-1). InPlan, OffPlan or NotAnswered.</param>
/// <param name="IsCountedTowardsTargets">RM-3 (IN-1). Whether it adds to the day ("Confirmada").</param>
public record PanelDiaryEntryResource(
    int DiaryEntryId,
    DateTimeOffset LocalTimestamp,
    string Provenance,
    string SyncState,
    int? ProposedReferenceFoodId,
    string? ProposedFoodName,
    decimal? ProposedPortionGrams,
    decimal? ProposedConfidence,
    int? ConfirmedReferenceFoodId,
    string? ConfirmedFoodName,
    decimal? ConfirmedPortionGrams,
    DateTimeOffset? ConfirmedAt,
    string? FoodName = null,
    string PlanAdherence = "NotAnswered",
    bool IsCountedTowardsTargets = false)
{
    /// <summary>Identifier of the entry.</summary>
    public int DiaryEntryId { get; init; } = DiaryEntryId;

    /// <summary>The moment the patient declared, never rewritten by this server.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>
    ///     Photo, Manual or OffPlan. Always in plain sight: OffPlan is not a lesser value, it exists
    ///     so that saying so costs one tap.
    /// </summary>
    public string Provenance { get; init; } = Provenance;

    /// <summary>Pending, Synced or Conflicted. An entry that arrived late is still an entry.</summary>
    public string SyncState { get; init; } = SyncState;

    /// <summary>What the estimator proposed, or null while it has not run.</summary>
    public int? ProposedReferenceFoodId { get; init; } = ProposedReferenceFoodId;

    /// <summary>Local name of the proposed food, or null.</summary>
    public string? ProposedFoodName { get; init; } = ProposedFoodName;

    /// <summary>The proposed portion in grams, or null.</summary>
    public decimal? ProposedPortionGrams { get; init; } = ProposedPortionGrams;

    /// <summary>
    ///     How sure the estimator was, between 0 and 1. Always beside the proposal it qualifies,
    ///     never hidden and never rounded away.
    /// </summary>
    public decimal? ProposedConfidence { get; init; } = ProposedConfidence;

    /// <summary>What the patient confirmed, or null while they have not.</summary>
    public int? ConfirmedReferenceFoodId { get; init; } = ConfirmedReferenceFoodId;

    /// <summary>Local name of the confirmed food, or null.</summary>
    public string? ConfirmedFoodName { get; init; } = ConfirmedFoodName;

    /// <summary>The confirmed portion in grams, or null. Only this half counts as intake.</summary>
    public decimal? ConfirmedPortionGrams { get; init; } = ConfirmedPortionGrams;

    /// <summary>
    ///     When the patient spoke, or null. An estimate nobody confirmed stays unconfirmed: nothing
    ///     on this platform auto-confirms a guess into a fact.
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; init; } = ConfirmedAt;

    /// <summary>
    ///     RM-3. "Lomo saltado": the confirmed food, or the proposed one while it is "Por confirmar". Null when there
    ///     is no food or the catalog cannot resolve it.
    /// </summary>
    public string? FoodName { get; init; } = FoodName;

    /// <summary>RM-3 (IN-1). InPlan, OffPlan or NotAnswered. Descriptive only, never a grade.</summary>
    public string PlanAdherence { get; init; } = PlanAdherence;

    /// <summary>RM-3 (IN-1). "Confirmada" (true) or "Por confirmar" (false): only a confirmed entry is intake.</summary>
    public bool IsCountedTowardsTargets { get; init; } = IsCountedTowardsTargets;
}

/// <summary>RM-3. One day of PAC-2 "Esta semana · L M M J V S D".</summary>
/// <param name="Date">The calendar day.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
public record PanelWeekDayResource(DateOnly Date, string Outcome)
{
    /// <summary>The calendar day.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>
    ///     Met ("Dentro de metas"), Short ("Por debajo"), Exceeded, or Unlogged: its own grey state ("Sin
    ///     registro"), never "Por debajo". Days still ahead are Unlogged.
    /// </summary>
    public string Outcome { get; init; } = Outcome;
}

/// <summary>RM-3 (MA-6). PAC-2 "Esta semana · 5 de 7 días", Monday to Sunday.</summary>
/// <param name="From">Monday.</param>
/// <param name="To">Sunday.</param>
/// <param name="Days">The seven days, Monday first.</param>
/// <param name="MetDays">Days within the targets: the "5".</param>
/// <param name="ExceededDays">Days above.</param>
/// <param name="ShortDays">Days below.</param>
/// <param name="UnloggedDays">Days without a record (or still ahead).</param>
/// <param name="LoggedDays">Days with something logged.</param>
/// <param name="TotalDays">Calendar days: the "7" (DECISIÓN §12-#7).</param>
public record PanelWeekResource(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PanelWeekDayResource> Days,
    int MetDays,
    int ExceededDays,
    int ShortDays,
    int UnloggedDays,
    int LoggedDays,
    int TotalDays)
{
    /// <summary>Monday of the week.</summary>
    public DateOnly From { get; init; } = From;

    /// <summary>Sunday of the week.</summary>
    public DateOnly To { get; init; } = To;

    /// <summary>The seven days, Monday first.</summary>
    public IReadOnlyList<PanelWeekDayResource> Days { get; init; } = Days;

    /// <summary>Days within the targets.</summary>
    public int MetDays { get; init; } = MetDays;

    /// <summary>Days above the targets.</summary>
    public int ExceededDays { get; init; } = ExceededDays;

    /// <summary>Days below the targets.</summary>
    public int ShortDays { get; init; } = ShortDays;

    /// <summary>Days without a record, including the ones still ahead. Not a bad day.</summary>
    public int UnloggedDays { get; init; } = UnloggedDays;

    /// <summary>Days with something logged.</summary>
    public int LoggedDays { get; init; } = LoggedDays;

    /// <summary>Calendar days of the week: the denominator.</summary>
    public int TotalDays { get; init; } = TotalDays;
}

/// <summary>RM-3. PAC-2 "Registro 6 de 7 días · Comidas registradas".</summary>
/// <param name="From">First of the seven days.</param>
/// <param name="To">The panel day.</param>
/// <param name="Logged">Days with something logged.</param>
/// <param name="TotalDays">Calendar days counted.</param>
public record PanelLoggedDaysResource(DateOnly From, DateOnly To, int Logged, int TotalDays)
{
    /// <summary>First of the days counted.</summary>
    public DateOnly From { get; init; } = From;

    /// <summary>Last day counted: the panel day.</summary>
    public DateOnly To { get; init; } = To;

    /// <summary>Days with something logged: the "6".</summary>
    public int Logged { get; init; } = Logged;

    /// <summary>Calendar days counted: the "7".</summary>
    public int TotalDays { get; init; } = TotalDays;
}

/// <summary>RM-3 (IN-5). PAC-2 "Tendencia de peso −0,3 kg/sem · Autopesaje · 4 semanas". Never today's weight.</summary>
/// <param name="Weeks">Weeks the trend covers.</param>
/// <param name="SlopeKgPerWeek">Kilograms per week, or null with fewer than two points.</param>
/// <param name="ChangeKg">Change over the weeks, or null.</param>
/// <param name="PointCount">Points of the smoothed series used.</param>
public record PanelWeightTrendSummaryResource(int Weeks, decimal? SlopeKgPerWeek, decimal? ChangeKg, int PointCount)
{
    /// <summary>Weeks the trend covers.</summary>
    public int Weeks { get; init; } = Weeks;

    /// <summary>Kilograms per week of the smoothed home series, or null with fewer than two points.</summary>
    public decimal? SlopeKgPerWeek { get; init; } = SlopeKgPerWeek;

    /// <summary>Change over the weeks, or null.</summary>
    public decimal? ChangeKg { get; init; } = ChangeKg;

    /// <summary>Points of the smoothed series used.</summary>
    public int PointCount { get; init; } = PointCount;
}

/// <summary>RM-3 (NC-3). PAC-2 "Última medición clínica · 3 sept. · en ayunas, sin zapatos · 74.2 kg".</summary>
/// <param name="TakenAt">When it was taken.</param>
/// <param name="WeightKg">Weight in kilograms.</param>
/// <param name="ProtocolChecks">EV-2 checklist codes; empty for measurements before NC-3.</param>
/// <param name="Protocol">The protocol as recorded.</param>
public record PanelClinicalMeasurementResource(
    DateTimeOffset TakenAt,
    decimal WeightKg,
    IReadOnlyList<string> ProtocolChecks,
    string Protocol)
{
    /// <summary>When the practitioner took it.</summary>
    public DateTimeOffset TakenAt { get; init; } = TakenAt;

    /// <summary>Weight in kilograms. Clinical: never merged with the home series.</summary>
    public decimal WeightKg { get; init; } = WeightKg;

    /// <summary>Fasting, NoShoes, LightClothing, EmptyBladder, SameScale (the client translates them).</summary>
    public IReadOnlyList<string> ProtocolChecks { get; init; } = ProtocolChecks;

    /// <summary>The protocol as recorded: free text before NC-3, the readable summary since.</summary>
    public string Protocol { get; init; } = Protocol;
}

/// <summary>
///     What one day of confirmed entries added up to. Read model Patient Monitoring Panel.
/// </summary>
/// <remarks>
///     Totals and counts, with no verdict attached. <c>HasAnyEntry</c> is what separates an unlogged
///     day from a logged day that added up to little, and only one of those is about eating.
/// </remarks>
/// <param name="Date">The local calendar day.</param>
/// <param name="EnergyKcal">Confirmed energy in kilocalories.</param>
/// <param name="ProteinG">Confirmed protein in grams.</param>
/// <param name="CarbG">Confirmed carbohydrate in grams.</param>
/// <param name="FatG">Confirmed fat in grams.</param>
/// <param name="EntryCount">How many entries the diary held that day.</param>
/// <param name="OffPlanEntryCount">How many of them were declared off plan.</param>
/// <param name="HasAnyEntry">Whether the patient wrote anything down that day.</param>
public record PanelDailyIntakeSummaryResource(
    DateOnly Date,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    int EntryCount,
    int OffPlanEntryCount,
    bool HasAnyEntry)
{
    /// <summary>The local calendar day the patient was living.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>Confirmed energy in kilocalories. Unconfirmed proposals are not counted.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Confirmed protein in grams.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>Confirmed carbohydrate in grams.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>Confirmed fat in grams.</summary>
    public decimal FatG { get; init; } = FatG;

    /// <summary>How many entries the diary held that day.</summary>
    public int EntryCount { get; init; } = EntryCount;

    /// <summary>How many of them were declared off plan. A count, not a penalty.</summary>
    public int OffPlanEntryCount { get; init; } = OffPlanEntryCount;

    /// <summary>Whether the patient wrote anything down that day.</summary>
    public bool HasAnyEntry { get; init; } = HasAnyEntry;
}

/// <summary>
///     What the practitioner reads about one patient between visits. Read model Patient Monitoring
///     Panel.
/// </summary>
/// <remarks>
///     A composite read model, not a bounded context. It is the only path from the practitioner to
///     the diary, and it is a read.
///     The two weight series are carried side by side and never merged: the clinical one is what the
///     practitioner measured, the smoothed one is what the scale of the patient said. What is not
///     here is the nutritional diagnosis and the calculation basis, neither of which leaves
///     Nutritional Care, and the deviations, which cross the boundary once as an event and land in a
///     human inbox rather than being queryable on demand.
/// </remarks>
/// <param name="PatientId">Whose panel.</param>
/// <param name="Date">The local calendar day the diary section covers.</param>
/// <param name="ActiveTargets">The published contract in force, or null.</param>
/// <param name="DailyIntakeSummary">What that day added up to, or null when it cannot be read.</param>
/// <param name="Diary">That day of entries, oldest first, with confidence and provenance.</param>
/// <param name="DailyCompliance">Day by day, oldest first.</param>
/// <param name="ClinicalAnthropometrySeries">Weight taken by the practitioner, oldest first.</param>
/// <param name="SelfWeighInTrend">The smoothed home series, kept separate.</param>
/// <param name="Consistency">Deprecated by RM-3: always null.</param>
/// <param name="Week">RM-3. "Esta semana", Monday to Sunday, or null.</param>
/// <param name="LoggedDays">RM-3. "Registro 6 de 7 días", or null.</param>
/// <param name="WeightTrendSummary">RM-3. "Tendencia de peso · 4 semanas", or null.</param>
/// <param name="LastClinicalMeasurement">RM-3. "Última medición clínica", or null.</param>
public record PatientMonitoringPanelResource(
    int PatientId,
    DateOnly Date,
    RecordActiveTargetsResource? ActiveTargets,
    PanelDailyIntakeSummaryResource? DailyIntakeSummary,
    IReadOnlyList<PanelDiaryEntryResource> Diary,
    IReadOnlyList<RecordDailyComplianceResource> DailyCompliance,
    IReadOnlyList<RecordAnthropometryPointResource> ClinicalAnthropometrySeries,
    IReadOnlyList<RecordWeightTrendPointResource> SelfWeighInTrend,
    RecordConsistencyResource? Consistency,
    PanelWeekResource? Week = null,
    PanelLoggedDaysResource? LoggedDays = null,
    PanelWeightTrendSummaryResource? WeightTrendSummary = null,
    PanelClinicalMeasurementResource? LastClinicalMeasurement = null)
{
    /// <summary>Whose panel.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The local calendar day the diary section covers.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>The published contract in force. Targets, guidelines and restrictions only.</summary>
    public RecordActiveTargetsResource? ActiveTargets { get; init; } = ActiveTargets;

    /// <summary>What that day of confirmed entries added up to, or null.</summary>
    public PanelDailyIntakeSummaryResource? DailyIntakeSummary { get; init; } = DailyIntakeSummary;

    /// <summary>That day of entries, oldest first, always with confidence and provenance.</summary>
    public IReadOnlyList<PanelDiaryEntryResource> Diary { get; init; } = Diary;

    /// <summary>Day by day, oldest first, unlogged days included as such.</summary>
    public IReadOnlyList<RecordDailyComplianceResource> DailyCompliance { get; init; } = DailyCompliance;

    /// <summary>Weight taken by the practitioner. Never merged with the series below.</summary>
    public IReadOnlyList<RecordAnthropometryPointResource> ClinicalAnthropometrySeries { get; init; } =
        ClinicalAnthropometrySeries;

    /// <summary>The smoothed home series. A trend, never a single day as a headline.</summary>
    public IReadOnlyList<RecordWeightTrendPointResource> SelfWeighInTrend { get; init; } = SelfWeighInTrend;

    /// <summary>
    ///     Deprecated by RM-3: always null, kept so existing clients do not break. Patient First Always: the
    ///     practitioner learns about a consistency alert only as a ConsistencyEscalation item in the review inbox,
    ///     after the patient saw it.
    /// </summary>
    [Obsolete("Removed from the panel by RM-3 (Patient First Always); always null. Use the review inbox.")]
    public RecordConsistencyResource? Consistency { get; init; } = Consistency;

    /// <summary>RM-3 (MA-6). PAC-2 "Esta semana · 5 de 7 días · L M M J V S D".</summary>
    public PanelWeekResource? Week { get; init; } = Week;

    /// <summary>RM-3. PAC-2 "Registro 6 de 7 días": the last seven calendar days up to <see cref="Date" />.</summary>
    public PanelLoggedDaysResource? LoggedDays { get; init; } = LoggedDays;

    /// <summary>RM-3 (IN-5). PAC-2 "Tendencia de peso −0,3 kg/sem · Autopesaje · 4 semanas".</summary>
    public PanelWeightTrendSummaryResource? WeightTrendSummary { get; init; } = WeightTrendSummary;

    /// <summary>RM-3 (NC-3). PAC-2 "Última medición clínica".</summary>
    public PanelClinicalMeasurementResource? LastClinicalMeasurement { get; init; } = LastClinicalMeasurement;
}

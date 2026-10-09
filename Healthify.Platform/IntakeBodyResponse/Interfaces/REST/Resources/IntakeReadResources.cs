namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;

/// <summary>Read model My Daily Targets. Available offline on the device.</summary>
/// <remarks>
///     Business rules: Published Contract Only and Diagnosis And Basis Never Cached (Subflow 4.1).
///     There is no diagnosis on this resource, no clinical rationale, no equation, no reference
///     weight, no activity factor and no deficit. A patient does not need to read their diagnosis in
///     order to log what they ate.
/// </remarks>
public record ActiveTargetsResource(
    int PatientId,
    int PlanVersion,
    DateTimeOffset ValidFrom,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    DateTimeOffset RefreshedAt,
    IReadOnlyList<ActiveGuidelineResource>? GuidelineItems = null,
    IReadOnlyList<string>? LegacyRestrictions = null,
    IReadOnlyList<ActivePlanChangeResource>? ChangesFromPrevious = null,
    string? PatientMessage = null)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Version of the published contract now in force.</summary>
    public int PlanVersion { get; init; } = PlanVersion;

    /// <summary>Since when it applies.</summary>
    public DateTimeOffset ValidFrom { get; init; } = ValidFrom;

    /// <summary>Daily energy target in kilocalories.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Daily protein target in grams.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>Daily carbohydrate target in grams.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>Daily fat target in grams.</summary>
    public decimal FatG { get; init; } = FatG;

    /// <summary>
    ///     Qualitative guidance from the practitioner: a catalog code or a custom text each. Use
    ///     <see cref="GuidelineItems" /> to tell them apart (NC-6).
    /// </summary>
    public IReadOnlyList<string> Guidelines { get; init; } = Guidelines;

    /// <summary>Restrictions from the practitioner, as codes since NC-6 (LactoseFree, GlutenFree, Vegan…).</summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>When this copy was last refreshed. The device keeps it and it survives offline.</summary>
    public DateTimeOffset RefreshedAt { get; init; } = RefreshedAt;

    /// <summary>NC-6. The guidelines, each a catalog code (translate it) or a custom text (show it as written).</summary>
    public IReadOnlyList<ActiveGuidelineResource> GuidelineItems { get; init; } = GuidelineItems ?? [];

    /// <summary>
    ///     NC-6. Restrictions written as free text before the closed list, which match no code. Show them as
    ///     written, next to <see cref="Restrictions" />.
    /// </summary>
    public IReadOnlyList<string> LegacyRestrictions { get; init; } = LegacyRestrictions ?? [];

    /// <summary>
    ///     NC-8. "Qué cambió en esta versión": empty for the first version and for contracts published before
    ///     NC-8. Write the sentence from each type on the device.
    /// </summary>
    public IReadOnlyList<ActivePlanChangeResource> ChangesFromPrevious { get; init; } = ChangesFromPrevious ?? [];

    /// <summary>NC-9. The practitioner's message to the patient for this version, or null.</summary>
    public string? PatientMessage { get; init; } = PatientMessage;
}

/// <summary>NC-8. One line of "Qué cambió en esta versión".</summary>
public record ActivePlanChangeResource(
    string Type,
    string? Code,
    string? Custom,
    string? Macro,
    decimal? From,
    decimal? To)
{
    /// <summary>
    ///     GuidelineAdded, GuidelineRemoved, RestrictionAdded, RestrictionRemoved, EnergyChanged, MacroChanged or
    ///     NoTargetChanges.
    /// </summary>
    public string Type { get; init; } = Type;

    /// <summary>Guideline or restriction code (translate it), for the guideline and restriction types.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>Custom guideline text, or a legacy restriction left out. Show it as written.</summary>
    public string? Custom { get; init; } = Custom;

    /// <summary>Protein, Carb or Fat, for MacroChanged.</summary>
    public string? Macro { get; init; } = Macro;

    /// <summary>Previous value (kcal or grams), for EnergyChanged and MacroChanged.</summary>
    public decimal? From { get; init; } = From;

    /// <summary>New value (kcal or grams), for EnergyChanged and MacroChanged.</summary>
    public decimal? To { get; init; } = To;
}

/// <summary>NC-6. One guideline of the plan: a catalog code or a custom text, never both.</summary>
public record ActiveGuidelineResource(string? Code, string? Custom)
{
    /// <summary>Catalog code, to translate on the device; null for a custom guideline.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>Written by the practitioner. Clinical data: never translated.</summary>
    public string? Custom { get; init; } = Custom;
}

/// <summary>
///     One entry of the diary. Read models Daily Diary and Estimate Preview Card.
/// </summary>
/// <remarks>
///     Business rule: Confidence And Provenance Always Exposed (Subflow 4.2). Both are on every
///     entry this platform ever returns, including the ones the practitioner reads through the
///     monitoring panel. A number that came from a photo estimate and a number the patient typed are
///     not the same kind of fact, and whoever reads them is entitled to know which one they have.
///     The proposal and the confirmation both appear, because both exist. Nothing here says whether
///     the entry was a good idea.
/// </remarks>
public record DiaryEntryResource(
    int DiaryEntryId,
    int PatientId,
    DateTimeOffset LocalTimestamp,
    DateOnly LocalDate,
    string Provenance,
    string? PhotoRef,
    int? ProposedReferenceFoodId,
    decimal? ProposedPortionGrams,
    decimal? Confidence,
    DateTimeOffset? ProposedEstimatedAt,
    int? ConfirmedReferenceFoodId,
    decimal? ConfirmedPortionGrams,
    DateTimeOffset? ConfirmedAt,
    string SyncState,
    string PlanAdherence,
    bool IsCountedTowardsTargets,
    string? FoodName,
    Guid? MealGroupId = null,
    string? Origin = null)
{
    /// <summary>Identifier of the entry.</summary>
    public int DiaryEntryId { get; init; } = DiaryEntryId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The moment the patient declared, exactly as they declared it.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>The calendar day the patient was living, not the server day.</summary>
    public DateOnly LocalDate { get; init; } = LocalDate;

    /// <summary>Photo, Manual or OffPlan. Always present.</summary>
    public string Provenance { get; init; } = Provenance;

    /// <summary>Where the photo lives, when there is one.</summary>
    public string? PhotoRef { get; init; } = PhotoRef;

    /// <summary>The catalog entry the on-device estimator proposed, when it ran.</summary>
    public int? ProposedReferenceFoodId { get; init; } = ProposedReferenceFoodId;

    /// <summary>The portion it proposed, in grams.</summary>
    public decimal? ProposedPortionGrams { get; init; } = ProposedPortionGrams;

    /// <summary>How sure the estimator was, from 0 to 1. Always shown when there is an estimate.</summary>
    public decimal? Confidence { get; init; } = Confidence;

    /// <summary>When the estimate was proposed.</summary>
    public DateTimeOffset? ProposedEstimatedAt { get; init; } = ProposedEstimatedAt;

    /// <summary>The catalog entry the patient confirmed. Sits beside the proposal, never over it.</summary>
    public int? ConfirmedReferenceFoodId { get; init; } = ConfirmedReferenceFoodId;

    /// <summary>The portion the patient confirmed, in grams.</summary>
    public decimal? ConfirmedPortionGrams { get; init; } = ConfirmedPortionGrams;

    /// <summary>When the patient confirmed.</summary>
    public DateTimeOffset? ConfirmedAt { get; init; } = ConfirmedAt;

    /// <summary>Pending, Synced or Conflicted.</summary>
    public string SyncState { get; init; } = SyncState;

    /// <summary>
    ///     InPlan, OffPlan, or NotAnswered while the entry waits to be confirmed. OffPlan is shown as
    ///     «Fuera del plan» and still counts in the diary.
    /// </summary>
    public string PlanAdherence { get; init; } = PlanAdherence;

    /// <summary>
    ///     Whether the entry adds to today's targets. False while it is «Por confirmar»: a proposal
    ///     the patient has not spoken about is not intake.
    /// </summary>
    public bool IsCountedTowardsTargets { get; init; } = IsCountedTowardsTargets;

    /// <summary>
    ///     Local name of the food the entry shows: the confirmed one, or the proposed one while it
    ///     waits. Null when there is no food or the catalog cannot resolve it.
    /// </summary>
    public string? FoodName { get; init; } = FoodName;

    /// <summary>
    ///     IN-6. The meal this entry was logged with, shared by its other foods, so PT14 shows them as one meal. Null
    ///     for an entry logged alone.
    /// </summary>
    public Guid? MealGroupId { get; init; } = MealGroupId;

    /// <summary>IN-6. MealIdea when it was logged from an idea (IA-3); otherwise null.</summary>
    public string? Origin { get; init; } = Origin;
}

/// <summary>One self weigh-in. Never the headline of anything.</summary>
public record SelfWeighInResource(
    int SelfWeighInId,
    int PatientId,
    decimal ValueKg,
    DateTimeOffset LocalTimestamp,
    bool FastedState,
    bool? SameTimeOfDay,
    bool? SameScale,
    bool FollowsProtocol)
{
    /// <summary>Identifier of the reading.</summary>
    public int SelfWeighInId { get; init; } = SelfWeighInId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The reading in kilograms.</summary>
    public decimal ValueKg { get; init; } = ValueKg;

    /// <summary>The moment the patient declared.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>Declared as taken fasted.</summary>
    public bool FastedState { get; init; } = FastedState;

    /// <summary>Declared as taken at the usual time of day. Null when not asked (IN-3).</summary>
    public bool? SameTimeOfDay { get; init; } = SameTimeOfDay;

    /// <summary>Declared as taken on the usual scale. Null when not asked (IN-3).</summary>
    public bool? SameScale { get; init; } = SameScale;

    /// <summary>
    ///     Whether the reading meets the protocol in force (IN-3: taken fasted). False means the
    ///     reading is kept in full and does not smooth the trend. It does not mean anything else.
    /// </summary>
    public bool FollowsProtocol { get; init; } = FollowsProtocol;
}

/// <summary>One point of the smoothed series.</summary>
public record WeightTrendPointResource(DateOnly Date, decimal SmoothedValueKg)
{
    /// <summary>The day this point summarises.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>The smoothed value in kilograms.</summary>
    public decimal SmoothedValueKg { get; init; } = SmoothedValueKg;
}

/// <summary>
///     Read model Weight Trend Chart.
/// </summary>
/// <remarks>
///     Business rule: Daily Figure Never Exposed As Headline (Subflow 4.5). The series is what this
///     resource carries. There is no field here for the latest reading and no field for today's weight,
///     because a single morning reading moves several hundred grams with hydration alone and showing it
///     as the headline turns noise into a verdict.
///     IN-5: the summary of the last weeks (slope and change) is computed from the smoothed points of the
///     range, never from a single day's reading.
/// </remarks>
public record WeightTrendResource(
    int PatientId,
    int WindowSize,
    DateTimeOffset LastRecalculatedAt,
    IReadOnlyList<WeightTrendPointResource> Points,
    int ExcludedReadingsCount = 0,
    decimal? ChangeKgOverRange = null,
    decimal? SlopeKgPerWeek = null,
    DateOnly? RangeFrom = null,
    DateOnly? RangeTo = null)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>How many readings are averaged into each point.</summary>
    public int WindowSize { get; init; } = WindowSize;

    /// <summary>When the series was last rebuilt.</summary>
    public DateTimeOffset LastRecalculatedAt { get; init; } = LastRecalculatedAt;

    /// <summary>The smoothed series, oldest first. The whole series, not only the range.</summary>
    public IReadOnlyList<WeightTrendPointResource> Points { get; init; } = Points;

    /// <summary>
    ///     IN-5. Readings between <see cref="RangeFrom" /> and <see cref="RangeTo" /> that did not follow the
    ///     protocol: kept, but not in this line («no están en esta línea, pero sí quedaron guardados»).
    /// </summary>
    public int ExcludedReadingsCount { get; init; } = ExcludedReadingsCount;

    /// <summary>IN-5. Last smoothed point of the range minus the first. Null with fewer than two points.</summary>
    public decimal? ChangeKgOverRange { get; init; } = ChangeKgOverRange;

    /// <summary>
    ///     IN-5. Least squares slope of the smoothed points of the range, in kg per week ("−0,3 kg/sem"). Null
    ///     with fewer than two points.
    /// </summary>
    public decimal? SlopeKgPerWeek { get; init; } = SlopeKgPerWeek;

    /// <summary>IN-5. First day of the range (<c>?weeks=</c>, 4 by default), inclusive.</summary>
    public DateOnly? RangeFrom { get; init; } = RangeFrom;

    /// <summary>IN-5. Last day of the range (today), inclusive.</summary>
    public DateOnly? RangeTo { get; init; } = RangeTo;
}

/// <summary>What happened to one item of a synchronisation batch.</summary>
public record SyncedEntryOutcomeResource(
    Guid ClientEntryId,
    int? DiaryEntryId,
    string Outcome,
    string? Reason)
{
    /// <summary>The identifier the device generated.</summary>
    public Guid ClientEntryId { get; init; } = ClientEntryId;

    /// <summary>The entry it became, when it became one.</summary>
    public int? DiaryEntryId { get; init; } = DiaryEntryId;

    /// <summary>Created, AlreadyPresent, ConflictResolved or Rejected.</summary>
    public string Outcome { get; init; } = Outcome;

    /// <summary>Why it was rejected, when it was.</summary>
    public string? Reason { get; init; } = Reason;
}

/// <summary>IN-4. What happened to one queued self weigh-in.</summary>
public record SyncedSelfWeighInOutcomeResource(
    Guid ClientEntryId,
    int? SelfWeighInId,
    string Outcome,
    string? Reason)
{
    /// <summary>The identifier the device generated.</summary>
    public Guid ClientEntryId { get; init; } = ClientEntryId;

    /// <summary>The reading it became or already was, when there is one.</summary>
    public int? SelfWeighInId { get; init; } = SelfWeighInId;

    /// <summary>Created, AlreadyPresent (a duplicate, resolved in silence) or Rejected.</summary>
    public string Outcome { get; init; } = Outcome;

    /// <summary>Why it was rejected, when it was.</summary>
    public string? Reason { get; init; } = Reason;
}

/// <summary>IN-4. The end of a self weigh-in synchronisation batch.</summary>
public record SelfWeighInSyncOutcomeResource(
    int PatientId,
    int Created,
    int AlreadyPresent,
    int Rejected,
    IReadOnlyList<SyncedSelfWeighInOutcomeResource> Entries)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Readings that did not exist and now do.</summary>
    public int Created { get; init; } = Created;

    /// <summary>Readings that were already here. A resend is recognised, never duplicated.</summary>
    public int AlreadyPresent { get; init; } = AlreadyPresent;

    /// <summary>Readings the server could not accept. The rest of the batch was still accepted.</summary>
    public int Rejected { get; init; } = Rejected;

    /// <summary>The outcome of every item, in the order they were sent.</summary>
    public IReadOnlyList<SyncedSelfWeighInOutcomeResource> Entries { get; init; } = Entries;
}

/// <summary>Read model Pending Sync Queue, seen from the end of a batch.</summary>
public record SyncOutcomeResource(
    int PatientId,
    int Created,
    int AlreadyPresent,
    int ConflictsResolved,
    int Rejected,
    IReadOnlyList<SyncedEntryOutcomeResource> Entries)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Entries that did not exist and now do.</summary>
    public int Created { get; init; } = Created;

    /// <summary>Entries that were already here. A resend is recognised, never duplicated.</summary>
    public int AlreadyPresent { get; init; } = AlreadyPresent;

    /// <summary>Entries whose estimate was resolved by last-write-wins.</summary>
    public int ConflictsResolved { get; init; } = ConflictsResolved;

    /// <summary>Entries the server could not accept. The rest of the batch was still accepted.</summary>
    public int Rejected { get; init; } = Rejected;

    /// <summary>The outcome of every item, in the order they were sent.</summary>
    public IReadOnlyList<SyncedEntryOutcomeResource> Entries { get; init; } = Entries;
}

/// <summary>IA-3. One ingredient of a meal idea («Pechuga de pollo · 150 g»).</summary>
public record MealIdeaIngredientResource(
    string Name,
    decimal Grams,
    int? ReferenceFoodId,
    string? CatalogName,
    bool Resolved = true)
{
    /// <summary>The name the idea gives it.</summary>
    public string Name { get; init; } = Name;

    /// <summary>The quantity, in grams.</summary>
    public decimal Grams { get; init; } = Grams;

    /// <summary>
    ///     The catalog entry it resolved to (FC-2), to log the idea with <c>/diary-entries/manual-logs/batch</c>; null
    ///     when the catalog does not have it (<see cref="Resolved" /> false).
    /// </summary>
    public int? ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The local name of that catalog entry, or null.</summary>
    public string? CatalogName { get; init; } = CatalogName;

    /// <summary>
    ///     False for a minor ingredient (a condiment, less than 15 % of the energy of the idea) the catalog does not
    ///     have: it carries no referenceFoodId, and the app leaves it out of the log or sends it to PT9.
    /// </summary>
    public bool Resolved { get; init; } = Resolved;
}

/// <summary>IA-3. One idea (PT14.4 card and PT14.5 detail).</summary>
public record MealIdeaResource(
    string MealIdeaId,
    string Name,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<MealIdeaIngredientResource> Ingredients,
    string Why,
    bool NutrientsFromCatalog)
{
    /// <summary>Sent back as <c>origin.mealIdeaId</c> when the idea is logged, and in <c>excludeIdeaIds</c>.</summary>
    public string MealIdeaId { get; init; } = MealIdeaId;

    /// <summary>«Pollo al horno con camote y ensalada».</summary>
    public string Name { get; init; } = Name;

    /// <summary>Energy of the idea, in kcal. Always within what is left today.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Protein, in grams.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>Carbohydrate, in grams.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>Fat, in grams.</summary>
    public decimal FatG { get; init; } = FatG;

    /// <summary>The ingredients in grams.</summary>
    public IReadOnlyList<MealIdeaIngredientResource> Ingredients { get; init; } = Ingredients;

    /// <summary>«Por qué esta idea».</summary>
    public string Why { get; init; } = Why;

    /// <summary>True when the figures are the food catalog's rather than the AI's (they differed by more than 15 %).</summary>
    public bool NutrientsFromCatalog { get; init; } = NutrientsFromCatalog;
}

/// <summary>
///     IA-3. «Caben en lo que te queda hoy (590 kcal) y no llevan mariscos.» What is left is computed by the
///     server, never by the AI; the ideas never carry a diagnosis.
/// </summary>
public record MealIdeasResource(
    DateOnly LocalDate,
    decimal RemainingKcal,
    decimal RemainingProteinG,
    decimal RemainingCarbG,
    decimal RemainingFatG,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<MealIdeaResource> Ideas,
    long AiGenerationId,
    DateTimeOffset GeneratedAt,
    string Disclaimer)
{
    /// <summary>The patient's day.</summary>
    public DateOnly LocalDate { get; init; } = LocalDate;

    /// <summary>Energy left today: the published targets minus the confirmed entries of the day.</summary>
    public decimal RemainingKcal { get; init; } = RemainingKcal;

    /// <summary>Protein left today, in grams (zero when covered).</summary>
    public decimal RemainingProteinG { get; init; } = RemainingProteinG;

    /// <summary>Carbohydrate left today, in grams (zero when covered).</summary>
    public decimal RemainingCarbG { get; init; } = RemainingCarbG;

    /// <summary>Fat left today, in grams (zero when covered).</summary>
    public decimal RemainingFatG { get; init; } = RemainingFatG;

    /// <summary>The restriction codes of the plan every idea respects (NC-6).</summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>Two or three ideas.</summary>
    public IReadOnlyList<MealIdeaResource> Ideas { get; init; } = Ideas;

    /// <summary>The AI generation they come from.</summary>
    public long AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>When they were generated (cached for two hours).</summary>
    public DateTimeOffset GeneratedAt { get; init; } = GeneratedAt;

    /// <summary>«Las ideas no reemplazan las indicaciones».</summary>
    public string Disclaimer { get; init; } = Disclaimer;
}

/// <summary>
///     IN-7. What the AI recognized in the photo (PT7 «¿Es esto lo que comiste?»). A proposal: nothing is in the diary
///     until the patient logs it with <c>POST /diary-entries/photo-logs</c> and this <c>analysisId</c>.
/// </summary>
/// <remarks>
///     It deliberately says nothing about where the food came from (the catalog, a provider, or created from the
///     estimate): for the patient it is a food like any other.
/// </remarks>
public record MealPhotoAnalysisResource(
    Guid AnalysisId,
    int ReferenceFoodId,
    string FoodName,
    decimal EstimatedGrams,
    decimal Confidence,
    IReadOnlyList<MealPhotoAlternativeResource> Alternatives,
    DateTimeOffset ExpiresAt)
{
    /// <summary>What <c>photo-logs</c> references. Valid until <see cref="ExpiresAt" />.</summary>
    public Guid AnalysisId { get; init; } = AnalysisId;

    /// <summary>The catalog entry of the recognized dish.</summary>
    public int ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>Its name in the catalog.</summary>
    public string FoodName { get; init; } = FoodName;

    /// <summary>The estimated portion, in grams («Lo que propusimos: 320 g»).</summary>
    public decimal EstimatedGrams { get; init; } = EstimatedGrams;

    /// <summary>How sure the AI was, from 0 to 1. Always present.</summary>
    public decimal Confidence { get; init; } = Confidence;

    /// <summary>Up to three other dishes the photo could show.</summary>
    public IReadOnlyList<MealPhotoAlternativeResource> Alternatives { get; init; } = Alternatives;

    /// <summary>After this instant the analysis can no longer be logged (24 hours by default).</summary>
    public DateTimeOffset ExpiresAt { get; init; } = ExpiresAt;
}

/// <summary>IN-7. Another dish the photo could show.</summary>
public record MealPhotoAlternativeResource(string Name, decimal Grams, int? ReferenceFoodId)
{
    /// <summary>Its name: the catalog's when the catalog carries it.</summary>
    public string Name { get; init; } = Name;

    /// <summary>Its estimated portion, in grams.</summary>
    public decimal Grams { get; init; } = Grams;

    /// <summary>The catalog entry, or null when the local catalog does not carry it (the app lets the patient search).</summary>
    public int? ReferenceFoodId { get; init; } = ReferenceFoodId;
}

/// <summary>IN-6. The entries a meal logged in a group became.</summary>
public record MealGroupLogResource(Guid MealGroupId, string? Origin, IReadOnlyList<DiaryEntryResource> Entries)
{
    /// <summary>The group the entries share.</summary>
    public Guid MealGroupId { get; init; } = MealGroupId;

    /// <summary>MealIdea, or null.</summary>
    public string? Origin { get; init; } = Origin;

    /// <summary>One confirmed Manual entry per food, in the order of the items.</summary>
    public IReadOnlyList<DiaryEntryResource> Entries { get; init; } = Entries;
}

namespace Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;

/// <summary>
///     What one patient declared on one day, added up. Primitives only.
/// </summary>
/// <remarks>
///     Notice what this DTO does not carry: no compliance, no deviation, no verdict of any kind. It
///     reports totals and counts. Comparing them against a target is Monitoring's work, and keeping
///     that comparison out of this contract is what stops it leaking back into the context that is
///     supposed to record without judging.
///     <paramref name="HasAnyEntry" /> exists so that a consumer can tell an unlogged day from a day
///     that was logged and added up to little. Those are different facts and only one of them is
///     about eating.
///     <paramref name="OffPlanEntryCount" /> counts the entries the patient answered as off the plan
///     (IN-1), legacy one-tap entries included, and <paramref name="OffPlanEnergyKcal" /> is the part
///     of <paramref name="EnergyKcal" /> they confirmed (MA-1). Both are descriptive, not a verdict.
/// </remarks>
public record DailyIntakeSummaryItem(
    int PatientId,
    DateOnly Date,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    int EntryCount,
    int OffPlanEntryCount,
    bool HasAnyEntry,
    decimal OffPlanEnergyKcal = 0m);

/// <summary>One point of the smoothed weight series. Never a single day's raw reading.</summary>
public record WeightTrendPointItem(DateOnly Date, decimal SmoothedValueKg);

/// <summary>
///     RM-2/IN-5. Where the smoothed home series went over the last weeks ("−0,3 kg/sem · 4 semanas").
///     Primitives only; never a single day's reading. Slope and change are null with fewer than two points.
/// </summary>
public record WeightTrendSummaryItem(decimal? SlopeKgPerWeek, decimal? ChangeKg, int PointCount);

/// <summary>
///     One entry of the diary, as the practitioner reads it. Primitives only.
/// </summary>
/// <remarks>
///     Business rule: Confidence And Provenance Always Exposed (Subflow 4.2). The confidence and the
///     provenance are not optional decoration on this DTO and there is no shape of it that omits
///     them: a portion a model guessed from a photo and a portion the patient typed are different
///     kinds of evidence, and a reader who cannot tell them apart will read a guess as a measurement.
///     Business rule: Proposal Kept Alongside Confirmation (Subflow 4.2). The proposed and the
///     confirmed halves travel side by side rather than collapsed into one number, because what a
///     model said and what a person said are separate facts and the difference between them is the
///     interesting part.
///     There is no compliance flag here, no deviation and no grade. This context records; comparing
///     is Monitoring's work.
/// </remarks>
/// <param name="DiaryEntryId">Identifier of the entry.</param>
/// <param name="LocalTimestamp">The moment the patient declared, exactly as their device gave it.</param>
/// <param name="Provenance">Photo or Manual; OffPlan only on legacy entries (IN-1).</param>
/// <param name="SyncState">Pending, Synced or Conflicted.</param>
/// <param name="ProposedReferenceFoodId">What the estimator proposed, or null while it has not run.</param>
/// <param name="ProposedFoodName">Local name of the proposed food, or null.</param>
/// <param name="ProposedPortionGrams">The proposed portion in grams, or null.</param>
/// <param name="ProposedConfidence">How sure the estimator was, between 0 and 1, or null.</param>
/// <param name="ConfirmedReferenceFoodId">What the patient confirmed, or null while they have not.</param>
/// <param name="ConfirmedFoodName">Local name of the confirmed food, or null.</param>
/// <param name="ConfirmedPortionGrams">The confirmed portion in grams, or null.</param>
/// <param name="ConfirmedAt">When the patient spoke, or null.</param>
/// <param name="PlanAdherence">
///     IN-1. InPlan, OffPlan, or NotAnswered while the entry waits to be confirmed. Descriptive only:
///     it is there to help the practitioner understand the week, never to grade it.
/// </param>
/// <param name="IsCountedTowardsTargets">RM-3. Whether it adds to the day: only a confirmed entry is intake.</param>
/// <param name="FoodName">RM-3. The food the entry shows: the confirmed one, or the proposed one while it waits.</param>
public record DiaryEntryItem(
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
    string PlanAdherence = "NotAnswered",
    bool IsCountedTowardsTargets = false,
    string? FoodName = null);

/// <summary>
///     IA-2/IA-4/IA-5. When an entry was declared and whether it counts, nothing more: no food, no portion, no
///     photo. Enough for the AI facts of Monitoring to tell breakfast from dinner. Primitives only.
/// </summary>
/// <param name="Date">The local calendar day the patient was living.</param>
/// <param name="LocalTimestamp">The moment declared, on the device's clock.</param>
/// <param name="IsCountedTowardsTargets">Only a confirmed entry is intake.</param>
/// <param name="PlanAdherence">InPlan, OffPlan or NotAnswered. Descriptive only.</param>
public record DiaryEntryMomentItem(
    DateOnly Date,
    DateTimeOffset LocalTimestamp,
    bool IsCountedTowardsTargets,
    string PlanAdherence);

/// <summary>
///     Public ACL contract of the Intake and Body Response bounded context.
/// </summary>
/// <remarks>
///     Read only, without exception. This context is written by the patient and by nobody else, so
///     there is no method here that another context could use to write into it, and the absence is
///     the enforcement: a caller cannot misuse an operation that does not exist.
///     Every method degrades gracefully.
/// </remarks>
public interface IIntakeContextFacade
{
    /// <summary>
    ///     What the patient declared on that day, or null when the lookup fails. A day with no
    ///     entries returns a summary with <c>HasAnyEntry</c> false, not null: an unlogged day is a
    ///     fact, not a missing answer.
    /// </summary>
    Task<DailyIntakeSummaryItem?> GetDailyIntakeSummary(int patientId, DateOnly date,
        CancellationToken ct = default);

    /// <summary>The tail of the smoothed series. An empty list when there is none or the lookup fails.</summary>
    Task<IReadOnlyList<WeightTrendPointItem>> GetWeightTrendPoints(int patientId, int days,
        CancellationToken ct = default);

    /// <summary>
    ///     RM-2/IN-5. Slope and change of the smoothed series over the last <paramref name="weeks" /> weeks
    ///     (clamped to 1..52), or null when there is no trend or the lookup fails.
    /// </summary>
    /// <remarks>
    ///     IN-5: the same range and figures as <c>GET /patients/{id}/weight-trend?weeks=</c>, for the composers
    ///     (RM-2, RM-3, RM-4) and, later, the AI facts (IA-2, IA-5).
    /// </remarks>
    Task<WeightTrendSummaryItem?> GetWeightTrendSummary(int patientId, int weeks, CancellationToken ct = default);

    /// <summary>
    ///     IA-2. Slope and change of the smoothed series over an explicit range of local days (<paramref name="from" />
    ///     to <paramref name="to" />, both included), whatever day it is asked; null when there is no trend, the range
    ///     is reversed or the lookup fails.
    /// </summary>
    /// <remarks>
    ///     The weekly summary of a week must say the same about that week if its job runs on Monday or three days late:
    ///     a range counted back from today would not.
    /// </remarks>
    Task<WeightTrendSummaryItem?> GetWeightTrendSummaryBetween(int patientId, DateOnly from, DateOnly to,
        CancellationToken ct = default);

    /// <summary>
    ///     The entries of one day, oldest first. An empty list when there are none or the lookup
    ///     fails.
    /// </summary>
    /// <remarks>
    ///     This is the method the Patient Monitoring Panel reads the diary through, and it is what
    ///     keeps that panel out of this context's repositories. It is still read only, it still
    ///     returns primitives, and it is deliberately the whole entry rather than a tidied one: the
    ///     panel is required to show confidence and provenance, so the contract that feeds it cannot
    ///     be able to hide them.
    ///     The practitioner reads this. There is no counterpart that writes, here or anywhere else.
    /// </remarks>
    /// <param name="patientId">Whose diary.</param>
    /// <param name="date">The local calendar day the patient was living.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<DiaryEntryItem>> GetDiaryEntries(int patientId, DateOnly date,
        CancellationToken ct = default);

    /// <summary>
    ///     IA-2/IA-4/IA-5. The moments of the entries of a range of local days, oldest first, from one read instead
    ///     of one per day. An empty list when there are none or the lookup fails.
    /// </summary>
    Task<IReadOnlyList<DiaryEntryMomentItem>> GetDiaryEntryMoments(int patientId, DateOnly from, DateOnly to,
        CancellationToken ct = default);
}

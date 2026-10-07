using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;

/// <summary>
///     IA-2 - Generate Weekly Summary.
/// </summary>
/// <remarks>
///     The order is the specification:
///     1. the week (a Monday, already over);
///     2. the AI gates before the diary is read (kill switch and flag, then consent and the patient's preference:
///     "si las desactivas, dejamos de usar tu diario") and the active care link;
///     3. the summary of that week, if it exists, is returned as it is (idempotent);
///     4. the facts (MA-6, IN-5), and Summary Needs Three Logged Days;
///     5. the generation through the shared pipeline, validated by <see cref="WeeklySummaryOutputValidator" />;
///     6. the aggregate, persisted. Nothing subscribes to it, so nothing is published.
///     The input never carries the diagnosis nor the calculation basis: this context does not have them.
/// </remarks>
public class WeeklySummaryCommandService(
    IWeeklySummaryRepository weeklySummaryRepository,
    IUnitOfWork unitOfWork,
    MonitoringFactsReader factsReader,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IIamContextFacade iamContextFacade,
    IAiGenerationPipeline pipeline,
    IAiSettings aiSettings,
    IAiConsentPolicy consentPolicy,
    IAiLanguageLexicon lexicon,
    TimeProvider timeProvider,
    ILogger<WeeklySummaryCommandService> logger) : IWeeklySummaryCommandService
{
    private static readonly AiFeature Feature = AiFeature.WeeklySummary;

    public async Task<Result<WeeklySummary, MonitoringAiFailure>> Handle(GenerateWeeklySummaryCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. The week: Monday to Sunday, and already over on the clinical calendar.
        if (!WeeklySummary.IsWeekStart(command.WeekStart)) return Failure(MonitoringError.InvalidWeekStart);
        var weekEnd = command.WeekStart.AddDays(6);
        if (weekEnd >= factsReader.Today()) return Failure(MonitoringError.InvalidWeekStart);

        try
        {
            // 2. Gates before anything of the diary is read.
            if (!aiSettings.IsEnabled(Feature)) return Failure(AiError.AiFeatureDisabled);
            if (!await consentPolicy.IsAllowedAsync(command.PatientId, Feature, cancellationToken))
                return Failure(AiError.AiConsentRequired);
            if (await careRelationshipContextFacade.GetActiveCareLinkByPatientId(command.PatientId,
                    cancellationToken) is null)
                return Failure(MonitoringError.ActiveCareLinkRequired);

            // 3. Business rule: One Summary Per Patient And Week (IA-2).
            var existing = await weeklySummaryRepository.FindByPatientIdAndWeekStartAsync(command.PatientId,
                command.WeekStart, cancellationToken);
            if (existing is not null) return new Result<WeeklySummary, MonitoringAiFailure>.Success(existing);

            // 4. Business rule: The AI Does Not Count (IA-2). The figures come from MA-6 and IN-5.
            var facts = await factsReader.ReadAsync(command.PatientId, command.WeekStart, weekEnd, true,
                cancellationToken);
            // Business rule: Summary Needs Three Logged Days (IA-2).
            if (facts.LoggedDays < WeeklySummary.MinimumLoggedDays) return Failure(MonitoringError.NotEnoughData);

            var language = MonitoringFactsReader.LanguageOf(
                await iamContextFacade.GetPreferredLanguage(command.PatientId, cancellationToken));

            // 5. The generation. Requested by the job, so there is no requester.
            var generation = await pipeline.GenerateAsync(
                new AiGenerationRequest(Feature, command.PatientId, null, language, InputOf(facts)),
                new WeeklySummaryOutputValidator(facts, lexicon), cancellationToken);
            if (generation is Result<AiGenerationOutcome<WeeklySummaryOutput>, AiError>.Failure aiFailure)
                return Failure(aiFailure.Error);
            var outcome = ((Result<AiGenerationOutcome<WeeklySummaryOutput>, AiError>.Success)generation).Value;

            // 6. The aggregate reasserts the rules of a summary; the validator already checked the same limits.
            WeeklySummary summary;
            try
            {
                summary = new WeeklySummary(command.PatientId, facts, outcome.Output.Headline,
                    outcome.Output.WentWell, outcome.Output.WatchOut, outcome.GenerationId, language,
                    timeProvider.GetUtcNow());
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning("Weekly summary of generation {GenerationId} refused by the aggregate: {Message}",
                    outcome.GenerationId, ex.Message);
                return Failure(AiError.AiOutputRejected);
            }

            await weeklySummaryRepository.AddAsync(summary, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            return new Result<WeeklySummary, MonitoringAiFailure>.Success(summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error generating the weekly summary of patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    public async Task<Result<int, MonitoringError>> Handle(PurgeExpiredWeeklySummariesCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var expired = (await weeklySummaryRepository.ListGeneratedBeforeAsync(command.GeneratedBefore,
                Math.Max(1, command.BatchSize), cancellationToken)).ToList();
            if (expired.Count == 0) return new Result<int, MonitoringError>.Success(0);

            foreach (var summary in expired) weeklySummaryRepository.Remove(summary);
            await unitOfWork.CompleteAsync(cancellationToken);
            return new Result<int, MonitoringError>.Success(expired.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error purging expired weekly summaries");
            return new Result<int, MonitoringError>.Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>
    ///     What leaves for the model: per day the outcome, energy against the target, the slots and the off-plan
    ///     entries; the counts of the week and the home trend. No identifier, no name, no food, no diagnosis.
    /// </summary>
    internal static object InputOf(MonitoringPeriodFacts facts)
    {
        return new
        {
            week = new { from = facts.From, to = facts.To, totalDays = facts.TotalDays },
            facts = new
            {
                metDays = facts.MetDays,
                exceededDays = facts.ExceededDays,
                shortDays = facts.ShortDays,
                loggedDays = facts.LoggedDays,
                unloggedDays = facts.UnloggedDays,
                offPlanEntryCount = facts.OffPlanEntryCount,
                offPlanDays = facts.OffPlanDays,
                mealSlotDays = facts.MealSlotDays,
                weightChangeKg = facts.WeightChangeKg,
                selfWeighInCount = facts.SelfWeighInCount
            },
            days = facts.Days.Select(d => new
            {
                date = d.Date, weekday = d.Weekday, outcome = d.Outcome, energyKcal = d.EnergyKcal,
                targetEnergyKcal = d.TargetEnergyKcal, offPlanEntryCount = d.OffPlanEntryCount, slots = d.Slots
            })
        };
    }

    private static Result<WeeklySummary, MonitoringAiFailure> Failure(MonitoringError error)
    {
        return new Result<WeeklySummary, MonitoringAiFailure>.Failure(MonitoringAiFailure.Of(error));
    }

    private static Result<WeeklySummary, MonitoringAiFailure> Failure(AiError error)
    {
        return new Result<WeeklySummary, MonitoringAiFailure>.Failure(MonitoringAiFailure.Of(error));
    }
}

/// <summary>
///     IA-4 - Suggest Questions (PT25 "Prepara tu consulta").
/// </summary>
/// <remarks>
///     The order is the specification:
///     1. the visit identifier, when given;
///     2. the AI gates before the diary is read (kill switch and flag, consent and the patient's preference);
///     3. the visit (another patient's does not exist) and its check in;
///     4. the cache: one generation per visit (or per week without one), generated again when the check in or the
///     language changes;
///     5. the period (since the last completed consultation, at most 28 days, up to yesterday) and its facts:
///     Summary Needs Three Logged Days applies here too;
///     6. the generation, validated by <see cref="SuggestedQuestionsOutputValidator" />.
///     The input carries the slots, the off-plan and unlogged days, the check in and the guideline codes of the
///     plan. Never the diagnosis.
/// </remarks>
public class SuggestedQuestionsCommandService(
    IScheduledFollowUpRepository scheduledFollowUpRepository,
    IPreVisitCheckInRepository checkInRepository,
    MonitoringFactsReader factsReader,
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IIamContextFacade iamContextFacade,
    IAiGenerationPipeline pipeline,
    IAiSettings aiSettings,
    IAiConsentPolicy consentPolicy,
    IMonitoringAiCache cache,
    IAiLanguageLexicon lexicon,
    IFollowUpCalendar calendar,
    TimeProvider timeProvider,
    ILogger<SuggestedQuestionsCommandService> logger) : ISuggestedQuestionsCommandService
{
    /// <summary>"desde la última consulta completada o 28 días".</summary>
    public const int MaximumPeriodDays = 28;

    /// <summary>DECISIÓN IA-4: a cached generation lives at most a week; the MD fixes the key, not the lifetime.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private static readonly AiFeature Feature = AiFeature.SuggestedQuestions;

    public async Task<Result<SuggestedQuestionsView, MonitoringAiFailure>> Handle(SuggestQuestionsCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. The visit identifier.
        if (command.FollowUpId is <= 0) return Failure(MonitoringError.ScheduledFollowUpNotFound);

        try
        {
            // 2. Gates before anything of the diary is read.
            if (!aiSettings.IsEnabled(Feature)) return Failure(AiError.AiFeatureDisabled);
            if (!await consentPolicy.IsAllowedAsync(command.PatientId, Feature, cancellationToken))
                return Failure(AiError.AiConsentRequired);

            // 3. The visit, and what the patient already told about it.
            PreVisitCheckIn? checkIn = null;
            if (command.FollowUpId is { } followUpId)
            {
                var followUp = await scheduledFollowUpRepository.FindByIdAsync(followUpId, cancellationToken);
                if (followUp is null || followUp.PatientId != command.PatientId)
                    return Failure(MonitoringError.ScheduledFollowUpNotFound);
                checkIn = await checkInRepository.FindByFollowUpIdAsync(followUpId, cancellationToken);
            }

            var language = MonitoringFactsReader.LanguageOf(
                await iamContextFacade.GetPreferredLanguage(command.PatientId, cancellationToken));
            var today = factsReader.Today();

            // 4. The cache. Business rule: One Generation Per Visit (IA-4), renewed when the check in changes.
            var key = command.FollowUpId is { } id
                ? $"follow-up:{id}"
                : $"week:{WeeklySummary.WeekStartOf(today):yyyy-MM-dd}";
            var fingerprint = FingerprintOf(language, checkIn);
            if (cache.TryGet<CachedQuestions>(Feature, command.PatientId, key, out var cached) &&
                cached!.Fingerprint == fingerprint)
                return new Result<SuggestedQuestionsView, MonitoringAiFailure>.Success(cached.View);

            // 5. The period: since the last completed consultation, at most 28 days, up to yesterday.
            var to = today.AddDays(-1);
            var from = to.AddDays(-(MaximumPeriodDays - 1));
            var lastConsultation =
                await nutritionalCareContextFacade.GetLastCompletedConsultationAt(command.PatientId,
                    cancellationToken);
            if (lastConsultation is { } completedAt)
            {
                var completedOn = DateOnly.FromDateTime(calendar.LocalDayOf(completedAt).Start.DateTime);
                if (completedOn > from) from = completedOn;
            }

            if (from > to) return Failure(MonitoringError.NotEnoughData);

            var facts = await factsReader.ReadAsync(command.PatientId, from, to, false, cancellationToken);
            // Business rule: Summary Needs Three Logged Days (IA-4, like IA-2).
            if (facts.LoggedDays < WeeklySummary.MinimumLoggedDays) return Failure(MonitoringError.NotEnoughData);

            var targets =
                await nutritionalCareContextFacade.GetActiveTargetsByPatientId(command.PatientId, cancellationToken);
            // Codes only: a custom guideline is free text written by the practitioner, and nothing more is needed.
            var guidelines = targets?.GuidelineItems?.Select(g => g.Code).OfType<string>().ToList() ?? [];

            // 6. The generation.
            var generation = await pipeline.GenerateAsync(
                new AiGenerationRequest(Feature, command.PatientId, command.PatientId, language,
                    InputOf(facts, checkIn, guidelines)),
                new SuggestedQuestionsOutputValidator(facts, lexicon), cancellationToken);
            if (generation is Result<AiGenerationOutcome<SuggestedQuestionsOutput>, AiError>.Failure aiFailure)
                return Failure(aiFailure.Error);
            var outcome = ((Result<AiGenerationOutcome<SuggestedQuestionsOutput>, AiError>.Success)generation).Value;

            var view = new SuggestedQuestionsView(
                outcome.Output.Questions.Select((q, i) => new SuggestedQuestion($"{outcome.GenerationId}-{i + 1}",
                    q.Trim())).ToList(),
                outcome.GenerationId, from, to, timeProvider.GetUtcNow(), language);
            cache.Set(Feature, command.PatientId, key, new CachedQuestions(fingerprint, view), CacheLifetime);

            return new Result<SuggestedQuestionsView, MonitoringAiFailure>.Success(view);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error suggesting questions for patient {PatientId}", command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>What the cached generation depends on: the language and the answers of the check in.</summary>
    internal static string FingerprintOf(string language, PreVisitCheckIn? checkIn)
    {
        var answers = checkIn is null
            ? "none"
            : string.Join("|", checkIn.Feeling.Value, string.Join(",", checkIn.Difficulties.Select(d => d.Value)),
                string.Join(",", checkIn.Questions.Select(q => $"{q.Origin}:{q.Text}")));
        return AiInputPseudonymizer.Sha256($"{language}|{answers}");
    }

    /// <summary>
    ///     What leaves for the model: the slots and outcome of each day, the off-plan and unlogged days, the feeling
    ///     and difficulties of the check in (codes) and the guideline codes. No energy, no name, no diagnosis.
    /// </summary>
    internal static object InputOf(MonitoringPeriodFacts facts, PreVisitCheckIn? checkIn,
        IReadOnlyList<string> guidelines)
    {
        return new
        {
            period = new { from = facts.From, to = facts.To, totalDays = facts.TotalDays },
            facts = new
            {
                loggedDays = facts.LoggedDays,
                unloggedDays = facts.UnloggedDays,
                offPlanDays = facts.OffPlanDays,
                offPlanEntryCount = facts.OffPlanEntryCount,
                mealSlotDays = facts.MealSlotDays
            },
            days = facts.Days.Select(d => new
            {
                weekday = d.Weekday, outcome = d.Outcome, offPlanEntryCount = d.OffPlanEntryCount, slots = d.Slots
            }),
            checkIn = checkIn is null
                ? null
                : new { feeling = checkIn.Feeling.Value, difficulties = checkIn.Difficulties.Select(d => d.Value) },
            planGuidelines = guidelines
        };
    }

    private static Result<SuggestedQuestionsView, MonitoringAiFailure> Failure(MonitoringError error)
    {
        return new Result<SuggestedQuestionsView, MonitoringAiFailure>.Failure(MonitoringAiFailure.Of(error));
    }

    private static Result<SuggestedQuestionsView, MonitoringAiFailure> Failure(AiError error)
    {
        return new Result<SuggestedQuestionsView, MonitoringAiFailure>.Failure(MonitoringAiFailure.Of(error));
    }

    private sealed record CachedQuestions(string Fingerprint, SuggestedQuestionsView View);
}

/// <summary>
///     IA-5 - Summarize Monitoring (PAC-2 "Resumen generado con IA · Revísalo antes de usarlo en consulta").
/// </summary>
/// <remarks>
///     The order is the specification:
///     1. the range (default: the seven days up to yesterday; at most 31, MA-6);
///     2. the active care link between the practitioner and the patient;
///     3. the AI gates: without the feature or the patient's AI consent, the deterministic facts are the answer
///     (§12-#5) and the diary does not leave the platform;
///     4. the cache, six hours per patient and range;
///     5. the facts (MA-6, IN-5). Business rule: Patient Shown First (MA-7): the consistency index is part of the
///     input only once a ConsistencyEscalation review item exists (asked to Nutritional Care by its facade);
///     6. the generation, validated by <see cref="MonitoringSummaryOutputValidator" />.
/// </remarks>
public class MonitoringSummaryCommandService(
    MonitoringFactsReader factsReader,
    IConsistencyIndexQueryService consistencyIndexQueryService,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IIamContextFacade iamContextFacade,
    IAiGenerationPipeline pipeline,
    IAiSettings aiSettings,
    IAiConsentPolicy consentPolicy,
    IMonitoringAiCache cache,
    IAiLanguageLexicon lexicon,
    TimeProvider timeProvider,
    ILogger<MonitoringSummaryCommandService> logger) : IMonitoringSummaryCommandService
{
    public const int DefaultRangeDays = 7;

    /// <summary>"Caché de 6 h por (patient, rango)".</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private static readonly AiFeature Feature = AiFeature.PractitionerMonitoringSummary;

    public async Task<Result<MonitoringSummaryView, MonitoringAiFailure>> Handle(SummarizeMonitoringCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. The range. Both ends or none.
        if (command.From is null != command.To is null) return Failure(MonitoringError.InvalidComplianceRange);
        var to = command.To ?? factsReader.Today().AddDays(-1);
        var from = command.From ?? to.AddDays(-(DefaultRangeDays - 1));
        if (!ComplianceSummary.IsValidRange(from, to)) return Failure(MonitoringError.InvalidComplianceRange);

        try
        {
            // 2. Only a practitioner with an active care link reads it.
            if (!await careRelationshipContextFacade.IsCareLinkActive(command.PatientId, command.PractitionerId,
                    cancellationToken))
                return Failure(MonitoringError.ActiveCareLinkRequired);

            // 3. Gates. DECISIÓN §12-#5: without them the answer is the deterministic fallback, the facts alone.
            string? unavailable = null;
            if (!aiSettings.IsEnabled(Feature)) unavailable = nameof(AiError.AiFeatureDisabled);
            else if (!await consentPolicy.IsAllowedAsync(command.PatientId, Feature, cancellationToken))
                unavailable = nameof(AiError.AiConsentRequired);

            // Business rule: Patient Shown First (MA-7, IA-5).
            var consistencyAllowed =
                await nutritionalCareContextFacade.HasConsistencyEscalation(command.PatientId, cancellationToken);
            var language = MonitoringFactsReader.LanguageOf(
                await iamContextFacade.GetPreferredLanguage(command.PractitionerId, cancellationToken));

            // 4. The cache, only for generated texts.
            var key = $"{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{language}:{(consistencyAllowed ? "c" : "-")}";
            if (unavailable is null &&
                cache.TryGet<MonitoringSummaryView>(Feature, command.PatientId, key, out var cached))
                return new Result<MonitoringSummaryView, MonitoringAiFailure>.Success(cached!);

            // 5. The facts.
            var facts = await factsReader.ReadAsync(command.PatientId, from, to, true, cancellationToken);
            string? consistencyState = null;
            if (consistencyAllowed)
                consistencyState = (await consistencyIndexQueryService.Handle(
                    new GetConsistencyIndexByPatientIdQuery(command.PatientId), cancellationToken))?.State.Value;

            if (unavailable is not null) return Fallback(facts, consistencyState, unavailable);
            // Nothing logged: there is nothing for a text to say that the facts do not.
            if (facts.LoggedDays == 0) return Fallback(facts, consistencyState, nameof(MonitoringError.NotEnoughData));

            // 6. The generation, counted against the practitioner's quota.
            var generation = await pipeline.GenerateAsync(
                new AiGenerationRequest(Feature, command.PatientId, command.PractitionerId, language,
                    InputOf(facts, consistencyState)),
                new MonitoringSummaryOutputValidator(facts, lexicon, consistencyState is not null), cancellationToken);
            if (generation is Result<AiGenerationOutcome<MonitoringSummaryOutput>, AiError>.Failure aiFailure)
                return aiFailure.Error is AiError.AiFeatureDisabled or AiError.AiConsentRequired
                    ? Fallback(facts, consistencyState, aiFailure.Error.ToString())
                    : Failure(aiFailure.Error);
            var outcome = ((Result<AiGenerationOutcome<MonitoringSummaryOutput>, AiError>.Success)generation).Value;

            var view = new MonitoringSummaryView(outcome.Output.Text.Trim(), facts, consistencyState,
                outcome.GenerationId, timeProvider.GetUtcNow(), null);
            cache.Set(Feature, command.PatientId, key, view, CacheLifetime);
            return new Result<MonitoringSummaryView, MonitoringAiFailure>.Success(view);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error summarizing the monitoring of patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>
    ///     What leaves for the model: the facts of the period, day by day, the home trend and, only after an
    ///     escalation, the state of the consistency index. No identifier, no name, no diagnosis.
    /// </summary>
    internal static object InputOf(MonitoringPeriodFacts facts, string? consistencyState)
    {
        return new
        {
            period = new { from = facts.From, to = facts.To, totalDays = facts.TotalDays },
            facts = new
            {
                metDays = facts.MetDays,
                exceededDays = facts.ExceededDays,
                shortDays = facts.ShortDays,
                loggedDays = facts.LoggedDays,
                unloggedDays = facts.UnloggedDays,
                shortWeekdays = facts.ShortWeekdays,
                dominantMissingSlotOnShortDays = facts.DominantMissingSlotOnShortDays,
                offPlanEntryCount = facts.OffPlanEntryCount,
                offPlanDays = facts.OffPlanDays,
                mealSlotDays = facts.MealSlotDays,
                weightChangeKg = facts.WeightChangeKg,
                weightSlopeKgPerWeek = facts.WeightSlopeKgPerWeek,
                selfWeighInCount = facts.SelfWeighInCount
            },
            days = facts.Days.Select(d => new
            {
                date = d.Date, weekday = d.Weekday, outcome = d.Outcome, energyKcal = d.EnergyKcal,
                targetEnergyKcal = d.TargetEnergyKcal, offPlanEntryCount = d.OffPlanEntryCount, slots = d.Slots
            }),
            consistencyState
        };
    }

    private Result<MonitoringSummaryView, MonitoringAiFailure> Fallback(MonitoringPeriodFacts facts,
        string? consistencyState, string reason)
    {
        return new Result<MonitoringSummaryView, MonitoringAiFailure>.Success(
            new MonitoringSummaryView(null, facts, consistencyState, null, timeProvider.GetUtcNow(), reason));
    }

    private static Result<MonitoringSummaryView, MonitoringAiFailure> Failure(MonitoringError error)
    {
        return new Result<MonitoringSummaryView, MonitoringAiFailure>.Failure(MonitoringAiFailure.Of(error));
    }

    private static Result<MonitoringSummaryView, MonitoringAiFailure> Failure(AiError error)
    {
        return new Result<MonitoringSummaryView, MonitoringAiFailure>.Failure(MonitoringAiFailure.Of(error));
    }
}

/// <summary>
///     §12-#14 - Purge Monitoring AI Content: the weekly summaries (IA-2) and the cached questions (IA-4) and
///     monitoring summaries (IA-5) of the patient. The technical audit (<c>ai_generations</c>) is purged by Care
///     Relationship.
/// </summary>
public class MonitoringAiContentCommandService(
    IWeeklySummaryRepository weeklySummaryRepository,
    IUnitOfWork unitOfWork,
    IMonitoringAiCache cache,
    ILogger<MonitoringAiContentCommandService> logger) : IMonitoringAiContentCommandService
{
    public async Task<Result<int, MonitoringError>> Handle(PurgeMonitoringAiContentCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = 0;
            // The caches first: they cost nothing to lose and must not outlive the decision.
            if (command.SuggestedQuestions) removed += cache.Evict(command.PatientId, AiFeature.SuggestedQuestions);
            if (command.MonitoringSummaries)
                removed += cache.Evict(command.PatientId, AiFeature.PractitionerMonitoringSummary);

            if (command.WeeklySummaries)
            {
                var summaries = (await weeklySummaryRepository.ListByPatientIdAsync(command.PatientId,
                    cancellationToken)).ToList();
                foreach (var summary in summaries) weeklySummaryRepository.Remove(summary);
                if (summaries.Count > 0) await unitOfWork.CompleteAsync(cancellationToken);
                removed += summaries.Count;
            }

            return new Result<int, MonitoringError>.Success(removed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error purging the AI content of patient {PatientId}", command.PatientId);
            return new Result<int, MonitoringError>.Failure(MonitoringError.UnexpectedError);
        }
    }
}

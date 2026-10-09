using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;

/// <summary>
///     IA-3 - Generate Meal Ideas («Ideas que caben en lo que te queda hoy y respetan tu plan»).
/// </summary>
/// <remarks>
///     The order is the specification:
///     1. the patient's day (today on their device);
///     2. the AI gates before the diary is read (kill switch and flag, then consent and the patient's preference:
///     «si las desactivas, dejamos de usar tu diario»);
///     3. what is left today, computed here without AI: the published targets (<see cref="ActiveTargetsCache" />)
///     minus the confirmed entries of the day; Ideas Need Room For A Meal (more than 150 kcal);
///     4. the cache: two hours per day, 50 kcal bucket, restrictions, plan version, language and ideas already seen;
///     5. the generation through the shared pipeline, validated by <see cref="MealIdeasOutputValidator" />;
///     6. the hard validation of each idea with the catalog (FC-2): ingredients resolved (an idea whose unresolved
///     ingredients bring more than 15 % of its energy is discarded; a minor one, a condiment, stays marked
///     unresolved), figures recalculated (the catalog's when they differ by more than 15 %), «cabe en lo que te queda» and the restrictions checked
///     again, the ideas already seen dropped. With fewer than two ideas left, the generation is retried once, then
///     answered with <see cref="AiError.AiOutputRejected" /> (PT14.4.E).
///     The input carries the targets, what is left, the restriction codes and the guideline codes of the plan. Never
///     the diagnosis nor the calculation basis: this context does not have them (Diagnosis And Basis Never Cached).
///     Custom guidelines are clinical free text and do not leave either.
/// </remarks>
public class MealIdeasCommandService(
    IActiveTargetsCacheRepository targetsRepository,
    IDiaryEntryRepository diaryEntryRepository,
    IFoodCatalogContextFacade foodCatalogContextFacade,
    IIamContextFacade iamContextFacade,
    IAiGenerationPipeline pipeline,
    IAiSettings aiSettings,
    IAiConsentPolicy consentPolicy,
    IRestrictionLexicon lexicon,
    IMealIdeasCache cache,
    TimeProvider timeProvider,
    ILogger<MealIdeasCommandService> logger) : IMealIdeasCommandService
{
    /// <summary>IA-3: «Caché … durante 2 h».</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(2);

    /// <summary>IA-3: «se reintenta una vez».</summary>
    public const int MaximumAttempts = 2;

    /// <summary>How many ideas the model is asked for, so that two or three survive the validation.</summary>
    public const int IdeasRequested = 4;

    /// <summary>«Ver otras ideas» never needs more than this many seen ideas to stay different.</summary>
    public const int MaximumExcludedIdeas = 30;

    /// <summary>FC-2: distinct ingredient names resolved per generation.</summary>
    private const int MaximumIngredientNames = 50;

    private static readonly AiFeature Feature = AiFeature.MealIdeas;

    public async Task<Result<MealIdeasView, IntakeAiFailure>> Handle(GenerateMealIdeasCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. The patient's day: today on their device, one day either side of the server's UTC date.
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);
        var serverToday = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (command.LocalDate is not { } localDate || Math.Abs(localDate.DayNumber - serverToday.DayNumber) > 1)
            return Failure(IntakeError.InvalidLocalDate);

        try
        {
            // 2. Gates before anything of the diary is read.
            if (!aiSettings.IsEnabled(Feature)) return Failure(AiError.AiFeatureDisabled);
            if (!await consentPolicy.IsAllowedAsync(command.PatientId, Feature, cancellationToken))
                return Failure(AiError.AiConsentRequired);

            // 3. Business rule: Ideas Need Room For A Meal (IA-3). Computed here, never by the AI.
            var targets = await targetsRepository.FindByPatientIdAsync(command.PatientId, cancellationToken);
            if (targets is null) return Failure(IntakeError.ActiveTargetsCacheNotFound);

            var entries = await diaryEntryRepository.ListByPatientIdAsync(command.PatientId, localDate,
                cancellationToken);
            var eaten = await DailyIntakeCalculator.SumAsync(entries, foodCatalogContextFacade, cancellationToken);
            var remaining = RemainingTargets.Of(targets.EnergyKcal, targets.ProteinG, targets.CarbG, targets.FatG,
                eaten.EnergyKcal, eaten.ProteinG, eaten.CarbG, eaten.FatG);
            if (!remaining.LeavesRoomForAMeal) return Failure(IntakeError.NotEnoughRemaining);

            var restrictions = targets.Restrictions.Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToList();
            var language = LanguageOf(await iamContextFacade.GetPreferredLanguage(command.PatientId,
                cancellationToken));
            var excludedIds = (command.ExcludeIdeaIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim()).Distinct(StringComparer.Ordinal).Take(MaximumExcludedIdeas).ToList();

            // 4. The cache. Served only now that the gates passed again.
            var key = CacheKey(localDate, remaining, restrictions, targets.PlanVersion, language, excludedIds);
            if (cache.TryGet(command.PatientId, key, out var cached) && cached is not null)
                return new Result<MealIdeasView, IntakeAiFailure>.Success(cached);

            var avoid = cache.FindIdeaNames(command.PatientId, excludedIds);
            var input = InputOf(localDate, targets, remaining, restrictions, avoid);

            // 5 and 6. The generation and the hard validation, retried once.
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                var generation = await pipeline.GenerateAsync(
                    new AiGenerationRequest(Feature, command.PatientId, command.PatientId, language, input),
                    new MealIdeasOutputValidator(remaining, restrictions, lexicon), cancellationToken);

                if (generation is Result<AiGenerationOutcome<MealIdeasOutput>, AiError>.Failure aiFailure)
                {
                    if (aiFailure.Error != AiError.AiOutputRejected) return Failure(aiFailure.Error);
                    logger.LogInformation("Meal ideas rejected on attempt {Attempt} of {MaximumAttempts}", attempt,
                        MaximumAttempts);
                    continue;
                }

                var outcome = ((Result<AiGenerationOutcome<MealIdeasOutput>, AiError>.Success)generation).Value;
                var ideas = await ValidateWithCatalogAsync(outcome, remaining, restrictions, avoid,
                    cancellationToken);
                if (ideas.Count < MealIdeaRules.MinimumIdeas)
                {
                    logger.LogInformation(
                        "Meal ideas of generation {GenerationId}: {Count} valid after the catalog on attempt {Attempt}",
                        outcome.GenerationId, ideas.Count, attempt);
                    continue;
                }

                var view = new MealIdeasView(localDate, remaining, restrictions, ideas, outcome.GenerationId,
                    timeProvider.GetUtcNow());
                cache.Set(command.PatientId, key, view, CacheLifetime);
                return new Result<MealIdeasView, IntakeAiFailure>.Success(view);
            }

            return Failure(AiError.AiOutputRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error generating meal ideas for patient {PatientId}", command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    public Task<Result<int, IntakeError>> Handle(PurgeMealIdeasCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult<Result<int, IntakeError>>(
                new Result<int, IntakeError>.Success(cache.Evict(command.PatientId)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error purging the meal ideas of patient {PatientId}", command.PatientId);
            return Task.FromResult<Result<int, IntakeError>>(
                new Result<int, IntakeError>.Failure(IntakeError.UnexpectedError));
        }
    }

    /// <summary>
    ///     IA-3, validation 1 to 3 with the catalog: each ingredient resolved (FC-2), the figures recalculated, and
    ///     the idea screened again with the catalog names and figures. Ideas already seen are dropped.
    /// </summary>
    private async Task<IReadOnlyList<MealIdea>> ValidateWithCatalogAsync(AiGenerationOutcome<MealIdeasOutput> outcome,
        RemainingTargets remaining, IReadOnlyCollection<string> restrictions, IReadOnlyList<string> avoid,
        CancellationToken cancellationToken)
    {
        var ideas = outcome.Output.Ideas ?? [];
        var names = ideas.SelectMany(i => i.Ingredients ?? []).Select(i => i.Name?.Trim() ?? string.Empty)
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resolved = (await foodCatalogContextFacade.ResolveByNames(names, MaximumIngredientNames,
                cancellationToken))
            .Where(r => r.IsResolved)
            .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var seen = avoid.Select(Normalize).ToHashSet(StringComparer.Ordinal);

        var valid = new List<MealIdea>();
        for (var index = 0; index < ideas.Count && valid.Count < MealIdeaRules.MaximumIdeasShown; index++)
        {
            var idea = ideas[index];
            if (string.IsNullOrWhiteSpace(idea.Name) || seen.Contains(Normalize(idea.Name))) continue;

            var catalog = MealNutrients.Zero;
            var ingredients = new List<MealIdeaIngredient>();
            foreach (var ingredient in idea.Ingredients ?? [])
            {
                var name = ingredient.Name?.Trim() ?? string.Empty;
                var food = resolved.GetValueOrDefault(name);
                ingredients.Add(new MealIdeaIngredient(name, decimal.Round(ingredient.Grams, 0),
                    food?.ReferenceFoodId, food?.LocalName));
                if (food is not null)
                    catalog = catalog.Plus(MealNutrients.OfPortion(ingredient.Grams, food.EnergyKcalPer100g!.Value,
                        food.ProteinGPer100g ?? 0m, food.CarbGPer100g ?? 0m, food.FatGPer100g ?? 0m));
            }

            if (ingredients.Count == 0) continue;

            // Business rule: Unresolved Ingredients Stay Minor (IA-3). More than 15 % of the energy the catalog cannot
            // see, and whether the idea fits what is left cannot be verified.
            if (ingredients.Any(i => !i.IsResolved) &&
                !MealIdeaRules.UnresolvedIngredientsAreMinor(idea.EnergyKcal, catalog.EnergyKcal))
            {
                logger.LogInformation(
                    "Meal idea {Index} of generation {GenerationId} discarded: {Share:P0} of its energy is unresolved",
                    index + 1, outcome.GenerationId,
                    MealIdeaRules.UnresolvedEnergyShare(idea.EnergyKcal, catalog.EnergyKcal));
                continue;
            }
            var (figures, fromCatalog) = MealIdeaRules.Reconcile(
                new MealNutrients(idea.EnergyKcal, idea.ProteinG, idea.CarbG, idea.FatG), catalog,
                ingredients.All(i => i.IsResolved));

            var texts = ingredients.Select(i => i.Name)
                .Concat(ingredients.Where(i => i.CatalogName is not null).Select(i => i.CatalogName!));
            var reasons = MealIdeaRules.Screen(idea.Name, figures.EnergyKcal, texts, idea.Why, remaining,
                restrictions, lexicon);
            if (reasons.Count > 0)
            {
                logger.LogInformation("Meal idea {Index} of generation {GenerationId} discarded: {Reasons}",
                    index + 1, outcome.GenerationId, string.Join(" | ", reasons.Take(3)));
                continue;
            }

            valid.Add(new MealIdea($"{outcome.GenerationId}-{index + 1}", idea.Name.Trim(), figures.EnergyKcal,
                figures.ProteinG, figures.CarbG, figures.FatG, ingredients, idea.Why.Trim(), fromCatalog));
        }

        return valid;
    }

    /// <summary>
    ///     What leaves for the model: today's targets and what is left of them, the restriction codes, the guideline
    ///     codes of the plan and the ideas already seen. No identifier, no name, no diary entry, no diagnosis.
    /// </summary>
    internal static object InputOf(DateOnly localDate, ActiveTargetsCache targets, RemainingTargets remaining,
        IReadOnlyList<string> restrictions, IReadOnlyList<string> avoid)
    {
        return new
        {
            localDate,
            ideasWanted = IdeasRequested,
            remaining = new
            {
                energyKcal = remaining.EnergyKcal, proteinG = remaining.ProteinG, carbG = remaining.CarbG,
                fatG = remaining.FatG
            },
            dailyTargets = new
            {
                energyKcal = targets.EnergyKcal, proteinG = targets.ProteinG, carbG = targets.CarbG,
                fatG = targets.FatG
            },
            restrictions,
            planGuidelines = targets.GuidelineItems.Where(g => g.Code is not null).Select(g => g.Code).ToList(),
            avoidIdeas = avoid
        };
    }

    private static string CacheKey(DateOnly localDate, RemainingTargets remaining, IReadOnlyList<string> restrictions,
        int planVersion, string language, IReadOnlyList<string> excludedIds)
    {
        return string.Join('|', localDate.ToString("yyyy-MM-dd"), remaining.EnergyBucket,
            string.Join(',', restrictions), planVersion, language,
            string.Join(',', excludedIds.Order(StringComparer.Ordinal)));
    }

    private static string LanguageOf(string? preferred)
    {
        return string.Equals(preferred?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
    }

    private static string Normalize(string text)
    {
        return string.Join(' ', text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Result<MealIdeasView, IntakeAiFailure> Failure(IntakeError error)
    {
        return new Result<MealIdeasView, IntakeAiFailure>.Failure(IntakeAiFailure.Of(error));
    }

    private static Result<MealIdeasView, IntakeAiFailure> Failure(AiError error)
    {
        return new Result<MealIdeasView, IntakeAiFailure>.Failure(IntakeAiFailure.Of(error));
    }
}

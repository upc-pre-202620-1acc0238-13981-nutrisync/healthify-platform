using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;

/// <summary>
///     Analyze Meal Photo: the AI recognizes the dish on the server, the catalog resolves it, and the analysis
///     waits 24 hours for the patient to confirm it.
/// </summary>
/// <remarks>
///     The order is the specification:
///     1. the photo: present, at most <c>Intake:MealPhoto:MaxBytes</c> (2 MB), JPEG or WebP by its signature;
///     2. Photo Leaves Without Patient Data: its metadata (EXIF, XMP, ICC…) removed in memory;
///     3. the AI gates (kill switch and flag, consent and preference) before anything else is read, then the
///     catalog names the model should prefer (Similar Dish Reused, Not Duplicated: see CatalogNameHintsAsync);
///     4. the generation through the shared pipeline, which owns the gates in their order: kill switch and flag
///     (503), consent and the <c>MealPhotoRecognition</c> preference (403, before anything reaches the AI), the daily
///     quota (429), the provider (503), the schema and <see cref="MealPhotoRecognitionOutputValidator" />. A rejected
///     output is <see cref="IntakeError.PhotoNotRecognized" /> (422, the app offers to log it by hand);
///     5. the dish resolved, in this order: the local catalog, the external providers (Search Food, idempotent
///     by source hash), and last a food created from the AI's nutrients, which the catalog refuses when they are
///     incoherent (then <see cref="IntakeError.PhotoNotRecognized" />). The alternatives follow the same path without
///     the providers: the local catalog, then a food created from their own nutrients (or the similar entry); one
///     the catalog refuses stays without a food and never fails the analysis;
///     6. the analysis stored without any image, with its lifetime;
///     7. the suggestion for "was it in your plan?" of the dish and of each alternative
///     (<see cref="IPlanFitQueryService" />), from the plan's restrictions and what is left today. Never sent to the
///     AI, and it never fails the analysis.
///     It creates no diary entry: logging is <c>photo-logs</c> with the <c>analysisId</c>.
///     Business rule: Photo Never Stored. The bytes live in this method and in the request to the provider;
///     they are never written, logged nor kept. The audit keeps their SHA-256.
/// </remarks>
public class MealPhotoAnalysisCommandService(
    IMealPhotoAnalysisRepository analysisRepository,
    IDiaryEntryRepository diaryEntryRepository,
    IUnitOfWork unitOfWork,
    IFoodCatalogContextFacade foodCatalogContextFacade,
    IPhotoMetadataStripper metadataStripper,
    IAiGenerationPipeline pipeline,
    IAiSettings aiSettings,
    IAiConsentPolicy consentPolicy,
    ICatalogNameHintsCache catalogNameHintsCache,
    IPlanFitQueryService planFitQueryService,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<MealPhotoAnalysisCommandService> logger) : IMealPhotoAnalysisCommandService
{
    /// <summary>Two MB at most, configurable (<c>Intake:MealPhoto:MaxBytes</c>).</summary>
    public const int DefaultMaxBytes = 2 * 1024 * 1024;

    /// <summary>Kept only for a while, 24 hours by default (<c>Intake:MealPhoto:AnalysisLifetimeHours</c>).</summary>
    public const int DefaultAnalysisLifetimeHours = 24;

    /// <summary>
    ///     Dish names are catalog vocabulary, in Spanish whatever the reader's language (as the
    ///     ingredients of meal ideas), so the prompt is always rendered in Spanish. The output has no text for a reader.
    /// </summary>
    public const string CatalogLanguage = "es";

    /// <summary>How many catalog names go in the prompt (<c>FoodCatalog:PromptCatalogNamesCount</c>).</summary>
    public const int DefaultCatalogNameHints = 100;

    /// <summary>How long the list is kept in memory (<c>FoodCatalog:PromptCatalogNamesCacheMinutes</c>).</summary>
    public const int DefaultCatalogNameHintsCacheMinutes = 60;

    /// <summary>
    ///     A food becomes a hint only when at least this many different patients logged it, so the list is
    ///     a statistic of the catalog and never reveals what one patient eats.
    /// </summary>
    public const int MinimumPatientsPerHint = 3;

    private static readonly AiFeature Feature = AiFeature.MealPhotoRecognition;

    public async Task<Result<MealPhotoAnalysisView, IntakeAiFailure>> Handle(AnalyzeMealPhotoCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        // 1. The photo, each check to its own error.
        if (command.Photo is not { Length: > 0 } bytes) return Failure(IntakeError.PhotoRequired);
        if (bytes.Length > MaxBytes) return Failure(IntakeError.PhotoTooLarge);

        MealPhoto photo;
        try
        {
            photo = new MealPhoto(bytes);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.UnsupportedPhotoFormat);
        }

        try
        {
            // 2. Business rule: Photo Leaves Without Patient Data.
            MealPhoto clean;
            try
            {
                clean = metadataStripper.Strip(photo);
            }
            catch (ArgumentException)
            {
                return Failure(IntakeError.UnsupportedPhotoFormat);
            }

            // 3. The gates before anything is read (the pipeline asks them again, in its own order).
            if (!aiSettings.IsEnabled(Feature)) return Failure(AiError.AiFeatureDisabled);
            if (!await consentPolicy.IsAllowedAsync(command.PatientId, Feature, cancellationToken))
                return Failure(AiError.AiConsentRequired);
            var hints = await CatalogNameHintsAsync(cancellationToken);

            // 4. The generation. The image and catalog names are its only input; nothing about the patient goes with it.
            var generation = await pipeline.GenerateAsync(
                new AiGenerationRequest(Feature, command.PatientId, command.PatientId, CatalogLanguage, InputOf(hints),
                    Images: [new AiImagePart(clean.Bytes, clean.MimeType)]),
                new MealPhotoRecognitionOutputValidator(), cancellationToken);

            if (generation is Result<AiGenerationOutcome<MealPhotoRecognitionOutput>, AiError>.Failure aiFailure)
                return aiFailure.Error == AiError.AiOutputRejected
                    ? Failure(IntakeError.PhotoNotRecognized)
                    : Failure(aiFailure.Error);

            var outcome = ((Result<AiGenerationOutcome<MealPhotoRecognitionOutput>, AiError>.Success)generation).Value;
            var output = outcome.Output;

            // 5. The dish: local catalog, then the providers, then created from the estimate (or the similar entry).
            var dish = await ResolveDishAsync(output, outcome.GenerationId, cancellationToken);
            if (dish is null)
            {
                logger.LogInformation("Meal photo of generation {GenerationId}: the dish could not be resolved nor " +
                                      "created", outcome.GenerationId);
                return Failure(IntakeError.PhotoNotRecognized);
            }

            var alternatives = await ResolveAlternativesAsync(output, dish.Value.ReferenceFoodId,
                outcome.GenerationId, cancellationToken);

            // 6. The analysis, without the image.
            var analysis = new MealPhotoAnalysis(command.PatientId, dish.Value.ReferenceFoodId,
                output.EstimatedGrams, new Confidence(output.Confidence), alternatives, outcome.GenerationId,
                timeProvider.GetUtcNow().Add(AnalysisLifetime));

            await analysisRepository.AddAsync(analysis, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. The plan suggestion, computed here from the plan and the diary; the AI never sees either.
            var fits = await PlanFitsAsync(command, analysis, output.DishName, cancellationToken);

            return new Result<MealPhotoAnalysisView, IntakeAiFailure>.Success(new MealPhotoAnalysisView(analysis.Id,
                analysis.ReferenceFoodId, dish.Value.Name, analysis.EstimatedGrams, analysis.Confidence,
                analysis.Alternatives, analysis.ExpiresAt, fits?.FirstOrDefault(), fits?.Skip(1).ToList()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            // An invariant of the analysis refused what the model said (portion or confidence out of range).
            return Failure(IntakeError.PhotoNotRecognized);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error analysing a meal photo for patient {PatientId}", command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    public async Task<Result<int, IntakeError>> Handle(PurgeMealPhotoAnalysesCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return new Result<int, IntakeError>.Success(
                await analysisRepository.DeleteByPatientIdAsync(command.PatientId, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error purging the photo analyses of patient {PatientId}",
                command.PatientId);
            return new Result<int, IntakeError>.Failure(IntakeError.UnexpectedError);
        }
    }

    public async Task<Result<int, IntakeError>> Handle(PurgeExpiredMealPhotoAnalysesCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return new Result<int, IntakeError>.Success(
                await analysisRepository.DeleteExpiredAsync(timeProvider.GetUtcNow(), cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error purging the expired photo analyses");
            return new Result<int, IntakeError>.Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>
    ///     The suggestion for the dish first, then one per alternative in order. Null when there is none (no plan
    ///     published yet, or the suggestion failed): a suggestion never fails the analysis.
    /// </summary>
    private async Task<IReadOnlyList<PlanFit?>?> PlanFitsAsync(AnalyzeMealPhotoCommand command,
        MealPhotoAnalysis analysis, string dishName, CancellationToken cancellationToken)
    {
        try
        {
            var serverToday = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var localDate = command.LocalDate is { } day && Math.Abs(day.DayNumber - serverToday.DayNumber) <= 1
                ? day
                : (DateOnly?)null;
            var candidates = analysis.Alternatives
                .Select(a => new PlanFitCandidate(a.ReferenceFoodId ?? 0, a.Grams, [a.Name]))
                .Prepend(new PlanFitCandidate(analysis.ReferenceFoodId, analysis.EstimatedGrams, [dishName]))
                .ToList();

            var result = await planFitQueryService.Handle(new GetPlanFitsQuery(command.PatientId, localDate,
                candidates), cancellationToken);
            return result is Result<IReadOnlyList<PlanFit?>, IntakeError>.Success success ? success.Value : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The plan suggestion of a meal photo for patient {PatientId} failed",
                command.PatientId);
            return null;
        }
    }

    /// <summary>
    ///     What leaves with the image: the task, its bounds and the catalog names to prefer. No identifier, no person's
    ///     name, no diary, no target.
    /// </summary>
    internal static object InputOf(IReadOnlyList<string>? catalogDishNames = null)
    {
        return new
        {
            task = "recognize-main-dish",
            portionGrams = new { minimum = MealPhotoPortion.MinimumGrams, maximum = MealPhotoPortion.MaximumGrams },
            maximumAlternatives = MealPhotoAnalysis.MaximumAlternatives,
            dishNameLanguage = CatalogLanguage,
            catalogDishNames = catalogDishNames ?? []
        };
    }

    /// <summary>
    ///     The catalog names the model uses verbatim when one of them is the dish, so a known dish is not named
    ///     differently and created twice (see <see cref="CatalogNameHintsReader" />).
    /// </summary>
    private Task<IReadOnlyList<string>> CatalogNameHintsAsync(CancellationToken cancellationToken)
    {
        return new CatalogNameHintsReader(diaryEntryRepository, foodCatalogContextFacade, catalogNameHintsCache,
            configuration, logger).ReadAsync(cancellationToken);
    }

    /// <summary>Step 4: local catalog → external providers → created from the estimate.</summary>
    private async Task<(int ReferenceFoodId, string Name)?> ResolveDishAsync(MealPhotoRecognitionOutput output,
        long generationId, CancellationToken cancellationToken)
    {
        var name = output.DishName.Trim();

        var local = (await foodCatalogContextFacade.ResolveByNames([name], 1, cancellationToken))
            .FirstOrDefault(r => r.IsResolved);
        if (local is not null) return (local.ReferenceFoodId!.Value, local.LocalName ?? name);

        var external = await foodCatalogContextFacade.ResolveByNameWithProviders(name, cancellationToken);
        if (external is { IsResolved: true }) return (external.ReferenceFoodId!.Value, external.LocalName ?? name);

        var nutrients = output.NutrientsPer100g;
        var created = await foodCatalogContextFacade.CreateAiEstimatedFood(name,
            new FoodNutrientsItem(nutrients.Kcal, nutrients.Protein, nutrients.Carb, nutrients.Fat), generationId,
            cancellationToken);
        return created is null ? null : (created.ReferenceFoodId, created.LocalName);
    }

    /// <summary>
    ///     Each alternative follows the main dish's path, so the patient can choose it: the local catalog,
    ///     then a food created from the alternative's own nutrients (<c>meal-photo-recognition@3</c>), which the catalog
    ///     resolves to a clearly similar entry (Similar Dish Reused, Not Duplicated) or creates, idempotent by source
    ///     hash. The main dish is not repeated, nor one food twice.
    /// </summary>
    /// <remarks>
    ///     No external provider is waited for an alternative (the patient is waiting for the analysis);
    ///     the AI's nutrients stand in for it. An alternative without nutrients (an output shaped as v2) or with
    ///     nutrients the catalog refuses (AI Estimated Nutrients Coherent) stays without a food, as before: it is
    ///     shown but cannot be chosen, and the analysis never fails because of it.
    /// </remarks>
    private async Task<IReadOnlyList<MealPhotoAlternative>> ResolveAlternativesAsync(MealPhotoRecognitionOutput output,
        int dishFoodId, long generationId, CancellationToken cancellationToken)
    {
        var proposed = (output.Alternatives ?? []).Take(MealPhotoAnalysis.MaximumAlternatives).ToList();
        if (proposed.Count == 0) return [];

        var resolved = await foodCatalogContextFacade.ResolveByNames(proposed.Select(a => a.Name.Trim()).ToList(),
            proposed.Count, cancellationToken);

        var alternatives = new List<MealPhotoAlternative>();
        for (var i = 0; i < proposed.Count; i++)
        {
            var name = proposed[i].Name.Trim();
            var match = i < resolved.Count && resolved[i].IsResolved ? resolved[i] : null;
            var (foodId, foodName) = match is not null
                ? (match.ReferenceFoodId, match.LocalName ?? name)
                : await CreateAlternativeAsync(proposed[i], name, generationId, cancellationToken);

            if (foodId == dishFoodId) continue;
            if (foodId is not null && alternatives.Any(a => a.ReferenceFoodId == foodId)) continue;
            alternatives.Add(new MealPhotoAlternative(foodName, proposed[i].Grams, foodId));
        }

        return alternatives;
    }

    /// <summary>
    ///     The food of an alternative the local catalog lacks, from its own nutrients; (null, its name) when it
    ///     brings none or the catalog refuses them.
    /// </summary>
    private async Task<(int? ReferenceFoodId, string Name)> CreateAlternativeAsync(MealPhotoAlternativeOutput alternative,
        string name, long generationId, CancellationToken cancellationToken)
    {
        if (alternative.NutrientsPer100g is not { } nutrients) return (null, name);

        var created = await foodCatalogContextFacade.CreateAiEstimatedFood(name,
            new FoodNutrientsItem(nutrients.Kcal, nutrients.Protein, nutrients.Carb, nutrients.Fat), generationId,
            cancellationToken);
        if (created is not null) return (created.ReferenceFoodId, created.LocalName);

        logger.LogInformation("Meal photo of generation {GenerationId}: an alternative could not be resolved nor " +
                              "created; it stays without a food", generationId);
        return (null, name);
    }

    private int MaxBytes => configuration.GetValue<int?>("Intake:MealPhoto:MaxBytes") is > 0 and var configured
        ? configured
        : DefaultMaxBytes;

    private TimeSpan AnalysisLifetime => TimeSpan.FromHours(
        configuration.GetValue<int?>("Intake:MealPhoto:AnalysisLifetimeHours") is > 0 and var hours
            ? hours
            : DefaultAnalysisLifetimeHours);

    private static Result<MealPhotoAnalysisView, IntakeAiFailure> Failure(IntakeError error)
    {
        return new Result<MealPhotoAnalysisView, IntakeAiFailure>.Failure(IntakeAiFailure.Of(error));
    }

    private static Result<MealPhotoAnalysisView, IntakeAiFailure> Failure(AiError error)
    {
        return new Result<MealPhotoAnalysisView, IntakeAiFailure>.Failure(IntakeAiFailure.Of(error));
    }
}

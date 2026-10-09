using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;

/// <summary>
///     IN-7 - Analyze Meal Photo (PT6.1 «Viendo tu foto…»): the AI recognizes the dish on the server, the catalog
///     resolves it, and the analysis waits 24 hours for the patient to confirm it on PT7.
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
///     output is <see cref="IntakeError.PhotoNotRecognized" /> (422, PT7.3 → log it by hand);
///     5. the dish resolved, in this order: the local catalog (FC-2), the external providers (Search Food, idempotent
///     by source hash), and last a food created from the AI's nutrients, which the catalog refuses when they are
///     incoherent (then <see cref="IntakeError.PhotoNotRecognized" />). The alternatives are resolved against the
///     local catalog only;
///     6. the analysis stored without any image, with its lifetime.
///     It creates no diary entry: logging is <c>photo-logs</c> with the <c>analysisId</c>.
///     Business rule: Photo Never Stored (IN-7). The bytes live in this method and in the request to the provider;
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
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<MealPhotoAnalysisCommandService> logger) : IMealPhotoAnalysisCommandService
{
    /// <summary>IN-7: «máximo 2 MB configurable» (<c>Intake:MealPhoto:MaxBytes</c>).</summary>
    public const int DefaultMaxBytes = 2 * 1024 * 1024;

    /// <summary>IN-7: «de forma temporal (por ejemplo 24 h)» (<c>Intake:MealPhoto:AnalysisLifetimeHours</c>).</summary>
    public const int DefaultAnalysisLifetimeHours = 24;

    /// <summary>
    ///     DECISIÓN IN-7: dish names are catalog vocabulary, in Spanish whatever the reader's language (as the
    ///     ingredients of IA-3), so the prompt is always rendered in Spanish. The output has no text for a reader.
    /// </summary>
    public const string CatalogLanguage = "es";

    /// <summary>IN-7: how many catalog names go in the prompt (<c>FoodCatalog:PromptCatalogNamesCount</c>).</summary>
    public const int DefaultCatalogNameHints = 100;

    /// <summary>IN-7: how long the list is kept in memory (<c>FoodCatalog:PromptCatalogNamesCacheMinutes</c>).</summary>
    public const int DefaultCatalogNameHintsCacheMinutes = 60;

    /// <summary>
    ///     DECISIÓN IN-7: a food becomes a hint only when at least this many different patients logged it, so the list is
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
            // 2. Business rule: Photo Leaves Without Patient Data (IN-7).
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

            var alternatives = await ResolveAlternativesAsync(output, dish.Value.ReferenceFoodId, cancellationToken);

            // 6. The analysis, without the image.
            var analysis = new MealPhotoAnalysis(command.PatientId, dish.Value.ReferenceFoodId,
                output.EstimatedGrams, new Confidence(output.Confidence), alternatives, outcome.GenerationId,
                timeProvider.GetUtcNow().Add(AnalysisLifetime));

            await analysisRepository.AddAsync(analysis, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            return new Result<MealPhotoAnalysisView, IntakeAiFailure>.Success(new MealPhotoAnalysisView(analysis.Id,
                analysis.ReferenceFoodId, dish.Value.Name, analysis.EstimatedGrams, analysis.Confidence,
                analysis.Alternatives, analysis.ExpiresAt));
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
    ///     IN-7. Up to <c>FoodCatalog:PromptCatalogNamesCount</c> (100) names of the catalog the model uses verbatim when
    ///     one of them is the dish, so a known dish is not named differently and created twice.
    /// </summary>
    /// <remarks>
    ///     DECISIÓN IN-7: first the most logged foods of the whole platform (among those logged by at least
    ///     <see cref="MinimumPatientsPerHint" /> patients), then, until the count is reached, verified foods of the
    ///     catalog (local overrides first, then by name). Never the patient's own dishes: they would be data of their
    ///     diary sent to the AI, which IN-7 forbids («la IA recibe la imagen sin ningún dato del paciente»).
    ///     The list names nobody, so it is cached for the whole platform for
    ///     <c>FoodCatalog:PromptCatalogNamesCacheMinutes</c> (60) instead of aggregating <c>diary_entries</c> on every
    ///     analysis. A failure only leaves the list empty, and an empty list is not cached.
    /// </remarks>
    private async Task<IReadOnlyList<string>> CatalogNameHintsAsync(CancellationToken cancellationToken)
    {
        if (catalogNameHintsCache.TryGet(out var cached)) return cached;

        try
        {
            var count = HintsCount;
            if (count == 0) return [];

            var ids = await diaryEntryRepository.ListMostLoggedReferenceFoodIdsAsync(MinimumPatientsPerHint, count,
                cancellationToken);
            var popular = ids.Count == 0 ? [] : await foodCatalogContextFacade.GetReferenceFoodsByIds(ids, cancellationToken);
            var byId = popular.ToDictionary(f => f.ReferenceFoodId, f => f.LocalName);
            var names = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

            if (names.Count < count)
            {
                var verified = await foodCatalogContextFacade.ListVerifiedFoods(count, byId.Keys.ToList(),
                    cancellationToken);
                names.AddRange(verified.Select(f => f.LocalName));
            }

            var hints = names.Distinct(StringComparer.OrdinalIgnoreCase).Take(count).ToList();
            if (hints.Count > 0) catalogNameHintsCache.Set(hints, HintsLifetime);
            return hints;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the catalog name hints for a meal photo; none are sent");
            return [];
        }
    }

    private int HintsCount => configuration.GetValue<int?>("FoodCatalog:PromptCatalogNamesCount") is >= 0 and var count
        ? Math.Min(count, 1000)
        : DefaultCatalogNameHints;

    private TimeSpan HintsLifetime => TimeSpan.FromMinutes(
        configuration.GetValue<int?>("FoodCatalog:PromptCatalogNamesCacheMinutes") is > 0 and var minutes
            ? minutes
            : DefaultCatalogNameHintsCacheMinutes);

    /// <summary>IN-7, step 4: local catalog → external providers → created from the estimate.</summary>
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
    ///     DECISIÓN IN-7: the alternatives are resolved against the local catalog only (FC-2): no provider is waited
    ///     for and nothing is created for a dish the photo probably is not. The main dish is not repeated.
    /// </summary>
    private async Task<IReadOnlyList<MealPhotoAlternative>> ResolveAlternativesAsync(MealPhotoRecognitionOutput output,
        int dishFoodId, CancellationToken cancellationToken)
    {
        var proposed = (output.Alternatives ?? []).Take(MealPhotoAnalysis.MaximumAlternatives).ToList();
        if (proposed.Count == 0) return [];

        var resolved = await foodCatalogContextFacade.ResolveByNames(proposed.Select(a => a.Name.Trim()).ToList(),
            proposed.Count, cancellationToken);

        var alternatives = new List<MealPhotoAlternative>();
        for (var i = 0; i < proposed.Count; i++)
        {
            var match = i < resolved.Count && resolved[i].IsResolved ? resolved[i] : null;
            if (match?.ReferenceFoodId == dishFoodId) continue;
            alternatives.Add(new MealPhotoAlternative(match?.LocalName ?? proposed[i].Name, proposed[i].Grams,
                match?.ReferenceFoodId));
        }

        return alternatives;
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

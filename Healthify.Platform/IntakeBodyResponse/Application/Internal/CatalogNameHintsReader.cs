using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>
///     IN-7 and IA-3. Up to <c>FoodCatalog:PromptCatalogNamesCount</c> (100) names of the catalog the model uses
///     verbatim: the dish of a photo (<c>catalogDishNames</c>) and the ingredients of a meal idea
///     (<c>catalogIngredientNames</c>), so what the AI names is what FC-2 resolves.
/// </summary>
/// <remarks>
///     DECISIÓN IN-7: first the most logged foods of the whole platform (among those logged by at least
///     <see cref="MealPhotoAnalysisCommandService.MinimumPatientsPerHint" /> patients), then, until the count is
///     reached, verified foods of the catalog (local overrides first, then by name). Never the patient's own dishes:
///     they would be data of their diary sent to the AI, which IN-7 forbids («la IA recibe la imagen sin ningún dato
///     del paciente»).
///     The list names nobody, so it is cached for the whole platform for
///     <c>FoodCatalog:PromptCatalogNamesCacheMinutes</c> (60) instead of aggregating <c>diary_entries</c> on every
///     call. A failure only leaves the list empty, and an empty list is not cached.
///     DECISIÓN IA-3: meal ideas use the same list as IN-7: one platform vocabulary, one cache.
/// </remarks>
public sealed class CatalogNameHintsReader(
    IDiaryEntryRepository diaryEntryRepository,
    IFoodCatalogContextFacade foodCatalogContextFacade,
    ICatalogNameHintsCache catalogNameHintsCache,
    IConfiguration configuration,
    ILogger logger)
{
    public async Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (catalogNameHintsCache.TryGet(out var cached)) return cached;

        try
        {
            var count = HintsCount;
            if (count == 0) return [];

            var ids = await diaryEntryRepository.ListMostLoggedReferenceFoodIdsAsync(
                MealPhotoAnalysisCommandService.MinimumPatientsPerHint, count, cancellationToken);
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
            logger.LogWarning(ex, "Could not read the catalog name hints for the AI; none are sent");
            return [];
        }
    }

    private int HintsCount => configuration.GetValue<int?>("FoodCatalog:PromptCatalogNamesCount") is >= 0 and var count
        ? Math.Min(count, 1000)
        : MealPhotoAnalysisCommandService.DefaultCatalogNameHints;

    private TimeSpan HintsLifetime => TimeSpan.FromMinutes(
        configuration.GetValue<int?>("FoodCatalog:PromptCatalogNamesCacheMinutes") is > 0 and var minutes
            ? minutes
            : MealPhotoAnalysisCommandService.DefaultCatalogNameHintsCacheMinutes);
}

using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal;
using Healthify.Platform.FoodCatalog.Application.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.FoodCatalog.Domain.Model.Queries;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.FoodCatalog.Application.Acl;

/// <inheritdoc cref="IFoodCatalogContextFacade" />
public class FoodCatalogContextFacade(
    IReferenceFoodQueryService queryService,
    IReferenceFoodCommandService commandService) : IFoodCatalogContextFacade
{
    public async Task<ReferenceFoodItem?> GetReferenceFoodById(int referenceFoodId,
        CancellationToken ct = default)
    {
        try
        {
            var referenceFood = await queryService.Handle(new GetReferenceFoodByIdQuery(referenceFoodId), ct);
            return referenceFood is null ? null : ToItem(referenceFood);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ReferenceFoodItem>> ListVerifiedFoods(int max,
        IReadOnlyCollection<int>? excludedIds = null, CancellationToken ct = default)
    {
        try
        {
            return (await queryService.Handle(new ListVerifiedReferenceFoodsQuery(max, excludedIds), ct))
                .Select(ToItem).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ReferenceFoodItem>> GetReferenceFoodsByIds(IReadOnlyList<int> referenceFoodIds,
        CancellationToken ct = default)
    {
        try
        {
            if (referenceFoodIds.Count == 0) return [];
            return (await queryService.Handle(new GetReferenceFoodsByIdsQuery(referenceFoodIds), ct)).Select(ToItem)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ReferenceFoodItem>> SearchReferenceFoods(string term, int max,
        CancellationToken ct = default)
    {
        try
        {
            var results = await queryService.Handle(new SearchReferenceFoodsQuery(term, max), ct);
            return results.Select(ToItem).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ResolvedFoodItem>> ResolveByNames(IReadOnlyList<string> names, int max,
        CancellationToken ct = default)
    {
        try
        {
            if (names.Count == 0) return [];
            var resolutions = await queryService.Handle(new ResolveReferenceFoodsByNamesQuery(names, max), ct);
            return resolutions.Select(r => r.ReferenceFood is null
                    ? new ResolvedFoodItem(r.Name, null, null)
                    : new ResolvedFoodItem(r.Name, r.ReferenceFood.Id.Value, r.ReferenceFood.EnergyKcalPer100g,
                        r.ReferenceFood.ProteinGPer100g, r.ReferenceFood.CarbGPer100g, r.ReferenceFood.FatGPer100g,
                        r.ReferenceFood.LocalNameText))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<ResolvedFoodItem?> ResolveByNameWithProviders(string name, CancellationToken ct = default)
    {
        try
        {
            var result = await commandService.Handle(new ResolveFoodWithProvidersCommand(name), ct);
            if (result is not Result<FoodNameResolution, FoodCatalogError>.Success { Value: var resolution })
                return null;
            return resolution.ReferenceFood is null
                ? new ResolvedFoodItem(resolution.Name, null, null)
                : ToResolved(resolution.Name, resolution.ReferenceFood);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ReferenceFoodItem?> CreateAiEstimatedFood(string name, FoodNutrientsItem nutrientsPer100g,
        long aiGenerationId, CancellationToken ct = default)
    {
        try
        {
            var result = await commandService.Handle(new CreateAiEstimatedFoodCommand(name,
                nutrientsPer100g.EnergyKcalPer100g, nutrientsPer100g.ProteinGPer100g, nutrientsPer100g.CarbGPer100g,
                nutrientsPer100g.FatGPer100g, aiGenerationId), ct);
            return result is Result<ReferenceFood, FoodCatalogError>.Success { Value: var food }
                ? ToItem(food)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static ResolvedFoodItem ToResolved(string name, ReferenceFood referenceFood)
    {
        return new ResolvedFoodItem(name, referenceFood.Id.Value, referenceFood.EnergyKcalPer100g,
            referenceFood.ProteinGPer100g, referenceFood.CarbGPer100g, referenceFood.FatGPer100g,
            referenceFood.LocalNameText);
    }

    private static ReferenceFoodItem ToItem(ReferenceFood referenceFood)
    {
        return new ReferenceFoodItem(
            referenceFood.Id.Value,
            referenceFood.LocalNameText,
            referenceFood.EnergyKcalPer100g,
            referenceFood.ProteinGPer100g,
            referenceFood.CarbGPer100g,
            referenceFood.FatGPer100g,
            referenceFood.IsLocalOverride);
    }
}

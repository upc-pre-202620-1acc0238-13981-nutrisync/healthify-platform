using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.FoodCatalog.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Reference Food Translated" (Food Catalog, Subflow 6.2): a record that crossed the
///     anti-corruption layer becomes an entry of the local catalog.
/// </summary>
/// <remarks>
///     This handler is the only writer of the catalog that reacts to an import. Keeping the write
///     here rather than inside Import Catalog Snapshot is what guarantees that every stored row went
///     through the translation: there is no other way in.
/// </remarks>
public class OnReferenceFoodTranslatedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnReferenceFoodTranslatedHandler> logger) : IEventHandler<ReferenceFoodTranslated>
{
    public async Task Handle(ReferenceFoodTranslated notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IReferenceFoodCommandService>();

        var result = await commandService.Handle(
            new CacheFoodLocallyCommand(
                notification.LocalName,
                notification.EnergyKcalPer100g,
                notification.ProteinGPer100g,
                notification.CarbGPer100g,
                notification.FatGPer100g,
                notification.SourceHash),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not cache the translated reference food {LocalName}",
                notification.LocalName);
    }
}

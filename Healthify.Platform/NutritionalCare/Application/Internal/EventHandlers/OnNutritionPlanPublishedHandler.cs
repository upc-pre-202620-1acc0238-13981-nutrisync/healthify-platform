using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Nutrition Plan Published" (Nutritional Care, Subflow 3.5): publishing a plan
///     publishes the reduced contract the patient actually receives.
/// </summary>
public class OnNutritionPlanPublishedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnNutritionPlanPublishedHandler> logger) : IEventHandler<NutritionPlanPublished>
{
    public async Task Handle(NutritionPlanPublished notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<INutritionPlanCommandService>();

        var result = await commandService.Handle(
            new PublishActiveTargetsCommand(notification.PlanId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not publish the active targets contract for plan {PlanId}",
                notification.PlanId);
    }
}

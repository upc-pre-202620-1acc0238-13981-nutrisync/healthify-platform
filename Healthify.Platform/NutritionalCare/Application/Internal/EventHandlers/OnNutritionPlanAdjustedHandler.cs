using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Nutrition Plan Adjusted" (Nutritional Care, Subflow 3.6): an adjustment between
///     visits republishes the contract, exactly as a first publication does.
/// </summary>
public class OnNutritionPlanAdjustedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnNutritionPlanAdjustedHandler> logger) : IEventHandler<NutritionPlanAdjusted>
{
    public async Task Handle(NutritionPlanAdjusted notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<INutritionPlanCommandService>();

        var result = await commandService.Handle(
            new PublishActiveTargetsCommand(notification.PlanId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not publish the active targets contract for adjusted plan {PlanId}",
                notification.PlanId);
    }
}

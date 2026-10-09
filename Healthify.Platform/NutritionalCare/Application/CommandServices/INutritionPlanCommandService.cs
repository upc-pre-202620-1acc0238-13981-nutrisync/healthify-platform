using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

public interface INutritionPlanCommandService
{
    /// <summary>Subflow 3.3 - Propose Targets.</summary>
    Task<Result<NutritionPlan, NutritionalCareError>> Handle(ProposeTargetsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 3.4 - Prescribe Targets.</summary>
    Task<Result<NutritionPlan, NutritionalCareError>> Handle(PrescribeTargetsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 3.5 - Publish Nutrition Plan.</summary>
    Task<Result<NutritionPlan, NutritionalCareError>> Handle(PublishNutritionPlanCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Subflows 3.5 and 3.6 - Publish Active Targets. Invoked by a policy, never by an endpoint.
    /// </summary>
    Task<Result<NutritionPlan, NutritionalCareError>> Handle(PublishActiveTargetsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 3.6 - Adjust Nutrition Plan.</summary>
    Task<Result<NutritionPlan, NutritionalCareError>> Handle(AdjustNutritionPlanCommand command,
        CancellationToken cancellationToken = default);
}

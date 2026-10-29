using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST;

/// <summary>
///     Subflows 3.3 to 3.6 of the Nutritional Care bounded context: propose, prescribe, publish and
///     adjust.
/// </summary>
/// <remarks>
///     Publish Active Targets has no endpoint here on purpose. It is a policy that fires when a plan
///     is published or adjusted, and it is the only thing about a plan that ever crosses to the
///     patient.
/// </remarks>
[ApiController]
[Route("api/v1/nutrition-plans")]
[Authorize(Roles = "Practitioner")]
[Tags("Nutrition Plans")]
[Produces("application/json")]
public class NutritionPlansController(
    INutritionPlanCommandService commandService,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpPost("{planId:int}/adjustments")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Adjust the plan between visits",
        Description =
            "Creates the next version of the plan and replaces this one, which is kept and never deleted. Every " +
            "version needs a change reason. Only a person can make this change: no automatic alert can. " +
            "Restrictions come from the list and guidelines from the catalog or as custom text; old free-text " +
            "restrictions that do not match the list are not carried over, and the new version records them as left " +
            "out.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The new version was published.",
        typeof(NutritionPlanResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The change reason is missing, a restriction code is unknown, or a custom guideline is invalid.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner who owns the plan.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No plan was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "This version is not published, or was already superseded.", typeof(ProblemDetails))]
    public async Task<IActionResult> AdjustNutritionPlan(int planId,
        [FromBody] AdjustNutritionPlanResource resource)
    {
        var command = AdjustNutritionPlanCommandAssembler.ToCommand(
            planId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToPlanResult(result, localizer,
            StatusCodes.Status201Created);
    }
}

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
    [HttpPost("target-proposals")]
    [Consumes("application/json")]
    [Obsolete("Deprecated by X-1: use POST /consultations/{id}/target-proposal of the guided consultation.")]
    [SwaggerOperation(
        Summary = "Propose targets (deprecated)",
        Description =
            "Deprecated: the app uses the third step of the guided consultation instead. It still works the same " +
            "way for older versions of the app. Calculates targets from parameters the practitioner chooses: the " +
            "equation, the reference weight, the activity factor, the deficit and the protein target; the system " +
            "decides none of them. The basal metabolic rate comes from the chosen equation, the total expenditure " +
            "is that rate times the activity factor, the target energy is the total expenditure minus the deficit, " +
            "and carbohydrate is what is left after protein and fat. Every number can be checked by hand from the " +
            "calculation saved with the plan.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The targets were proposed.",
        typeof(NutritionPlanResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "A calculation parameter is missing or out of range.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner who issued the diagnosis.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "There is no active diagnosis, or no clinical measurement to calculate from.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> ProposeTargets([FromBody] ProposeTargetsResource resource)
    {
        var command = ProposeTargetsCommandAssembler.ToCommand(this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToPlanResult(result, localizer,
            StatusCodes.Status201Created);
    }

    [HttpPost("{planId:int}/prescribed-targets")]
    [Consumes("application/json")]
    [Obsolete("Deprecated by X-1: use PUT /consultations/{id}/targets of the guided consultation.")]
    [SwaggerOperation(
        Summary = "Prescribe the targets (deprecated)",
        Description =
            "Deprecated: the app uses the third step of the guided consultation instead. It still works the same " +
            "way for older versions of the app. The practitioner accepts the proposed targets as they are, or " +
            "replaces the numbers and says why. Replacing them without a reason is refused: every target can be " +
            "traced back to a person.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The targets were prescribed.", typeof(NutritionPlanResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "An override was requested without a reason.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner who owns the plan.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No plan was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "This version was already prescribed or published.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> PrescribeTargets(int planId,
        [FromBody] PrescribeTargetsResource resource)
    {
        var command = PrescribeTargetsCommandAssembler.ToCommand(
            planId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToPlanResult(result, localizer);
    }

    [HttpPost("{planId:int}/publication")]
    [Consumes("application/json")]
    [Obsolete("Deprecated by X-1: use POST /consultations/{id}/publication of the guided consultation.")]
    [SwaggerOperation(
        Summary = "Publish the nutrition plan (deprecated)",
        Description =
            "Deprecated: the app uses the last step of the guided consultation instead. It still works the same way " +
            "for older versions of the app. Publishing makes this version the active plan and sends the patient " +
            "only their targets, guidelines and restrictions; the diagnosis and the calculation are never sent. " +
            "Restrictions are chosen from a list (lactose free, gluten free, vegan, vegetarian, tree nut free, " +
            "shellfish free, kosher, halal); guidelines come from the catalog, and any other text is kept as a " +
            "custom guideline (3 to 140 characters, up to 5).")]
    [SwaggerResponse(StatusCodes.Status200OK, "The plan was published.", typeof(NutritionPlanResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "An unknown restriction code, or a custom guideline out of length or beyond five.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner who owns the plan.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No plan was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The patient already has an active version, or this one is already published.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "The targets have not been prescribed yet, or the diagnosis is missing.", typeof(ProblemDetails))]
    public async Task<IActionResult> PublishNutritionPlan(int planId,
        [FromBody] PublishNutritionPlanResource resource)
    {
        var command = PublishNutritionPlanCommandAssembler.ToCommand(
            planId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToPlanResult(result, localizer);
    }

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

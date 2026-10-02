using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;
using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.CareRelationship.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.CareRelationship.Interfaces.REST;

/// <summary>
///     IA-1. The patient's choice of AI functions (PT21 "Funciones con IA", PT21.IA). Only the patient reads or
///     changes their own; it is coherent with the consent to AI processing of their care link (CR-2).
/// </summary>
[ApiController]
[Route("api/v1/patients/{patientId:int}/ai-preferences")]
[Authorize(Roles = "Patient")]
[Tags("AI Preferences")]
[Produces("application/json")]
public class PatientAiPreferencesController(
    IAiPreferencesCommandService commandService,
    IAiPreferencesQueryService queryService,
    IStringLocalizer<CareRelationshipMessages> localizer) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Get the AI preferences of a patient",
        Description =
            "Returns whether the patient agreed to AI processing and which AI functions are turned on: the weekly " +
            "summary, meal ideas, suggested questions for the visit and meal recognition from a photo. Without that " +
            "agreement every function shows as off. A patient who never chose anything gets everything off, never a " +
            "not found.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The AI preferences.", typeof(AiPreferencesResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient may read their own preferences.")]
    public async Task<IActionResult> GetAiPreferences(int patientId)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();

        var status = await queryService.Handle(new GetAiPreferencesByPatientIdQuery(patientId),
            HttpContext.RequestAborted);
        return Ok(AiPreferencesResourceAssembler.ToResource(status));
    }

    [HttpPut]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Choose the AI functions",
        Description =
            "The patient turns each AI function on or off. Turning a function off stops using the diary for it and " +
            "deletes what it generated; the plan and the records do not change. Turning one on requires the patient " +
            "to have agreed to AI processing first; otherwise the request is refused with a conflict, and the app " +
            "can ask for that agreement first. The meal photo recognition setting is optional: if it is not sent, " +
            "it keeps its current value. Turning it off also deletes the temporary photo analyses.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The AI preferences as they now are.", typeof(AiPreferencesResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient may change their own preferences.")]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "A function was turned on before the patient agreed to AI processing.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> UpdateAiPreferences(int patientId,
        [FromBody] UpdateAiPreferencesResource resource)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();

        var command = UpdateAiPreferencesCommandAssembler.ToCommand(patientId, resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToAiPreferencesResult(result, localizer);
    }
}

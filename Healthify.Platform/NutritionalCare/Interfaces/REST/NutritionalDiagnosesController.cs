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

/// <summary>Subflow 3.2 of the Nutritional Care bounded context, clinical phase 2.</summary>
[ApiController]
[Route("api/v1/nutritional-diagnoses")]
[Authorize(Roles = "Practitioner")]
[Tags("Nutritional Diagnoses")]
[Produces("application/json")]
public class NutritionalDiagnosesController(
    INutritionalDiagnosisCommandService commandService,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [Obsolete("Deprecated by X-1: use PUT /consultations/{id}/diagnosis of the guided consultation.")]
    [SwaggerOperation(
        Summary = "Issue a nutritional diagnosis (deprecated)",
        Description =
            "Deprecated: the app uses the second step of the guided consultation instead (suggest and then set the " +
            "diagnosis). It still works the same way for older versions of the app. Records a diagnosis from a " +
            "closed assessment, with the reasoning behind it. A patient has one current diagnosis at a time and " +
            "never sees it in the app. The diagnosis can be given as a code from the closed list instead of free " +
            "text, and then the reasoning is written from the measurement if it is left empty. If the patient " +
            "already has a current diagnosis the request is refused; only a consultation replaces it.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The diagnosis was issued.",
        typeof(NutritionalDiagnosisResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The statement or the rationale is missing, or the diagnosis code is unknown.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No assessment was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The patient already has an active diagnosis.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The assessment has not been closed yet.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> IssueDiagnosis([FromBody] IssueDiagnosisResource resource)
    {
        var command = IssueDiagnosisCommandAssembler.ToCommand(this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToDiagnosisResult(result, localizer,
            StatusCodes.Status201Created);
    }
}

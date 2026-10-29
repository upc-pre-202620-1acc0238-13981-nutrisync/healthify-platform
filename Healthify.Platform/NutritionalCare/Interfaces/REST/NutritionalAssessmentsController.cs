using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
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
///     Subflow 3.1 of the Nutritional Care bounded context, clinical phase 1.
/// </summary>
/// <remarks>
///     Every endpoint of this bounded context is practitioner only. There is no patient-facing route
///     anywhere in Nutritional Care: what the patient receives is the published contract, and that
///     travels as an event to Intake, not as an endpoint here.
/// </remarks>
[ApiController]
[Route("api/v1/nutritional-assessments")]
[Authorize(Roles = "Practitioner")]
[Tags("Nutritional Assessments")]
[Produces("application/json")]
public class NutritionalAssessmentsController(
    INutritionalAssessmentQueryService queryService,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpGet("{assessmentId:int}")]
    [SwaggerOperation(
        Summary = "Get an assessment",
        Description =
            "Returns an assessment with its measurements.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The assessment.", typeof(NutritionalAssessmentResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner who owns the assessment.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No assessment was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetAssessmentById(int assessmentId)
    {
        var assessment = await queryService.Handle(
            new GetAssessmentByIdQuery(assessmentId), HttpContext.RequestAborted);

        if (assessment is null)
            return NutritionalCareActionResultAssembler.ToNotFoundResult(
                NutritionalCareError.AssessmentNotFound, localizer);
        if (assessment.PractitionerId != this.GetAuthenticatedUserId()) return Forbid();

        return Ok(NutritionalAssessmentResourceAssembler.ToResource(assessment));
    }
}

using Healthify.Platform.NutritionalCare.Application.CommandServices;
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
    INutritionalAssessmentCommandService commandService,
    INutritionalAssessmentQueryService queryService,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [Obsolete("Deprecated by X-1: use the guided consultation, POST /patients/{patientId}/consultations and PUT /consultations/{id}/measurement.")]
    [SwaggerOperation(
        Summary = "Record a nutritional assessment (deprecated)",
        Description =
            "Deprecated: the app uses the first step of the guided consultation instead (start a consultation, then " +
            "record its measurement). It still works the same way for older versions of the app. Records a " +
            "nutritional assessment with the patient's habits, medical history and physical activity, all of them " +
            "required. An active care link is required: without consent, nothing about the patient can be written.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The assessment was recorded.",
        typeof(NutritionalAssessmentResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Habits, history or activity are missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> RecordAssessment([FromBody] RecordAssessmentResource resource)
    {
        var command = RecordAssessmentCommandAssembler.ToCommand(this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToAssessmentResult(result, localizer,
            StatusCodes.Status201Created);
    }

    [HttpPost("{assessmentId:int}/clinical-measurements")]
    [Consumes("application/json")]
    [Obsolete("Deprecated by X-1: use PUT /consultations/{id}/measurement of the guided consultation.")]
    [SwaggerOperation(
        Summary = "Take a clinical measurement (deprecated)",
        Description =
            "Deprecated: the app uses the first step of the guided consultation instead. It still works the same " +
            "way for older versions of the app. Records a measurement taken by the practitioner, together with how " +
            "it was taken. That is why a clinic measurement weighs more than a home weigh-in, and why the two are " +
            "never mixed. A closed assessment accepts no new measurements.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The measurement was taken.",
        typeof(NutritionalAssessmentResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The protocol or a value is missing or invalid.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner who owns the assessment.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No assessment was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The assessment is closed and therefore immutable.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> TakeClinicalMeasurement(int assessmentId,
        [FromBody] TakeClinicalMeasurementResource resource)
    {
        var command = TakeClinicalMeasurementCommandAssembler.ToCommand(
            assessmentId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToAssessmentResult(result, localizer,
            StatusCodes.Status201Created);
    }

    [HttpPost("{assessmentId:int}/closure")]
    [Obsolete("Deprecated by X-1: step 1 of the guided consultation (PUT /consultations/{id}/measurement) closes the assessment.")]
    [SwaggerOperation(
        Summary = "Close the assessment (deprecated)",
        Description =
            "Deprecated: the app uses the first step of the guided consultation instead, which closes the " +
            "assessment on its own. It still works the same way for older versions of the app. Once closed, the " +
            "assessment cannot be edited: a correction creates a new assessment that refers to this one, so what " +
            "was known at the time is kept.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The assessment was closed.",
        typeof(NutritionalAssessmentResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner who owns the assessment.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No assessment was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The assessment was already closed.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> CloseAssessment(int assessmentId)
    {
        var command = CloseAssessmentCommandAssembler.ToCommand(assessmentId, this.GetAuthenticatedUserId());
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToAssessmentResult(result, localizer);
    }

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

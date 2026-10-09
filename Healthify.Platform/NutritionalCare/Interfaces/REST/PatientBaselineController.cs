using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Services;
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
///     NC-1. The patient baseline: birth date, biological sex, height and medical history, recorded
///     once (EV-1) and edited afterwards from the Summary tab or from EV-2.
/// </summary>
/// <remarks>
///     Practitioner only. The patient does not see their own medical history in the app.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Practitioner")]
[Tags("Patient Baseline")]
[Produces("application/json")]
public class PatientBaselineController(
    IPatientBaselineCommandService commandService,
    IPatientBaselineQueryService queryService,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IClinicalDateProvider clinicalDate,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpPost("{patientId:int}/baseline")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Record the patient baseline",
        Description =
            "Records the patient's basic data once: biological sex (female or male), birth date, height and medical " +
            "history from a closed list (an empty list means none). The height is not asked again at each " +
            "consultation. An active care link is required.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The baseline was recorded.",
        typeof(PatientBaselineResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "Birth date, biological sex, height or a medical condition is invalid.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The patient already has a baseline; edit it instead.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> RecordPatientBaseline(int patientId,
        [FromBody] RecordPatientBaselineResource resource)
    {
        var command = RecordPatientBaselineCommandAssembler.ToCommand(
            patientId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToBaselineResult(result, localizer, clinicalDate.Today(),
            StatusCodes.Status201Created);
    }

    [HttpPut("{patientId:int}/baseline")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Edit the patient baseline",
        Description =
            "Edits the patient's basic data. Past assessments are not changed: each one keeps the age, sex, height " +
            "and medical history it was recorded with.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The baseline was updated.", typeof(PatientBaselineResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "Birth date, biological sex, height or a medical condition is invalid.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The patient has no baseline yet.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> UpdatePatientBaseline(int patientId,
        [FromBody] UpdatePatientBaselineResource resource)
    {
        var command = UpdatePatientBaselineCommandAssembler.ToCommand(
            patientId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToBaselineResult(result, localizer, clinicalDate.Today());
    }

    [HttpGet("{patientId:int}/baseline")]
    [SwaggerOperation(
        Summary = "Get the patient baseline",
        Description =
            "Returns the patient's basic data. The age is calculated from the birth date each time, in the " +
            "practice's time zone, so it updates on the birthday. Returns not found while there is no data yet.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The baseline.", typeof(PatientBaselineResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The patient has no baseline yet.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetPatientBaseline(int patientId)
    {
        if (!await careRelationshipContextFacade.IsCareLinkActive(
                patientId, this.GetAuthenticatedUserId(), HttpContext.RequestAborted))
            return Forbid();

        var baseline = await queryService.Handle(
            new GetPatientBaselineByPatientIdQuery(patientId), HttpContext.RequestAborted);

        if (baseline is null)
            return NutritionalCareActionResultAssembler.ToNotFoundResult(
                NutritionalCareError.BaselineNotFound, localizer);

        return Ok(PatientBaselineResourceAssembler.ToResource(baseline, clinicalDate.Today()));
    }
}

using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST;

/// <summary>
///     NC-2. The guided consultation seen from the patient record: start it (PAC-1 "Iniciar consulta"), resume
///     it (PAC-1.C "Consulta en curso") and list past ones (PT25 "Anteriores", PAC-3).
/// </summary>
/// <remarks>Practitioner only: a consultation carries the diagnosis, which never reaches the patient.</remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Practitioner")]
[Tags("Consultations")]
[Produces("application/json")]
public class PatientConsultationsController(
    IConsultationCommandService commandService,
    IConsultationQueryService queryService,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpPost("{patientId:int}/consultations")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Start a consultation",
        Description =
            "Starts a consultation with the patient. It requires an active care link and the patient's basic data. " +
            "A patient can have only one consultation in progress: starting another one is refused with a conflict " +
            "that includes the identifier of the one in progress, so the app can offer to continue it. The request " +
            "body may be empty.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The consultation started on step 1.",
        typeof(ConsultationResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Not a practitioner, or no active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The patient already has a consultation in progress; the answer includes its identifier.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The patient has no baseline yet.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> StartConsultation(int patientId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartConsultationResource? resource)
    {
        var command = ConsultationCommandAssembler.ToCommand(patientId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);

        // The new consultation, or (409) the one already in progress, read for the response.
        var details = result.IsSuccess || result is Result<Consultation, NutritionalCareError>.Failure
            {
                Error: NutritionalCareError.ConsultationAlreadyInProgress
            }
            ? await queryService.Handle(new GetInProgressConsultationByPatientIdQuery(patientId),
                HttpContext.RequestAborted)
            : null;
        return NutritionalCareActionResultAssembler.ToStartConsultationResult(result, details, localizer);
    }

    [HttpGet("{patientId:int}/consultations/in-progress")]
    [SwaggerOperation(
        Summary = "Get the consultation in progress",
        Description =
            "Returns the consultation in progress, with its current step, when it was last saved and what was saved " +
            "in each step, so the app can resume it. Returns not found when there is none.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The consultation in progress.", typeof(ConsultationResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation in progress.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetConsultationInProgress(int patientId)
    {
        if (!await careRelationshipContextFacade.IsCareLinkActive(
                patientId, this.GetAuthenticatedUserId(), HttpContext.RequestAborted))
            return Forbid();

        var details = await queryService.Handle(new GetInProgressConsultationByPatientIdQuery(patientId),
            HttpContext.RequestAborted);

        if (details is null)
            return NutritionalCareActionResultAssembler.ToNotFoundResult(
                NutritionalCareError.ConsultationNotFound, localizer);

        return Ok(ConsultationResourceAssembler.ToResource(details));
    }

    [HttpGet("{patientId:int}/consultations")]
    [SwaggerOperation(
        Summary = "List the consultations of a patient",
        Description =
            "Lists the patient's consultations, newest first, with the plan version that was published and the " +
            "weight, body mass index and waist that were measured. They can be filtered by state: in progress, " +
            "completed or abandoned.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The consultations; empty when there are none.",
        typeof(IEnumerable<ConsultationSummaryResource>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The state filter is not a consultation state.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.")]
    public async Task<IActionResult> GetConsultations(int patientId, [FromQuery] string? state)
    {
        if (!ConsultationQueryAssembler.TryToQuery(patientId, state, out var query))
            return NutritionalCareActionResultAssembler.ToErrorResult(
                NutritionalCareError.InvalidConsultationState, localizer);

        if (!await careRelationshipContextFacade.IsCareLinkActive(
                patientId, this.GetAuthenticatedUserId(), HttpContext.RequestAborted))
            return Forbid();

        var consultations = await queryService.Handle(query, HttpContext.RequestAborted);
        return Ok(consultations.Select(ConsultationResourceAssembler.ToResource));
    }
}

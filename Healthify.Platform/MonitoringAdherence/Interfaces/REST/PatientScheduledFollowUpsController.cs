using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST;

/// <summary>
///     MA-3. The visits of a patient, as the patient reads them (PT3, PT20, PT25).
/// </summary>
/// <remarks>
///     Read only: "agendar o mover la consulta lo hace el nutricionista". The patient reads their own, and the
///     practitioner with an active care link reads the same thing (PAC-1). Not in
///     <see cref="ScheduledFollowUpsController" /> because that one is for the practitioner alone.
/// </remarks>
[ApiController]
[Route("api/v1/patients/{patientId:int}/scheduled-follow-ups")]
[Authorize(Roles = "Patient,Practitioner")]
[Tags("Monitoring and Adherence")]
[Produces("application/json")]
public class PatientScheduledFollowUpsController(
    IScheduledFollowUpQueryService queryService,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<MonitoringMessages> localizer) : ControllerBase
{
    [HttpGet("next")]
    [SwaggerOperation(
        Summary = "Get the next visit of a patient",
        Description =
            "Returns the patient's next scheduled visit, with its modality, how to prepare, when it was scheduled " +
            "and the name of the practitioner. No clinical notes and no diagnosis. Returns not found when there is " +
            "none. The patient can read their own; a practitioner needs an active care link.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The next visit.", typeof(PatientFollowUpResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "This patient cannot be read from this session.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "There is no visit on the calendar.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetNextFollowUp(int patientId)
    {
        if (!await MayRead(patientId)) return NotAllowed();

        var next = await queryService.Handle(new GetNextFollowUpByPatientIdQuery(patientId),
            HttpContext.RequestAborted);
        if (next is null)
            return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.ScheduledFollowUpNotFound,
                localizer);

        return Ok(PatientFollowUpResourceAssembler.ToResource(next));
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Get the visits of a patient",
        Description =
            "Lists the patient's visits, most recent first. They can be filtered by state (scheduled, completed, " +
            "missed or cancelled); without a filter every visit is returned. The patient can read their own; a " +
            "practitioner needs an active care link.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The visits, most recent first.",
        typeof(IEnumerable<PatientFollowUpResource>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The state filter is not a follow up state.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "This patient cannot be read from this session.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetFollowUps(int patientId, [FromQuery] string? state)
    {
        if (!await MayRead(patientId)) return NotAllowed();
        if (!PatientFollowUpsQueryAssembler.TryToQuery(patientId, state, out var query))
            return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.InvalidFollowUpState,
                localizer);

        var followUps = await queryService.Handle(query, HttpContext.RequestAborted);
        return Ok(followUps.Select(PatientFollowUpResourceAssembler.ToResource));
    }

    /// <summary>The patient reads their own; the practitioner, the patients they are linked to.</summary>
    private async Task<bool> MayRead(int patientId)
    {
        if (this.GetAuthenticatedUserId() == patientId) return true;
        return await careRelationshipContextFacade.IsCareLinkActive(patientId, this.GetAuthenticatedUserId(),
            HttpContext.RequestAborted);
    }

    private IActionResult NotAllowed()
    {
        return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.ActiveCareLinkRequired, localizer);
    }
}

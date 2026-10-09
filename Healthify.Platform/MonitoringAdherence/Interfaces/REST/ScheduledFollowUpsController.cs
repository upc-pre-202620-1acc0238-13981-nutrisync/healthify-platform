using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST;

/// <summary>
///     Subflow 5.10 of the Monitoring and Adherence bounded context, agenda side.
/// </summary>
/// <remarks>
///     Practitioner only. Schedule Follow Up is the second and last write endpoint of this bounded
///     context. Flag Missed Follow Up has no endpoint: nobody reports a visit that did not happen, so
///     only the clock can notice it, and noticing it closes nothing.
/// </remarks>
[ApiController]
[Route("api/v1/scheduled-follow-ups")]
[Authorize(Roles = "Practitioner")]
[Tags("Monitoring and Adherence")]
[Produces("application/json")]
public class ScheduledFollowUpsController(
    IScheduledFollowUpCommandService commandService,
    IScheduledFollowUpQueryService queryService,
    IStringLocalizer<MonitoringMessages> localizer) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Schedule a follow up",
        Description =
            "The practitioner schedules the patient's next visit; a patient has one scheduled visit at a time. The " +
            "practitioner is taken from the signed-in session, and an active care link is required. Optionally it " +
            "says how to prepare (fasting, light clothing, bringing blood tests, empty bladder), which the patient " +
            "sees in the app, and whether it is in person (the default) or remote. The time must be in the future. " +
            "The visit is marked as completed when a consultation is published for it, and a missed visit does not " +
            "end the care link.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The visit was scheduled.",
        typeof(ScheduledFollowUpResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The time is not in the future, a preparation instruction is not in the list, or the modality is not in " +
        "person or remote.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "There is no active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "This patient already has a visit on the calendar.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> ScheduleFollowUp([FromBody] ScheduleFollowUpResource resource)
    {
        var practitionerId = this.GetAuthenticatedUserId();

        var result = await commandService.Handle(
            ScheduleFollowUpCommandAssembler.ToCommand(practitionerId, resource),
            HttpContext.RequestAborted);

        return MonitoringActionResultAssembler.ToScheduledFollowUpResult(result, localizer,
            StatusCodes.Status201Created);
    }

    [HttpPost("{followUpId:int}/cancellation")]
    [SwaggerOperation(
        Summary = "Cancel a visit",
        Description =
            "The practitioner cancels a scheduled visit on their agenda, optionally with a short reason (up to 30 " +
            "characters). Cancelling is not a judgement on the patient and does not end the care link. Patients do " +
            "not cancel visits; the app asks them to tell their practitioner.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The visit was cancelled.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The reason is longer than 30 characters.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only a practitioner cancels a visit.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The visit does not exist or is not on this agenda.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The visit is no longer scheduled.", typeof(ProblemDetails))]
    public async Task<IActionResult> CancelFollowUp(int followUpId, [FromBody] CancelFollowUpResource? resource)
    {
        var result = await commandService.Handle(
            FollowUpChangeCommandAssembler.ToCommand(followUpId, this.GetAuthenticatedUserId(), resource),
            HttpContext.RequestAborted);
        return result is Result<ScheduledFollowUp, MonitoringError>.Success
            ? NoContent()
            : MonitoringActionResultAssembler.ToScheduledFollowUpResult(result, localizer);
    }

    [HttpPost("{followUpId:int}/rescheduling")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Reschedule a visit",
        Description =
            "The practitioner moves a scheduled visit on their agenda to another time in the future and, " +
            "optionally, changes how to prepare (if it is not sent, it is kept). What the patient said before the " +
            "visit is kept.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The visit, moved.", typeof(ScheduledFollowUpResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The moment is not in the future, or a preparation instruction is not in the list.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only a practitioner reschedules a visit.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The visit does not exist or is not on this agenda.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The visit is no longer scheduled.", typeof(ProblemDetails))]
    public async Task<IActionResult> RescheduleFollowUp(int followUpId,
        [FromBody] RescheduleFollowUpResource resource)
    {
        var result = await commandService.Handle(
            FollowUpChangeCommandAssembler.ToCommand(followUpId, this.GetAuthenticatedUserId(), resource),
            HttpContext.RequestAborted);
        return MonitoringActionResultAssembler.ToScheduledFollowUpResult(result, localizer);
    }

    [HttpGet("/api/v1/practitioners/{practitionerId:int}/scheduled-follow-ups")]
    [SwaggerOperation(
        Summary = "Get the agenda of a practitioner",
        Description =
            "Returns the practitioner's agenda. Missed visits stay with their state, because a visit someone could " +
            "not make is part of the history, not the end of it. Each visit shows the patient's name, how to " +
            "prepare, the modality, when it was scheduled and, when it applies, when it was completed or cancelled. " +
            "It can be filtered by state and by a starting moment.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The agenda, soonest first.",
        typeof(IEnumerable<ScheduledFollowUpResource>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The state filter is not a follow up state.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The agenda of another practitioner cannot be read.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetPractitionerAgenda(int practitionerId, [FromQuery] string? state,
        [FromQuery] DateTimeOffset? from)
    {
        if (this.GetAuthenticatedUserId() != practitionerId)
            return MonitoringActionResultAssembler.ToNotFoundResult(
                Domain.Model.Errors.MonitoringError.ActiveCareLinkRequired, localizer);

        if (!PractitionerAgendaQueryAssembler.TryToQuery(practitionerId, state, from, out var query))
            return MonitoringActionResultAssembler.ToNotFoundResult(
                Domain.Model.Errors.MonitoringError.InvalidFollowUpState, localizer);

        var agenda = await queryService.Handle(query, HttpContext.RequestAborted);

        return Ok(agenda.Select(ScheduledFollowUpResourceAssembler.ToResource));
    }
}

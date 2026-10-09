using System.Globalization;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
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
///     MA-4. The pre-visit check in (PT25.2, PT25.3, EV-2).
/// </summary>
/// <remarks>
///     Only the patient writes it; the patient and the practitioner with an active care link read it. It is
///     voluntary and raises no signal: nothing here reaches a window, a deviation or the consistency index.
/// </remarks>
[ApiController]
[Route("api/v1/scheduled-follow-ups/{followUpId:int}/check-in")]
[Authorize]
[Tags("Monitoring and Adherence")]
[Produces("application/json")]
public class FollowUpCheckInController(
    IPreVisitCheckInCommandService commandService,
    IPreVisitCheckInQueryService queryService,
    IScheduledFollowUpQueryService scheduledFollowUpQueryService,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<MonitoringMessages> localizer) : ControllerBase
{
    [HttpPut]
    [Authorize(Roles = "Patient")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Send or edit the check in of a visit",
        Description =
            "The patient tells their practitioner how things went before a visit, or edits what they already sent " +
            "(one answer per visit). How they felt (good, fair or hard) is required; the difficulties they had and " +
            "the questions they want to ask (up to 3 of their own and 3 suggested by the AI) are optional. Only the " +
            "patient of the visit can send it, while the visit is still scheduled and before its time. It is " +
            "voluntary and never raises an alert.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The check in, as stored.", typeof(PreVisitCheckInResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The feeling is missing, a difficulty is not in the list, a question is invalid or there are too many.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only a patient sends a check in.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The visit does not exist or is not this patient's.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The visit is no longer scheduled, or its hour has arrived and the check in is locked.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> SubmitCheckIn(int followUpId, [FromBody] SubmitPreVisitCheckInResource resource)
    {
        var result = await commandService.Handle(
            SubmitPreVisitCheckInCommandAssembler.ToCommand(followUpId, this.GetAuthenticatedUserId(), resource,
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName),
            HttpContext.RequestAborted);
        return MonitoringActionResultAssembler.ToPreVisitCheckInResult(result, localizer);
    }

    [HttpGet]
    [Authorize(Roles = "Patient,Practitioner")]
    [SwaggerOperation(
        Summary = "Get the check in of a visit",
        Description =
            "Returns what the patient said before the visit, or not found while they have not answered. The patient " +
            "of the visit can read it, and so can a practitioner with an active care link with that patient.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The check in.", typeof(PreVisitCheckInResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "There is no active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound,
        "The visit does not exist or is not this patient's, or there is no answer yet.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetCheckIn(int followUpId)
    {
        var followUp = await scheduledFollowUpQueryService.Handle(new GetScheduledFollowUpByIdQuery(followUpId),
            HttpContext.RequestAborted);
        var userId = this.GetAuthenticatedUserId();
        if (followUp is null || (User.IsInRole("Patient") && followUp.PatientId != userId))
            return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.ScheduledFollowUpNotFound,
                localizer);
        if (followUp.PatientId != userId &&
            !await careRelationshipContextFacade.IsCareLinkActive(followUp.PatientId, userId,
                HttpContext.RequestAborted))
            return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.ActiveCareLinkRequired,
                localizer);

        var checkIn = await queryService.Handle(new GetPreVisitCheckInByFollowUpIdQuery(followUpId),
            HttpContext.RequestAborted);
        if (checkIn is null)
            return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.PreVisitCheckInNotFound,
                localizer);

        return Ok(PreVisitCheckInResourceAssembler.ToResource(checkIn));
    }
}

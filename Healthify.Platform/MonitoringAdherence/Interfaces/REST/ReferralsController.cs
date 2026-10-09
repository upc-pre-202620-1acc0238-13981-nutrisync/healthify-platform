using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
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
///     Subflow 5.10 of the Monitoring and Adherence bounded context.
/// </summary>
/// <remarks>
///     Practitioner only, and one of only two write endpoints in the whole of this bounded context.
///     Everything else here is driven by a policy or by the clock, because interpretation is not
///     something a person requests.
/// </remarks>
[ApiController]
[Route("api/v1/referrals")]
[Authorize(Roles = "Practitioner")]
[Tags("Monitoring and Adherence")]
[Produces("application/json")]
public class ReferralsController(
    IReferralCommandService commandService,
    IStringLocalizer<MonitoringMessages> localizer) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Record a referral",
        Description =
            "The practitioner records that the patient is referred to another specialist. The speciality and the " +
            "reason are both required, so whoever receives the patient knows where they come from and why. The " +
            "practitioner is taken from the signed-in session, and an active care link is required.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The referral was recorded.",
        typeof(ReferralResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The speciality or the reason is missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "There is no active care link with this patient.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> RecordReferral([FromBody] RecordReferralResource resource)
    {
        var practitionerId = this.GetAuthenticatedUserId();

        var result = await commandService.Handle(
            RecordReferralCommandAssembler.ToCommand(practitionerId, resource),
            HttpContext.RequestAborted);

        return MonitoringActionResultAssembler.ToReferralResult(result, localizer,
            StatusCodes.Status201Created);
    }

    [HttpPost("{referralId:int}/closure")]
    [SwaggerOperation(
        Summary = "Close a referral",
        Description =
            "The practitioner who recorded a referral marks it as closed. A referral stays open until then. Closing " +
            "it changes nothing else.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The referral, now closed.", typeof(ReferralResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The referral does not exist or another practitioner recorded it.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The referral is already closed.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> CloseReferral(int referralId)
    {
        var result = await commandService.Handle(CloseReferralCommandAssembler.ToCommand(referralId, this.GetAuthenticatedUserId()),
            HttpContext.RequestAborted);
        return MonitoringActionResultAssembler.ToReferralResult(result, localizer);
    }
}

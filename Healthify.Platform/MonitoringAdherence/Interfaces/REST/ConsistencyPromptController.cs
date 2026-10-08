using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
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
///     MA-7 - The patient's acknowledgement of the consistency prompt (PT3 "Algo no cuadra", PT16).
/// </summary>
/// <remarks>
///     Business rules: Patient First Always and Patient Prompt Required Before Escalation (Subflows 5.7 and 5.8).
///     The alert counts as shown to the patient only when their app says it painted the card, never when the
///     server decided to prompt.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Patient")]
[Tags("Monitoring and Adherence")]
[Produces("application/json")]
public class ConsistencyPromptController(
    IConsistencyIndexCommandService commandService,
    IStringLocalizer<MonitoringMessages> localizer) : ControllerBase
{
    [HttpPost("{patientId:int}/consistency-index/prompt-acknowledgement")]
    [SwaggerOperation(
        Summary = "Acknowledge the consistency prompt",
        Description =
            "The app calls this once it has shown the patient the card saying that their weight and their logged " +
            "intake do not match. From then on the alert counts as shown to the patient, which must happen, plus a " +
            "few days of margin, before the practitioner is ever told. Calling it again keeps the first moment.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The acknowledgement was recorded (or already was).")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient acknowledges their own prompt.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "There is no prompt to acknowledge.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> AcknowledgePrompt(int patientId)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();

        var result = await commandService.Handle(new AcknowledgeConsistencyPromptCommand(patientId),
            HttpContext.RequestAborted);

        return result is Result<ConsistencyIndex, MonitoringError>.Failure failure
            ? MonitoringActionResultAssembler.ToNotFoundResult(failure.Error, localizer)
            : NoContent();
    }
}

using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST.Resources;
using Healthify.Platform.ReadModels.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.ReadModels.Interfaces.REST;

/// <summary>
///     What the practitioner reads about one patient between visits.
/// </summary>
/// <remarks>
///     This is the only path from the practitioner to the diary, and there is exactly one verb on
///     this controller. No command anywhere on this platform lets a practitioner write, correct,
///     validate or annotate a diary entry, a self weigh-in or a weight trend: professional validation
///     of estimates is out of scope by decision, and Intake is written by the patient and by nobody
///     else. The panel is a read, and the reading is qualitative.
///     Like the record, this controller belongs to no bounded context and reaches nothing but ACL
///     contracts.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Practitioner")]
[Tags("Read Models")]
[Produces("application/json")]
public class PatientMonitoringPanelController(
    PatientMonitoringPanelComposer composer,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/monitoring-panel")]
    [SwaggerOperation(
        Summary = "Get the monitoring panel of a patient",
        Description =
            "Returns the practitioner's view of how the patient is doing: the diary, the weights and the targets. " +
            "Each diary entry shows how it was logged and how confident the estimate is, because a portion " +
            "estimated from a photo and one the patient typed are different kinds of evidence. Home and clinic " +
            "weights are shown side by side and never mixed. The panel never shows the diagnosis, how the targets " +
            "were calculated or deviations on request: a sustained deviation reaches the practitioner once, in the " +
            "review inbox. Reading the panel changes nothing. The consistency field is deprecated and always empty: " +
            "that alert reaches the practitioner only through the review inbox, after the patient has seen it. The " +
            "panel also includes the current week, the days logged in the last 7 days, the weight trend of the last " +
            "4 weeks, the last clinic measurement and, for each diary entry, the food name, whether it was in the " +
            "plan and whether it counts towards the targets.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The panel.", typeof(PatientMonitoringPanelResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "There is no active care link with this patient.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetPatientMonitoringPanel(int patientId,
        [FromQuery] DateOnly? date, [FromQuery] int? days)
    {
        if (!await HasActiveCareLink(patientId)) return NotAllowed();

        var composition = await composer.Compose(patientId, date,
            days ?? PatientMonitoringPanelComposer.DefaultDays, HttpContext.RequestAborted);

        return Ok(PatientMonitoringPanelResourceAssembler.ToResource(composition));
    }

    /// <summary>
    ///     No access without consent. The question is answered by the context that owns it, and a
    ///     lookup that fails answers no.
    /// </summary>
    private async Task<bool> HasActiveCareLink(int patientId)
    {
        return await careRelationshipContextFacade.IsCareLinkActive(patientId,
            this.GetAuthenticatedUserId(), HttpContext.RequestAborted);
    }

    /// <summary>
    ///     Read models declare no error enum, so the refusal is built from the shared vocabulary.
    /// </summary>
    private IActionResult NotAllowed()
    {
        return ReadModelActionResultAssembler.ToForbiddenResult(localizer);
    }
}

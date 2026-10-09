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
///     RM-2. The summary the practitioner opens from the roster (PAC-1), composed across contexts.
/// </summary>
/// <remarks>
///     Practitioner only, and only with a live link to the patient: registering grants no access, so the link
///     is asked to the context that owns it. Like every composite endpoint it injects ACL facades and nothing
///     else.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Practitioner")]
[Tags("Read Models")]
[Produces("application/json")]
public class PatientSummaryController(
    PatientSummaryComposer composer,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/summary")]
    [SwaggerOperation(
        Summary = "Get the summary of a patient",
        Description =
            "Returns the summary of a patient for the practitioner: name, link date, active plan version, basic " +
            "data (empty when there is none yet), next visit, consultation in progress and how things went since " +
            "the last consultation (the home weight trend and the days within targets; before the first " +
            "consultation, the last 7 days and the last 4 weeks). No diagnosis, reasoning or calculation. If part " +
            "of the information cannot be loaded, that section is left empty.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The summary.", typeof(PatientSummaryResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "There is no active care link with this patient.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetPatientSummary(int patientId)
    {
        if (!await careRelationshipContextFacade.IsCareLinkActive(patientId, this.GetAuthenticatedUserId(),
                HttpContext.RequestAborted))
            return ReadModelActionResultAssembler.ToForbiddenResult(localizer);

        var composition = await composer.Compose(patientId, HttpContext.RequestAborted);

        return Ok(PatientSummaryResourceAssembler.ToResource(composition));
    }
}

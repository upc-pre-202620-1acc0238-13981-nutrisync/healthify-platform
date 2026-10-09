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
///     RM-1. The roster of a practitioner (PR1), composed across contexts.
/// </summary>
/// <remarks>
///     A practitioner reads only their own roster. It lists the links that are Active or waiting for the
///     patient's consent; the history of revoked or discharged links is not part of it. Like every composite
///     endpoint it injects ACL facades and nothing else, and reads them in batches: one call per facade for
///     the whole roster.
/// </remarks>
[ApiController]
[Route("api/v1/practitioners")]
[Authorize(Roles = "Practitioner")]
[Tags("Read Models")]
[Produces("application/json")]
public class PatientRosterController(
    PatientRosterComposer composer,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("{practitionerId:int}/patient-roster")]
    [SwaggerOperation(
        Summary = "Get the patient roster of a practitioner",
        Description =
            "Returns the practitioner's patient list: one row for each patient whose care link is active or waiting " +
            "for consent, oldest first. Each row shows the patient's name and initial, since when they are linked, " +
            "whether there is basic data, the plan version in force (empty means no plan yet), whether the patient " +
            "is new (no basic data or no plan yet), the next visit, a consultation in progress and an open alert.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The roster.", typeof(IEnumerable<PatientRosterItemResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The roster of another practitioner cannot be read.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetPatientRoster(int practitionerId)
    {
        if (this.GetAuthenticatedUserId() != practitionerId)
            return ReadModelActionResultAssembler.ToForbiddenResult(localizer);

        var roster = await composer.Compose(practitionerId, HttpContext.RequestAborted);

        return Ok(roster.Select(PatientRosterItemResourceAssembler.ToResource));
    }
}

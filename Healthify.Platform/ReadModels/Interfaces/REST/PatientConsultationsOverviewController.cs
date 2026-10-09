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
///     RM-5. PT25 "Mis consultas", composed across contexts.
/// </summary>
/// <remarks>
///     The patient reads their own, and the practitioner with an active care link reads the same view. Like every
///     composite endpoint it injects ACL facades and nothing else, and it returns only what the patient can see.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Patient,Practitioner")]
[Tags("Read Models")]
[Produces("application/json")]
public class PatientConsultationsOverviewController(
    PatientConsultationsComposer composer,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/consultations-overview")]
    [SwaggerOperation(
        Summary = "Get the consultations of a patient",
        Description =
            "Returns the patient's consultations screen: the next visit with its practitioner, modality and how to " +
            "prepare; what the patient said before that visit, if anything; and the consultations already held, " +
            "newest first, with their date, type and plan version. No diagnosis, calculation, weight or body mass " +
            "index. The suggested questions for the visit are requested separately. If part of the information " +
            "cannot be loaded, that section is left empty.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The consultations.", typeof(PatientConsultationsOverviewResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "This patient cannot be read from this session.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetConsultationsOverview(int patientId)
    {
        if (!await MayRead(patientId)) return ReadModelActionResultAssembler.ToForbiddenResult(localizer);

        var composition = await composer.Compose(patientId, HttpContext.RequestAborted);
        return Ok(PatientConsultationsOverviewResourceAssembler.ToResource(composition));
    }

    /// <summary>The patient reads their own; the practitioner, the patients they are linked to.</summary>
    private async Task<bool> MayRead(int patientId)
    {
        if (this.GetAuthenticatedUserId() == patientId) return true;

        return await careRelationshipContextFacade.IsCareLinkActive(patientId, this.GetAuthenticatedUserId(),
            HttpContext.RequestAborted);
    }
}

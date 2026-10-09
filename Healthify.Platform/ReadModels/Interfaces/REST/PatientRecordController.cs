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
///     The unified record of one patient, composed across contexts.
/// </summary>
/// <remarks>
///     This controller belongs to no bounded context. It injects five ACL facades and nothing else:
///     no repository, no query service, no DbContext and no error enum, because a composite read
///     model that could reach a repository would be a sixth context wearing a view as a disguise.
///     The problem this platform exists to solve is dispersion, so the record is one request rather
///     than five, and it is read by both roles: the patient reads their own, and the practitioner
///     reads the patients they hold a live link to.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize]
[Tags("Read Models")]
[Produces("application/json")]
public class PatientRecordController(
    PatientRecordComposer composer,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/record")]
    [SwaggerOperation(
        Summary = "Get the unified record of a patient",
        Description =
            "Returns the patient's record, organised by the stages of care: assessment, intervention and follow-up. " +
            "What it includes depends on who asks, so the diagnosis can never reach the patient by mistake. The " +
            "practitioner (with an active care link) also gets the current diagnosis, the past evaluations (weight, " +
            "body mass index and its category, waist), the clinic weight, the body mass index and how the patient " +
            "is meeting the targets. The patient (only their own record) also gets their practitioner, the next " +
            "visit, their numbers, their plan and their referrals with their status, and never a diagnosis, a " +
            "calculation, a body mass index or its category. If part of the information cannot be loaded, that " +
            "section is left empty instead of the whole record failing.")]
    [SwaggerResponse(StatusCodes.Status200OK,
        "The record: the practitioner's version for a practitioner, the patient's own version for the patient. " +
        "Both share the common sections.",
        typeof(PatientRecordResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "This patient cannot be read from this session.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetPatientRecord(int patientId, [FromQuery] int? days)
    {
        if (!await MayRead(patientId)) return NotAllowed();

        var window = days ?? PatientRecordComposer.DefaultDays;

        // RM-4: one resource per role. The patient's is built by a path that never asks for the diagnosis.
        if (this.GetAuthenticatedRole() == "Patient")
        {
            var (record, own) = await composer.ComposeForPatient(patientId, window, HttpContext.RequestAborted);
            return Ok(PatientOwnRecordResourceAssembler.ToResource(record, own));
        }

        var (composition, practitioner) = await composer.ComposeForPractitioner(patientId, window,
            HttpContext.RequestAborted);
        return Ok(PractitionerPatientRecordResourceAssembler.ToResource(composition, practitioner));
    }

    /// <summary>
    ///     The patient reads their own, and the practitioner reads the patients they are linked to.
    ///     Both questions are answered where they are owned: the identity in the token, and the care
    ///     link in the context that owns it.
    /// </summary>
    private async Task<bool> MayRead(int patientId)
    {
        if (this.GetAuthenticatedUserId() == patientId) return true;

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

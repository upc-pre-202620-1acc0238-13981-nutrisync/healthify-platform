using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;
using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.CareRelationship.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.CareRelationship.Interfaces.REST;

/// <summary>
///     Subflows 2.3, 2.4 and 2.5 of the Care Relationship bounded context.
/// </summary>
/// <remarks>
///     Revoke Care Link has no endpoint here on purpose: it is an internal policy triggered by
///     Consent Withdrawn, never a separate decision the practitioner or the patient makes.
/// </remarks>
[ApiController]
[Route("api/v1/care-links")]
[Authorize]
[Tags("Care Links")]
[Produces("application/json")]
public class CareLinksController(
    ICareLinkCommandService commandService,
    ICareLinkQueryService queryService,
    IStringLocalizer<CareRelationshipMessages> localizer) : ControllerBase
{
    [HttpGet("{careLinkId:int}")]
    [SwaggerOperation(
        Summary = "Get a care link",
        Description =
            "Returns a care link between a patient and a practitioner. Links that were revoked or ended with a " +
            "discharge are kept with their dates and can still be read, so the history of the relationship is never " +
            "lost. Only the patient and the practitioner of the link can read it.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The care link.", typeof(CareLinkResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the two participants may read the link.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No care link was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetCareLinkById(int careLinkId)
    {
        var careLink = await queryService.Handle(
            new GetCareLinkByIdQuery(careLinkId), HttpContext.RequestAborted);

        if (careLink is null)
            return CareRelationshipActionResultAssembler.ToNotFoundResult(
                CareRelationshipError.CareLinkNotFound, localizer);

        var userId = this.GetAuthenticatedUserId();
        if (careLink.PatientId != userId && careLink.PractitionerId != userId) return Forbid();

        return Ok(CareLinkResourceAssembler.ToResource(careLink));
    }

    [HttpGet("{careLinkId:int}/targets-read-status")]
    [SwaggerOperation(
        Summary = "Get the targets read status of a care link",
        Description =
            "Tells the practitioner whether the patient has already seen the latest targets published to them. It " +
            "only says whether they were seen, not whether the patient is following them.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The read status.", typeof(TargetsReadStatusResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the two participants may read it.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No care link was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetTargetsReadStatus(int careLinkId)
    {
        var careLink = await queryService.Handle(
            new GetCareLinkByIdQuery(careLinkId), HttpContext.RequestAborted);

        if (careLink is null)
            return CareRelationshipActionResultAssembler.ToNotFoundResult(
                CareRelationshipError.CareLinkNotFound, localizer);

        var userId = this.GetAuthenticatedUserId();
        if (careLink.PatientId != userId && careLink.PractitionerId != userId) return Forbid();

        return Ok(TargetsReadStatusResourceAssembler.ToResource(careLink));
    }

    [HttpPost("{careLinkId:int}/consent")]
    [Authorize(Roles = "Patient")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Grant consent",
        Description =
            "The patient gives consent to the care link, and that is what activates it: until then the practitioner " +
            "cannot see or write any of the patient's data. The scope of the consent is recorded with it. In the " +
            "same request the patient can also say whether they agree to their data being processed by AI; if they " +
            "say nothing, the answer is no.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Consent was granted.", typeof(CareLinkResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The consent scope is missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the linked patient may grant consent.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No care link was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "Consent was already granted, or the link is revoked or discharged.", typeof(ProblemDetails))]
    public async Task<IActionResult> GrantConsent(int careLinkId, [FromBody] GrantConsentResource resource)
    {
        var command = GrantConsentCommandAssembler.ToCommand(
            careLinkId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToCareLinkResult(result, localizer);
    }

    [HttpDelete("{careLinkId:int}/consent")]
    [Authorize(Roles = "Patient")]
    [SwaggerOperation(
        Summary = "Withdraw consent",
        Description =
            "The patient withdraws consent at any time, without having to give a reason. Doing so ends the care " +
            "link.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "Consent was withdrawn.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Only the linked patient may withdraw consent, and there must be live consent to withdraw.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No care link was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> WithdrawConsent(int careLinkId)
    {
        var command = WithdrawConsentCommandAssembler.ToCommand(careLinkId, this.GetAuthenticatedUserId());
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToWithdrawConsentResult(result, localizer);
    }

    [HttpPut("{careLinkId:int}/ai-processing-consent")]
    [Authorize(Roles = "Patient")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Turn AI processing on or off",
        Description =
            "The patient decides whether their data may be processed by AI, separately from the general consent to " +
            "the care link. Turning it on requires an active link; turning it off is always possible and changes " +
            "neither the plan nor the records. Sending the current state again changes nothing. Withdrawing the " +
            "whole consent also turns AI processing off.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The AI processing consent is in the requested state.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Only the linked patient may change it, and turning it on needs an active link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No care link was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The link is revoked or discharged.", typeof(ProblemDetails))]
    public async Task<IActionResult> ChangeAiProcessingConsent(int careLinkId,
        [FromBody] ChangeAiProcessingConsentResource resource)
    {
        var command = ChangeAiProcessingConsentCommandAssembler.ToCommand(
            careLinkId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToNoContentResult(result, localizer);
    }

    [HttpPost("{careLinkId:int}/targets-acknowledgement")]
    [Authorize(Roles = "Patient")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Acknowledge the active targets",
        Description =
            "The patient confirms they have seen the targets published to them. This only records that they were " +
            "seen: it does not change the plan in any way.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The targets were acknowledged.", typeof(CareLinkResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Only the linked patient may acknowledge, and the link must be active.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No care link was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "There is nothing pending, or the version is newer than the one that was published.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> AcknowledgeActiveTargets(int careLinkId,
        [FromBody] AcknowledgeActiveTargetsResource resource)
    {
        var command = AcknowledgeActiveTargetsCommandAssembler.ToCommand(
            careLinkId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToCareLinkResult(result, localizer);
    }

    [HttpPost("{careLinkId:int}/discharge")]
    [Authorize(Roles = "Practitioner")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Discharge the patient",
        Description =
            "The practitioner ends the treatment and records the clinical reason. A discharged link can never be " +
            "reactivated. Unlike a patient withdrawing consent, the practitioner must give a reason.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The treatment was discharged.", typeof(CareLinkResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The clinical reason is missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the linked practitioner may discharge.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No care link was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The link was already discharged.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> DischargePatient(int careLinkId,
        [FromBody] DischargePatientResource resource)
    {
        var command = DischargePatientCommandAssembler.ToCommand(
            careLinkId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToCareLinkResult(result, localizer);
    }
}

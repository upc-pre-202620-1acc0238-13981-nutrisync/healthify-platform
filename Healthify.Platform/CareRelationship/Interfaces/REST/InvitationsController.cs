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

/// <summary>Subflows 2.1 and 2.2 of the Care Relationship bounded context.</summary>
[ApiController]
[Route("api/v1/invitations")]
[Authorize]
[Tags("Invitations")]
[Produces("application/json")]
public class InvitationsController(
    IInvitationCommandService commandService,
    IInvitationQueryService queryService,
    IStringLocalizer<CareRelationshipMessages> localizer) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Practitioner")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Issue an invitation",
        Description =
            "Creates a single-use invitation that the practitioner shows as a QR code during the consultation. This " +
            "is the only way a patient joins a practitioner: patients can never create a care link on their own. " +
            "The invitation code is returned in this response only and is never shown again.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The invitation was issued.", typeof(InvitationResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The expiration date is missing or not in the future.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only a practitioner may issue an invitation.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> IssueInvitation([FromBody] IssueInvitationResource resource)
    {
        var command = IssueInvitationCommandAssembler.ToCommand(this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToIssueInvitationResult(result, localizer);
    }

    [HttpGet("{invitationId:int}")]
    [SwaggerOperation(
        Summary = "Get the status of an invitation",
        Description =
            "Tells whether an invitation is still pending, was used or expired. The invitation code itself is never " +
            "included: it is shown only once, when the invitation is created.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The invitation.", typeof(InvitationResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the issuing practitioner may read it.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No invitation was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetInvitationById(int invitationId)
    {
        var invitation = await queryService.Handle(
            new GetInvitationByIdQuery(invitationId), HttpContext.RequestAborted);

        if (invitation is null)
            return CareRelationshipActionResultAssembler.ToNotFoundResult(
                CareRelationshipError.InvitationNotFound, localizer);
        if (invitation.IssuedBy != this.GetAuthenticatedUserId()) return Forbid();

        return Ok(InvitationResourceAssembler.ToResource(invitation));
    }

    [HttpPost("redemption")]
    [Authorize(Roles = "Patient")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Redeem an invitation",
        Description =
            "The patient scans the QR code during the consultation, and the care link is created and returned. The " +
            "link stays inactive until the patient gives consent. If the patient already has a practitioner, the " +
            "request is refused with a conflict unless the app confirms that the patient wants to switch " +
            "(replaceActiveLink set to true); in that case the previous link ends at the same moment the new one is " +
            "created. The patient's diary and home weigh-ins are kept.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The care link was established.",
        typeof(CareLinkResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The token is malformed.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "A patient cannot redeem their own invitation.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No invitation matches that token.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The invitation was already redeemed, the patient already has a care link and did not " +
        "confirm replacing it, or the link to replace is with this same practitioner.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The invitation is no longer valid.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> RedeemInvitation([FromBody] RedeemInvitationResource resource)
    {
        var command = RedeemInvitationCommandAssembler.ToCommand(this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return CareRelationshipActionResultAssembler.ToRedeemInvitationResult(result, localizer);
    }
}

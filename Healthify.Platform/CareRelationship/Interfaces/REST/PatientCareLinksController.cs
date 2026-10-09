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

/// <summary>Read model Care Link Status of the Care Relationship bounded context.</summary>
[ApiController]
[Route("api/v1/patients")]
[Authorize]
[Tags("Care Links")]
[Produces("application/json")]
public class PatientCareLinksController(
    ICareLinkQueryService queryService,
    IStringLocalizer<CareRelationshipMessages> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/care-links/active")]
    [SwaggerOperation(
        Summary = "Get the active care link of a patient",
        Description =
            "Returns the care link that currently gives a practitioner access to this patient. A patient has at " +
            "most one such link at a time, and none until they give consent.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The active care link.", typeof(CareLinkResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient or their practitioner may read it.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "This patient has no active care link.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetActiveCareLink(int patientId)
    {
        var careLink = await queryService.Handle(
            new GetActiveCareLinkByPatientIdQuery(patientId), HttpContext.RequestAborted);

        if (careLink is null)
            return CareRelationshipActionResultAssembler.ToNotFoundResult(
                CareRelationshipError.CareLinkNotFound, localizer);

        var userId = this.GetAuthenticatedUserId();
        if (careLink.PatientId != userId && careLink.PractitionerId != userId) return Forbid();

        return Ok(CareLinkResourceAssembler.ToResource(careLink));
    }
}

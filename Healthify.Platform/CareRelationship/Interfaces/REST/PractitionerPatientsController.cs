using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;
using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Healthify.Platform.CareRelationship.Interfaces.REST;

/// <summary>Read model Practitioner Patient List of the Care Relationship bounded context.</summary>
[ApiController]
[Route("api/v1/practitioners")]
[Authorize(Roles = "Practitioner")]
[Tags("Care Links")]
[Produces("application/json")]
public class PractitionerPatientsController(ICareLinkQueryService queryService) : ControllerBase
{
    [HttpGet("{practitionerId:int}/patients")]
    [SwaggerOperation(
        Summary = "List the patients of a practitioner",
        Description =
            "Lists every patient the practitioner has been linked to, including links that were revoked or ended " +
            "with a discharge. Links are never deleted, so the list is also a history.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The care links, most recent first.",
        typeof(IEnumerable<CareLinkResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the practitioner may read their own roster.")]
    public async Task<IActionResult> GetPatientsByPractitionerId(int practitionerId)
    {
        if (practitionerId != this.GetAuthenticatedUserId()) return Forbid();

        var careLinks = await queryService.Handle(
            new GetCareLinksByPractitionerIdQuery(practitionerId), HttpContext.RequestAborted);

        return Ok(careLinks.Select(CareLinkResourceAssembler.ToResource));
    }
}

using Healthify.Platform.FoodCatalog.Application.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Queries;
using Healthify.Platform.FoodCatalog.Interfaces.REST.Resources;
using Healthify.Platform.FoodCatalog.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Healthify.Platform.FoodCatalog.Interfaces.REST;

/// <summary>
///     Subflow 6.2 of the Food Catalog bounded context, seen from the patient device.
/// </summary>
/// <remarks>
///     Read model Local Food Catalog. The device keeps its own copy of this list so that logging a
///     meal works with no connectivity at all, which is the rule Search Falls Back To Local Cache
///     Offline is about. This endpoint is how that copy is filled.
///     TODO: hotspot (event storming 6, hotspot 2) - local cache size. The endpoint currently returns
///     the whole local catalog rather than only the foods this patient has actually used. Assumed
///     interpretation: the conservative one, because a device that is missing a food cannot log the
///     meal at all, whereas a device carrying a few extra rows merely uses more storage. Narrowing it
///     needs a decision on what counts as "used" and how long a food stays in that set. Source:
///     event storming v3, section 6, hotspot 2.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Patient")]
[Tags("Food Catalog")]
[Produces("application/json")]
public class LocalFoodCatalogController(IReferenceFoodQueryService queryService) : ControllerBase
{
    private const int MaxEntries = 500;

    [HttpGet("{patientId:int}/local-food-catalog")]
    [SwaggerOperation(
        Summary = "Get the local food catalog of a patient",
        Description =
            "Returns the list of foods the patient's phone keeps so meals can be logged without a connection. Foods " +
            "added locally by the practice come first.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The local catalog.",
        typeof(IEnumerable<ReferenceFoodResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The catalog of another patient cannot be read.")]
    public async Task<IActionResult> GetLocalFoodCatalog(int patientId)
    {
        // The route carries the patient identifier for the shape of the read model; the identity
        // that is trusted is the one in the token.
        if (this.GetAuthenticatedUserId() != patientId) return Forbid();

        var entries = await queryService.Handle(new GetLocalFoodCatalogQuery(MaxEntries),
            HttpContext.RequestAborted);

        return Ok(entries.Select(ReferenceFoodResourceAssembler.ToResource));
    }
}

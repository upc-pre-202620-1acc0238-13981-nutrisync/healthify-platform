using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Application.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.FoodCatalog.Domain.Model.Queries;
using Healthify.Platform.FoodCatalog.Interfaces.REST.Resources;
using Healthify.Platform.FoodCatalog.Interfaces.REST.Transform;
using Healthify.Platform.FoodCatalog.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.FoodCatalog.Interfaces.REST;

/// <summary>
///     Subflows 6.1, 6.3 and 6.4 of the Food Catalog bounded context.
/// </summary>
/// <remarks>
///     Searching is open: a food and its nutrients per 100 grams are public reference data, they say
///     nothing about any patient, and requiring a session to look one up would only make the client
///     harder to build. Writing is not open. Only a practitioner may add a local override or ask for
///     an import.
///     Cache Food Locally has no endpoint. Entries reach the catalog through the policy of Subflow
///     6.2, which is what guarantees that everything stored has been through the translation.
/// </remarks>
[ApiController]
[Route("api/v1/reference-foods")]
[Tags("Food Catalog")]
[Produces("application/json")]
public class ReferenceFoodsController(
    IReferenceFoodCommandService commandService,
    IReferenceFoodQueryService queryService,
    IStringLocalizer<FoodCatalogMessages> localizer) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Search the food catalog",
        Description =
            "Searches foods by name. The platform's own catalog answers first, and external food databases are " +
            "consulted only when there are few results; if an external database is unavailable, the search still " +
            "works with what there is. Foods added locally by the practice come first.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The matching entries.",
        typeof(IEnumerable<ReferenceFoodResource>))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "The search did not complete.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> SearchReferenceFoods(
        [FromQuery(Name = "query")] string? query,
        [FromQuery] int max = 25)
    {
        var result = await commandService.Handle(new SearchFoodCommand(query ?? string.Empty, max),
            HttpContext.RequestAborted);
        return FoodCatalogActionResultAssembler.ToReferenceFoodListResult(result, localizer);
    }

    [HttpGet("{referenceFoodId:int}")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Get one catalog entry",
        Description =
            "Returns one food from the catalog with its nutrients. Internal details about where the food was " +
            "imported from are never included.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The catalog entry.", typeof(ReferenceFoodResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No catalog entry was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetReferenceFoodById(int referenceFoodId)
    {
        var referenceFood = await queryService.Handle(new GetReferenceFoodByIdQuery(referenceFoodId),
            HttpContext.RequestAborted);

        if (referenceFood is null)
            return FoodCatalogActionResultAssembler.ToNotFoundResult(
                FoodCatalogError.ReferenceFoodNotFound, localizer);

        return Ok(ReferenceFoodResourceAssembler.ToResource(referenceFood));
    }

    [HttpPost("local-overrides")]
    [Authorize(Roles = "Practitioner")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Create a local override",
        Description =
            "Adds a food that the external food databases do not have, which is the case for most Peruvian prepared " +
            "dishes. The food stays marked as added locally, so a later import can never overwrite it with a " +
            "generic record.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The local override was created.",
        typeof(ReferenceFoodResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The local name or the nutrients are missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only a practitioner can create an override.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "A local override with this name already exists.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> CreateLocalOverride([FromBody] CreateLocalOverrideResource resource)
    {
        var command = CreateLocalOverrideCommandAssembler.ToCommand(this.GetAuthenticatedUserId(),
            resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return FoodCatalogActionResultAssembler.ToReferenceFoodResult(result, localizer,
            StatusCodes.Status201Created);
    }

    [HttpPost("catalog-imports")]
    [Authorize(Roles = "Practitioner")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Request a catalog import",
        Description =
            "Asks the external food databases for part of their catalog right away; the same import also runs on " +
            "its own on a schedule. The answer is 'accepted' because the foods are saved in the background. Records " +
            "that cannot be expressed in the platform's terms are skipped and counted, not retried.")]
    [SwaggerResponse(StatusCodes.Status202Accepted, "The snapshot was translated and announced.",
        typeof(CatalogImportSummaryResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only a practitioner can request an import.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The term is empty.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, "No external provider could be reached.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> ImportCatalogSnapshot(
        [FromBody] ImportCatalogSnapshotResource resource)
    {
        var command = ImportCatalogSnapshotCommandAssembler.ToCommand(resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return FoodCatalogActionResultAssembler.ToCatalogImportResult(result, localizer);
    }
}

using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST;

/// <summary>
///     NC-8 - The plan versions the patient received (PT4, PT4.1), with "Qué cambió en esta versión".
/// </summary>
/// <remarks>
///     Patient only, and only their own. Separate from <c>GET /patients/{id}/nutrition-plans</c>, which is the
///     practitioner's history with the calculation basis: this one never carries the diagnosis, the basis or the
///     override reason.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Patient")]
[Tags("Nutritional Care")]
[Produces("application/json")]
public class PatientPlanVersionsController(INutritionPlanQueryService planQueryService) : ControllerBase
{
    [HttpGet("{patientId:int}/plan-versions")]
    [SwaggerOperation(
        Summary = "List my plan versions",
        Description =
            "Lists the patient's published plan versions, newest first, with their targets, guidelines, " +
            "restrictions and what changed from the previous version. Drafts are not included, and neither are the " +
            "diagnosis, the calculation or the reason a target was overridden.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The versions, most recent first (empty without a plan).",
        typeof(IEnumerable<PatientPlanVersionResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient reads their own versions.")]
    public async Task<IActionResult> GetMyPlanVersions(int patientId)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();

        var versions = await planQueryService.Handle(new GetPatientPlanVersionsQuery(patientId),
            HttpContext.RequestAborted);

        return Ok(versions.Select(PatientPlanVersionResourceAssembler.ToResource));
    }
}

using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Healthify.Platform.Shared.Interfaces.REST.RateLimiting;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST;

/// <summary>
///     IA-3. «Ideas con IA»: meal ideas that fit what is left of today's targets and respect the plan.
/// </summary>
/// <remarks>
///     Patient only, for their own day. What is left is computed here from the published targets and the diary;
///     the AI never receives the diagnosis nor the calculation basis. The ideas are suggestions: logging one goes
///     through <c>POST /diary-entries/manual-logs/batch</c> (IN-6).
/// </remarks>
[ApiController]
[Route("api/v1/patients/{patientId:int}/meal-ideas")]
[Authorize(Roles = "Patient")]
[Tags("Intake and Body Response")]
[Produces("application/json")]
public class PatientMealIdeasController(
    IMealIdeasCommandService mealIdeasCommandService,
    IStringLocalizer<IntakeMessages> localizer,
    IStringLocalizer<AiMessages> aiLocalizer) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.AiPhoto)]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Generate meal ideas for what is left today",
        Description =
            "Suggests meal ideas that fit what the patient has left for today and respect the restrictions of their " +
            "plan. The server calculates what is left from the published targets minus the meals confirmed that " +
            "day. The app can send the ideas already shown to get different ones. Each idea is checked against the " +
            "food catalog and its nutrients are recalculated from it; if fewer than two ideas are valid, the AI is " +
            "asked once more before giving up. Ideas are remembered for two hours. With 150 kcal or less left, no " +
            "ideas are suggested. Only the patient can ask; it requires their agreement to AI processing and the " +
            "function turned on, and there is a daily limit.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Two or three ideas.", typeof(MealIdeasResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The local date is missing or is not today.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the patient's own day, or no AI consent, or meal ideas turned off.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "There are no published targets yet.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "There is not enough left today for a meal.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "The daily quota of ideas is used up, or too many AI requests in a short time (Retry-After).",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status502BadGateway, "The AI did not produce two valid ideas.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, "The AI feature is off or unavailable.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GenerateMealIdeas(int patientId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GenerateMealIdeasResource? resource)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();

        var result = await mealIdeasCommandService.Handle(MealIdeasResourceAssembler.ToCommand(patientId, resource),
            HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToAiFunctionResult(result,
            view => MealIdeasResourceAssembler.ToResource(view, localizer["MealIdeasDisclaimer"].Value), localizer,
            aiLocalizer);
    }
}

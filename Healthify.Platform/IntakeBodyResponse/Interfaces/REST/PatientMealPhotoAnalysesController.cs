using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Healthify.Platform.Shared.Interfaces.REST.RateLimiting;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST;

/// <summary>
///     IN-7. «Viendo tu foto…»: the AI recognizes the dish of a photo on the server and proposes it, with its portion
///     and alternatives, for the patient to confirm on PT7.
/// </summary>
/// <remarks>
///     Patient only, for their own meals. The photo is read into memory, stripped of its metadata, sent to the AI
///     without any data of the patient and never stored. Logging the meal is a separate step
///     (<c>POST /diary-entries/photo-logs</c> with the <c>analysisId</c>): this endpoint writes nothing to the diary.
/// </remarks>
[ApiController]
[Route("api/v1/patients/{patientId:int}/meal-photo-analyses")]
[Authorize(Roles = "Patient")]
[Tags("Intake and Body Response")]
[Produces("application/json")]
public class PatientMealPhotoAnalysesController(
    IMealPhotoAnalysisCommandService commandService,
    IStringLocalizer<IntakeMessages> localizer,
    IStringLocalizer<AiMessages> aiLocalizer) : ControllerBase
{
    /// <summary>Above any configurable photo limit: the service applies the real one (2 MB by default).</summary>
    private const long MultipartLimitBytes = 16 * 1024 * 1024;

    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.AiPhoto)]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = MultipartLimitBytes)]
    [RequestSizeLimit(MultipartLimitBytes)]
    [SwaggerOperation(
        Summary = "Recognize the dish of a meal photo",
        Description =
            "Recognises the dish in a photo of a meal. The app sends one JPEG or WebP image of up to 2 MB. The " +
            "photo is cleaned of its metadata, sent to the AI without any information about the patient and never " +
            "stored. The dish is looked up in the food catalog and, if it is not there, it is added from the AI's " +
            "estimate. The answer includes the dish, the estimated grams, how confident the estimate is and up to " +
            "three alternatives. No meal is logged yet: the app logs it afterwards with the identifier of this " +
            "analysis, within 24 hours. Only the patient can ask; it requires their agreement to AI processing and " +
            "photo recognition turned on, and there is a daily limit of 30 photos. If no dish can be recognised, " +
            "the patient can log the meal by hand.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The analysis.", typeof(MealPhotoAnalysisResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The photo is missing or not a JPEG/WebP image.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the patient's own meal, or no AI consent, or meal photo recognition turned off.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status413PayloadTooLarge, "The photo exceeds the size limit.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The dish could not be recognized.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "The daily quota of photos is used up, or too many AI requests in a short time (Retry-After).",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, "The AI feature is off or unavailable.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> AnalyzeMealPhoto(int patientId, IFormFile? photo)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();

        // Read into memory only; the stream of the upload is the only other place the bytes ever are.
        byte[]? bytes = null;
        if (photo is { Length: > 0 })
        {
            using var buffer = new MemoryStream((int)Math.Min(photo.Length, MultipartLimitBytes));
            await photo.CopyToAsync(buffer, HttpContext.RequestAborted);
            bytes = buffer.ToArray();
        }

        var result = await commandService.Handle(new AnalyzeMealPhotoCommand(patientId, bytes),
            HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToAiFunctionResult(result, MealPhotoAnalysisResourceAssembler.ToResource,
            localizer, aiLocalizer, StatusCodes.Status201Created);
    }
}

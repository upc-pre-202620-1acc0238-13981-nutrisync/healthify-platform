using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Healthify.Platform.Shared.Interfaces.REST.RateLimiting;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST;

/// <summary>
///     IA-2, IA-4 and IA-5. The AI texts of Monitoring and Adherence: the weekly summary and the suggested questions
///     the patient reads, and the monitoring summary the practitioner reads.
/// </summary>
/// <remarks>
///     The figures of every text are computed without AI (MA-6, IN-5) and the output is rejected when its numbers
///     are not those, when it accuses or, for the patient, when it mentions a diagnosis. The patient functions never
///     receive the diagnosis nor the calculation basis.
/// </remarks>
[ApiController]
[Route("api/v1/patients/{patientId:int}")]
[Authorize]
[Tags("Monitoring and Adherence")]
[Produces("application/json")]
public class PatientAiSummariesController(
    IWeeklySummaryQueryService weeklySummaryQueryService,
    ISuggestedQuestionsCommandService suggestedQuestionsCommandService,
    IMonitoringSummaryCommandService monitoringSummaryCommandService,
    IAiSettings aiSettings,
    IAiConsentPolicy aiConsentPolicy,
    IStringLocalizer<MonitoringMessages> localizer,
    IStringLocalizer<AiMessages> aiLocalizer) : ControllerBase
{
    [HttpGet("weekly-summaries/latest")]
    [Authorize(Roles = "Patient")]
    [SwaggerOperation(
        Summary = "Get the latest weekly summary",
        Description =
            "Returns the patient's latest weekly summary: a headline, what went well and what to look at, written " +
            "by the AI every Monday from the previous week's diary, together with the figures behind it, which are " +
            "calculated without AI. Only the patient can read it. Returns not found while there is no summary yet " +
            "(fewer than 3 days logged, or not generated yet). It requires the patient's agreement to AI processing " +
            "and the function turned on.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The summary of the most recent week.", typeof(WeeklySummaryResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the patient's own summary, or the function is off for this patient.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "There is no summary yet.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, "The AI feature is off.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetLatestWeeklySummary(int patientId)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();
        if (!aiSettings.IsEnabled(AiFeature.WeeklySummary))
            return MonitoringActionResultAssembler.ToAiFailureResult(AiError.AiFeatureDisabled, aiLocalizer);
        if (!await aiConsentPolicy.IsAllowedAsync(patientId, AiFeature.WeeklySummary, HttpContext.RequestAborted))
            return MonitoringActionResultAssembler.ToAiFailureResult(AiError.AiConsentRequired, aiLocalizer);

        var summary = await weeklySummaryQueryService.Handle(new GetLatestWeeklySummaryByPatientIdQuery(patientId),
            HttpContext.RequestAborted);
        if (summary is null)
            return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.NotEnoughData, localizer);

        return Ok(WeeklySummaryResourceAssembler.ToResource(summary));
    }

    [HttpGet("suggested-questions")]
    [EnableRateLimiting(RateLimitingPolicies.AiPhoto)]
    [Authorize(Roles = "Patient")]
    [SwaggerOperation(
        Summary = "Get questions to bring to the visit",
        Description =
            "Suggests 3 to 5 questions, written in the first person, that the patient could bring to their next " +
            "visit. They are based on the diary since the last consultation (up to 28 days) and on what the patient " +
            "said before the visit, if anything; the diagnosis is never used. They are generated again when the " +
            "patient changes what they said before the visit. Only the patient can ask. Returns not found with " +
            "fewer than 3 days logged, or when the visit belongs to someone else.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The questions.", typeof(SuggestedQuestionsResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the patient's own, or the function is off for this patient.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not enough data yet, or the visit does not exist.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "The daily quota of the function is used up, or too many AI requests in a short time (Retry-After).",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status502BadGateway, "The AI output was rejected.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, "The AI feature is off or the provider failed.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetSuggestedQuestions(int patientId, [FromQuery] int? followUpId)
    {
        if (patientId != this.GetAuthenticatedUserId()) return Forbid();

        var result = await suggestedQuestionsCommandService.Handle(new SuggestQuestionsCommand(patientId, followUpId),
            HttpContext.RequestAborted);
        return MonitoringActionResultAssembler.ToAiFunctionResult(result,
            SuggestedQuestionsResourceAssembler.ToResource, localizer, aiLocalizer);
    }

    [HttpGet("monitoring-summary")]
    [EnableRateLimiting(RateLimitingPolicies.AiPhoto)]
    [Authorize(Roles = "Practitioner")]
    [SwaggerOperation(
        Summary = "Get the monitoring summary of a patient",
        Description =
            "Returns a short professional summary, written by the AI, of how the patient has been doing, together " +
            "with the facts behind it (days within targets, days short, the meal of the day logged most often...), " +
            "which are calculated without AI. The period is given with from and to (both or neither; by default the " +
            "7 days up to yesterday; at most 31 days). The summary never mentions a mismatch between weight and " +
            "intake unless that alert already reached the practitioner. Without the patient's agreement to AI " +
            "processing, or with the function off, only the facts are returned, with the reason the text is " +
            "missing. The result is remembered for 6 hours. Only a practitioner with an active care link can ask, " +
            "and the text should be reviewed before using it in a consultation.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The summary, or the facts alone.", typeof(MonitoringSummaryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The range is invalid.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "There is no active care link with this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "The daily quota of the practitioner is used up, or too many AI requests in a short time (Retry-After).",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status502BadGateway, "The AI output was rejected.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, "The AI provider failed.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetMonitoringSummary(int patientId, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await monitoringSummaryCommandService.Handle(
            new SummarizeMonitoringCommand(patientId, this.GetAuthenticatedUserId(), from, to),
            HttpContext.RequestAborted);
        return MonitoringActionResultAssembler.ToAiFunctionResult(result,
            MonitoringSummaryResourceAssembler.ToResource, localizer, aiLocalizer);
    }
}

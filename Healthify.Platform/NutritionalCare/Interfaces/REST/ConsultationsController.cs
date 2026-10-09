using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Healthify.Platform.Shared.Interfaces.REST.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST;

/// <summary>
///     NC-2. The four steps of the guided consultation (EV-2 to EV-5). Each step persists on its own, so
///     "Salir y continuar después" (EV-2.S) needs no endpoint: the consultation is resumed from PAC-1.C.
/// </summary>
/// <remarks>
///     Practitioner only, and only the practitioner leading the consultation saves its steps. The diagnosis of
///     step 2 stays pending, and the active diagnosis and plan version change together only when step 4
///     publishes (NC-7).
/// </remarks>
[ApiController]
[Route("api/v1/consultations")]
[Authorize(Roles = "Practitioner")]
[Tags("Consultations")]
[Produces("application/json")]
public class ConsultationsController(
    IConsultationCommandService commandService,
    IConsultationAiCommandService aiCommandService,
    IConsultationQueryService queryService,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpPut("{consultationId:int}/measurement")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Step 1: record the measurement",
        Description =
            "First step of the consultation: records the measurement. Height, age and sex come from the patient's " +
            "basic data. Repeating this step replaces the previous measurement, and the diagnosis of step 2 has to " +
            "be given again.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The step was saved.", typeof(ConsultationResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "An implausible measurement, an empty protocol checklist, or an invalid activity level, habit or lab value.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The consultation is no longer in progress.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The patient has no baseline.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> RecordMeasurement(int consultationId,
        [FromBody] RecordConsultationMeasurementResource resource)
    {
        var command = ConsultationCommandAssembler.ToCommand(consultationId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return await ConsultationResult(result, consultationId);
    }

    [HttpPost("{consultationId:int}/diagnosis-suggestion")]
    [EnableRateLimiting(RateLimitingPolicies.AiPhoto)]
    [SwaggerOperation(
        Summary = "Step 2: suggest a diagnosis",
        Description =
            "Suggests a nutritional diagnosis from a closed list, with its reasoning. The suggestion comes from the " +
            "AI when it is available and the patient agreed to AI processing. If the AI is off or fails, or its " +
            "answer strays too far from the body mass index, the suggestion is the category of the body mass index " +
            "measured in step 1, with a reasoning written from the measurement, so there is always a suggestion. " +
            "When the practitioner accepts a suggestion from the AI, the app sends back its identifier. This is " +
            "information for the practitioner only and never reaches the patient.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The suggestion.", typeof(DiagnosisSuggestionResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The measurement of step 1 is missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "Too many AI requests from this user in a short time; wait the Retry-After seconds.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> SuggestDiagnosis(int consultationId)
    {
        var result = await aiCommandService.Handle(
            new SuggestConsultationDiagnosisCommand(consultationId, this.GetAuthenticatedUserId()),
            HttpContext.RequestAborted);

        return result switch
        {
            Result<DiagnosisSuggestion, NutritionalCareError>.Success success => Ok(
                ConsultationResourceAssembler.ToResource(success.Value,
                    localizer["DiagnosisSuggestionDisclaimer"].Value)),
            Result<DiagnosisSuggestion, NutritionalCareError>.Failure failure =>
                NutritionalCareActionResultAssembler.ToErrorResult(failure.Error, localizer),
            _ => NutritionalCareActionResultAssembler.ToErrorResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    [HttpPost("{consultationId:int}/guideline-suggestions")]
    [EnableRateLimiting(RateLimitingPolicies.AiPhoto)]
    [SwaggerOperation(
        Summary = "Step 4: suggest guidelines for the diagnosis",
        Description =
            "Suggests which guidelines to pre-select for the diagnosis, always from the guideline catalog and never " +
            "as free text. They come from the AI when it is available and the patient agreed to AI processing; " +
            "otherwise, from a fixed table for each diagnosis. It requires the diagnosis of step 2. Nothing is " +
            "saved: the practitioner chooses when publishing.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The suggested codes.", typeof(GuidelineSuggestionsResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The diagnosis of step 2 is missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "Too many AI requests from this user in a short time; wait the Retry-After seconds.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> SuggestGuidelines(int consultationId)
    {
        var result = await aiCommandService.Handle(
            new SuggestConsultationGuidelinesCommand(consultationId, this.GetAuthenticatedUserId()),
            HttpContext.RequestAborted);

        return result switch
        {
            Result<GuidelineSuggestions, NutritionalCareError>.Success success => Ok(
                ConsultationResourceAssembler.ToResource(success.Value)),
            Result<GuidelineSuggestions, NutritionalCareError>.Failure failure =>
                NutritionalCareActionResultAssembler.ToErrorResult(failure.Error, localizer),
            _ => NutritionalCareActionResultAssembler.ToErrorResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    [HttpPut("{consultationId:int}/diagnosis")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Step 2: issue the diagnosis",
        Description =
            "Second step of the consultation: the practitioner sets the diagnosis, taken from the suggestion or " +
            "chosen from the closed list. It stays pending and replaces the current diagnosis only when the plan is " +
            "published in step 4. Repeating this step replaces the pending diagnosis.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The step was saved.", typeof(ConsultationResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "An unknown code or source, an accepted suggestion without its generation, or a rationale too long.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The consultation is no longer in progress.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The measurement of step 1 is missing.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> IssueDiagnosis(int consultationId,
        [FromBody] IssueConsultationDiagnosisResource resource)
    {
        var command = ConsultationCommandAssembler.ToCommand(consultationId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return await ConsultationResult(result, consultationId);
    }

    [HttpPost("{consultationId:int}/target-proposal")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Step 3: propose the targets",
        Description =
            "Third step of the consultation: calculates the proposed targets from the measurement of step 1. With " +
            "an empty request the usual parameters are used (Mifflin-St Jeor equation, 500 kcal deficit, 1.6 g of " +
            "protein per kg and 30 % of fat; no deficit when the weight is low or normal). To change parameters, " +
            "send only the ones that change. The same draft is recalculated until the targets are prescribed.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The proposal.", typeof(ConsultationTargetProposalResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "A changed parameter is invalid.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The consultation is no longer in progress, or the draft was already prescribed.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "Steps 1 and 2 come first.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> ProposeTargets(int consultationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ProposeConsultationTargetsResource? resource)
    {
        var command = ConsultationCommandAssembler.ToCommand(consultationId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToTargetProposalResult(result, localizer);
    }

    [HttpPut("{consultationId:int}/targets")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Step 3: prescribe the targets",
        Description =
            "Third step of the consultation: the practitioner accepts the proposed targets or writes their own " +
            "values. Writing their own values requires a reason.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The step was saved.", typeof(ConsultationResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Overriding without a reason.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation or draft was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The consultation is no longer in progress, or the draft was already prescribed.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "There is no proposal yet.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> PrescribeTargets(int consultationId,
        [FromBody] PrescribeConsultationTargetsResource resource)
    {
        var command = ConsultationCommandAssembler.ToCommand(consultationId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return await ConsultationResult(result, consultationId);
    }

    [HttpPut("{consultationId:int}/publication-draft")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Step 4: save the publication draft",
        Description =
            "Saves what the practitioner chose for the publication (restrictions, guidelines and the message for " +
            "the patient), so nothing is lost if the app closes. It is optional when the app keeps the draft " +
            "itself. The draft is returned when the step is opened again.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The draft was saved.", typeof(ConsultationResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "An unknown restriction or guideline code, a custom guideline out of length or beyond five, or a " +
        "message for the patient longer than 500 characters.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The consultation is no longer in progress.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The targets of step 3 come first.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> SavePublicationDraft(int consultationId,
        [FromBody] ConsultationPublicationResource resource)
    {
        var command = ConsultationCommandAssembler.ToDraftCommand(consultationId, this.GetAuthenticatedUserId(),
            resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return await ConsultationResult(result, consultationId);
    }

    [HttpPost("{consultationId:int}/publication")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Step 4: publish and close the consultation",
        Description =
            "Last step of the consultation: publishes the plan and closes the consultation. All at once, the new " +
            "diagnosis replaces the current one, the new plan version replaces the one in force with an automatic " +
            "change reason, and the consultation is closed; then the patient receives the new targets. Send an " +
            "Idempotency-Key header: retrying with the same key a publication that already succeeded returns the " +
            "same result without publishing again.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The plan was published and the consultation closed.",
        typeof(ConsultationResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "An invalid Idempotency-Key, an unknown restriction or guideline code, or a custom guideline out of " +
        "length or beyond five.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "Not the practitioner leading the consultation, or no active care link.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation or draft was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The consultation is no longer in progress (and the key does not repeat its publication).",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "A previous step is missing or stale, or the targets are not prescribed.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> Publish(int consultationId,
        [FromBody] ConsultationPublicationResource resource,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        var command = ConsultationCommandAssembler.ToPublishCommand(consultationId, this.GetAuthenticatedUserId(),
            resource, idempotencyKey);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        return await ConsultationResult(result, consultationId);
    }

    [HttpDelete("{consultationId:int}")]
    [SwaggerOperation(
        Summary = "Discard a consultation",
        Description =
            "Discards a consultation in progress. What was already recorded stays as history (the measurement); the " +
            "pending diagnosis and the unpublished plan are discarded. The current diagnosis and plan are not " +
            "touched.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The consultation was discarded.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Not the practitioner leading the consultation.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No consultation was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The consultation is no longer in progress.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> Discard(int consultationId)
    {
        var result = await commandService.Handle(
            new AbandonConsultationCommand(consultationId, this.GetAuthenticatedUserId()), HttpContext.RequestAborted);
        return NutritionalCareActionResultAssembler.ToNoContentResult(result, localizer);
    }

    /// <summary>After a saved step, the consultation as it now reads.</summary>
    private async Task<IActionResult> ConsultationResult<TOutcome>(Result<TOutcome, NutritionalCareError> result,
        int consultationId)
    {
        var details = result.IsSuccess
            ? await queryService.Handle(new GetConsultationByIdQuery(consultationId), HttpContext.RequestAborted)
            : null;
        return NutritionalCareActionResultAssembler.ToConsultationResult(result, details, localizer);
    }
}

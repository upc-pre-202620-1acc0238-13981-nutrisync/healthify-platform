using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;

/// <summary>The single place where an <see cref="IntakeError" /> becomes an HTTP status.</summary>
public static class IntakeActionResultAssembler
{
    public static IActionResult ToDiaryEntryResult(
        Result<DiaryEntry, IntakeError> result,
        IStringLocalizer<IntakeMessages> localizer,
        int successStatus = StatusCodes.Status200OK,
        string? foodName = null)
    {
        return result switch
        {
            Result<DiaryEntry, IntakeError>.Success s =>
                new ObjectResult(DiaryEntryResourceAssembler.ToResource(s.Value, foodName))
                    { StatusCode = successStatus },
            Result<DiaryEntry, IntakeError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IntakeError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToSelfWeighInResult(
        Result<SelfWeighIn, IntakeError> result,
        IStringLocalizer<IntakeMessages> localizer,
        int successStatus = StatusCodes.Status200OK,
        SelfWeighInProtocol? protocol = null)
    {
        return result switch
        {
            Result<SelfWeighIn, IntakeError>.Success s =>
                new ObjectResult(SelfWeighInResourceAssembler.ToResource(s.Value, protocol))
                    { StatusCode = successStatus },
            Result<SelfWeighIn, IntakeError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IntakeError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToSyncResult(
        Result<SyncOutcome, IntakeError> result,
        IStringLocalizer<IntakeMessages> localizer)
    {
        return result switch
        {
            Result<SyncOutcome, IntakeError>.Success s =>
                new OkObjectResult(SyncOutcomeResourceAssembler.ToResource(s.Value)),
            Result<SyncOutcome, IntakeError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IntakeError.UnexpectedError, localizer)
        };
    }

    /// <summary>IN-6. 201 with the entries of the meal.</summary>
    public static IActionResult ToMealGroupResult(Result<MealGroupLogOutcome, IntakeError> result,
        IReadOnlyDictionary<int, string> foodNames, IStringLocalizer<IntakeMessages> localizer)
    {
        return result switch
        {
            Result<MealGroupLogOutcome, IntakeError>.Success s =>
                new ObjectResult(LogMealGroupCommandAssembler.ToResource(s.Value, foodNames))
                    { StatusCode = StatusCodes.Status201Created },
            Result<MealGroupLogOutcome, IntakeError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IntakeError.UnexpectedError, localizer)
        };
    }

    /// <summary>IN-4. The batch always succeeds as a whole; each reading carries its own outcome.</summary>
    public static IActionResult ToSelfWeighInSyncResult(
        Result<SelfWeighInSyncOutcome, IntakeError> result,
        IStringLocalizer<IntakeMessages> localizer)
    {
        return result switch
        {
            Result<SelfWeighInSyncOutcome, IntakeError>.Success s =>
                new OkObjectResult(SelfWeighInSyncOutcomeResourceAssembler.ToResource(s.Value)),
            Result<SelfWeighInSyncOutcome, IntakeError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IntakeError.UnexpectedError, localizer)
        };
    }

    /// <summary>Response for the read endpoints and the ownership guards, which return no Result.</summary>
    public static IActionResult ToNotFoundResult(IntakeError error,
        IStringLocalizer<IntakeMessages> localizer)
    {
        return FailureResult(error, localizer);
    }

    private static ObjectResult FailureResult(IntakeError error,
        IStringLocalizer<IntakeMessages> localizer)
    {
        return (error switch
        {
            IntakeError.ActiveTargetsCacheNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["ActiveTargetsCacheNotFound"].Value),
            IntakeError.DiaryEntryNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["DiaryEntryNotFound"].Value),
            IntakeError.SelfWeighInNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["SelfWeighInNotFound"].Value),
            IntakeError.WeightTrendNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["WeightTrendNotFound"].Value),

            // This context is written by the patient and by nobody else.
            IntakeError.PatientWriteOnly => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["PatientWriteOnly"].Value),
            IntakeError.ActiveCareLinkRequired => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["ActiveCareLinkRequired"].Value),

            IntakeError.EstimateAlreadyConfirmed => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["EstimateAlreadyConfirmed"].Value),
            IntakeError.DuplicatedClientEntryId => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["DuplicatedClientEntryId"].Value),
            IntakeError.LocalTimestampCannotBeRewritten => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["LocalTimestampCannotBeRewritten"].Value),
            IntakeError.DiaryEntryCannotBeDeleted => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["DiaryEntryCannotBeDeleted"].Value),

            IntakeError.ProvenanceRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ProvenanceRequired"].Value),
            IntakeError.LocalTimestampRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["LocalTimestampRequired"].Value),
            IntakeError.ConfidenceRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ConfidenceRequired"].Value),
            IntakeError.ImplausibleWeightValue => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ImplausibleWeightValue"].Value),
            IntakeError.ProtocolComplianceRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ProtocolComplianceRequired"].Value),
            IntakeError.PublishedContractOnly => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["PublishedContractOnly"].Value),
            IntakeError.PlanAdherenceRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["PlanAdherenceRequired"].Value),
            IntakeError.InvalidPlanAdherence => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidPlanAdherence"].Value),
            IntakeError.InvalidEstimateConfirmation => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidEstimateConfirmation"].Value),
            IntakeError.ClientEntryIdRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ClientEntryIdRequired"].Value),
            IntakeError.InvalidLocalDate => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidLocalDate"].Value),
            IntakeError.InvalidMealGroupItems => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidMealGroupItems"].Value),
            IntakeError.InvalidEntryOrigin => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidEntryOrigin"].Value),
            IntakeError.PhotoRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["PhotoRequired"].Value),
            IntakeError.UnsupportedPhotoFormat => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnsupportedPhotoFormat"].Value),
            IntakeError.PhotoTooLarge => Problem(StatusCodes.Status413PayloadTooLarge,
                localizer["PayloadTooLargeTitle"].Value, localizer["PhotoTooLarge"].Value),
            IntakeError.MealPhotoAnalysisNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["MealPhotoAnalysisNotFound"].Value),

            // The request is well formed; what it asks for is not available or no longer allowed.
            IntakeError.RetroactiveLoggingWindowExceeded => Problem(
                StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value,
                localizer["RetroactiveLoggingWindowExceeded"].Value),
            IntakeError.ReferenceFoodNotResolved => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["ReferenceFoodNotResolved"].Value),
            IntakeError.EstimateNotProposed => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["EstimateNotProposed"].Value),
            // IA-3: not a failure of the patient; the text invites («Ya cubriste tu energía de hoy»).
            IntakeError.NotEnoughRemaining => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["NotEnoughRemaining"].Value),
            // IN-7: PT7.3, the app offers to log the meal by hand. Never the patient's fault.
            IntakeError.PhotoNotRecognized => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["PhotoNotRecognized"].Value),
            IntakeError.MealPhotoAnalysisExpired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["MealPhotoAnalysisExpired"].Value),

            _ => Problem(StatusCodes.Status500InternalServerError,
                localizer["UnexpectedServerError"].Value, localizer["UnexpectedError"].Value)
        }).WithErrorCode(error);
    }

    /// <summary>
    ///     IA-0. The shared technical AI errors of a generation run by this context (IA-3), with the texts of
    ///     <see cref="AiMessages" />.
    /// </summary>
    public static IActionResult ToAiFailureResult(AiError error, IStringLocalizer<AiMessages> localizer)
    {
        return (error switch
        {
            AiError.AiFeatureDisabled => Problem(StatusCodes.Status503ServiceUnavailable,
                localizer["ServiceUnavailableTitle"].Value, localizer["AiFeatureDisabled"].Value),
            AiError.AiConsentRequired => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["AiConsentRequired"].Value),
            AiError.AiRateLimited => Problem(StatusCodes.Status429TooManyRequests,
                localizer["TooManyRequestsTitle"].Value, localizer["AiRateLimited"].Value),
            AiError.AiProviderUnavailable => Problem(StatusCodes.Status503ServiceUnavailable,
                localizer["ServiceUnavailableTitle"].Value, localizer["AiProviderUnavailable"].Value),
            AiError.AiOutputRejected => Problem(StatusCodes.Status502BadGateway,
                localizer["BadGatewayTitle"].Value, localizer["AiOutputRejected"].Value),
            _ => Problem(StatusCodes.Status500InternalServerError,
                localizer["UnexpectedServerError"].Value, localizer["UnexpectedError"].Value)
        }).WithErrorCode(error);
    }

    /// <summary>
    ///     IA-3. An AI function of this context: its business errors with the texts of <see cref="IntakeMessages" />,
    ///     the shared technical ones with those of <see cref="AiMessages" />.
    /// </summary>
    public static IActionResult ToAiFunctionResult<T>(Result<T, IntakeAiFailure> result, Func<T, object> toResource,
        IStringLocalizer<IntakeMessages> localizer, IStringLocalizer<AiMessages> aiLocalizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<T, IntakeAiFailure>.Success s => new ObjectResult(toResource(s.Value)) { StatusCode = successStatus },
            Result<T, IntakeAiFailure>.Failure { Error.AiError: { } aiError } => ToAiFailureResult(aiError,
                aiLocalizer),
            Result<T, IntakeAiFailure>.Failure { Error.Error: { } error } => FailureResult(error, localizer),
            _ => FailureResult(IntakeError.UnexpectedError, localizer)
        };
    }

    private static ObjectResult Problem(int status, string title, string detail)
    {
        return new ObjectResult(ProblemDetailsFactory.Create(status, title, detail)) { StatusCode = status };
    }
}

using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;

/// <summary>The single place where a <see cref="MonitoringError" /> becomes an HTTP status.</summary>
public static class MonitoringActionResultAssembler
{
    public static IActionResult ToReferralResult(
        Result<Referral, MonitoringError> result,
        IStringLocalizer<MonitoringMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<Referral, MonitoringError>.Success s =>
                new ObjectResult(ReferralResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<Referral, MonitoringError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(MonitoringError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToScheduledFollowUpResult(
        Result<ScheduledFollowUp, MonitoringError> result,
        IStringLocalizer<MonitoringMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<ScheduledFollowUp, MonitoringError>.Success s =>
                new ObjectResult(ScheduledFollowUpResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<ScheduledFollowUp, MonitoringError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(MonitoringError.UnexpectedError, localizer)
        };
    }

    /// <summary>MA-4. The check in, or the error of the rule it broke.</summary>
    public static IActionResult ToPreVisitCheckInResult(
        Result<PreVisitCheckInView, MonitoringError> result,
        IStringLocalizer<MonitoringMessages> localizer)
    {
        return result switch
        {
            Result<PreVisitCheckInView, MonitoringError>.Success s =>
                new OkObjectResult(PreVisitCheckInResourceAssembler.ToResource(s.Value)),
            Result<PreVisitCheckInView, MonitoringError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(MonitoringError.UnexpectedError, localizer)
        };
    }

    /// <summary>Response for the read endpoints and the ownership guards, which return no Result.</summary>
    public static IActionResult ToNotFoundResult(MonitoringError error,
        IStringLocalizer<MonitoringMessages> localizer)
    {
        return FailureResult(error, localizer);
    }

    private static ObjectResult FailureResult(MonitoringError error,
        IStringLocalizer<MonitoringMessages> localizer)
    {
        return (error switch
        {
            MonitoringError.EvaluationWindowNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["EvaluationWindowNotFound"].Value),
            MonitoringError.DeviationNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["DeviationNotFound"].Value),
            MonitoringError.ScheduledFollowUpNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["ScheduledFollowUpNotFound"].Value),

            MonitoringError.PreVisitCheckInNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["PreVisitCheckInNotFound"].Value),
            MonitoringError.ActiveCareLinkRequired => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["ActiveCareLinkRequired"].Value),

            MonitoringError.PatientAlreadyHasOpenWindow => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PatientAlreadyHasOpenWindow"].Value),
            MonitoringError.FollowUpNotScheduled => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["FollowUpNotScheduled"].Value),
            MonitoringError.PatientAlreadyHasActiveScheduledFollowUp => Problem(
                StatusCodes.Status409Conflict, localizer["ConflictTitle"].Value,
                localizer["PatientAlreadyHasActiveScheduledFollowUp"].Value),
            MonitoringError.CheckInLocked => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["CheckInLocked"].Value),
            MonitoringError.WindowClosed => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["WindowClosed"].Value),
            MonitoringError.ClosedWindowCannotBeReopened => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ClosedWindowCannotBeReopened"].Value),
            MonitoringError.DayAlreadyEvaluated => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["DayAlreadyEvaluated"].Value),

            MonitoringError.SpecialtyAndReasonRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["SpecialtyAndReasonRequired"].Value),
            MonitoringError.WindowShorterThanMinimum => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["WindowShorterThanMinimum"].Value),
            MonitoringError.UnknownPreparationInstruction => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownPreparationInstruction"].Value),
            MonitoringError.UnknownConsultationModality => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownConsultationModality"].Value),
            MonitoringError.InvalidFollowUpState => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidFollowUpState"].Value),

            MonitoringError.ScheduledForMustBeInFuture => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ScheduledForMustBeInFuture"].Value),
            MonitoringError.CancellationReasonTooLong => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["CancellationReasonTooLong"].Value),
            MonitoringError.InvalidComplianceRange => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidComplianceRange"].Value),
            MonitoringError.ReferralNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["ReferralNotFound"].Value),
            MonitoringError.ReferralAlreadyClosed => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ReferralAlreadyClosed"].Value),
            MonitoringError.ConsistencyPromptNotIssued => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ConsistencyPromptNotIssued"].Value),
            MonitoringError.PatientAcknowledgementTooRecent => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["PatientAcknowledgementTooRecent"].Value),
            MonitoringError.FeelingRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["FeelingRequired"].Value),
            MonitoringError.TooManyQuestions => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["TooManyQuestions"].Value),
            MonitoringError.UnknownCheckInDifficulty => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownCheckInDifficulty"].Value),
            MonitoringError.InvalidCheckInQuestion => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidCheckInQuestion"].Value),
            // The request is well formed; what it asks for cannot be interpreted yet.
            MonitoringError.TargetsSnapshotMissing => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["TargetsSnapshotMissing"].Value),
            MonitoringError.InsufficientWindowLength => Problem(
                StatusCodes.Status422UnprocessableEntity, localizer["UnprocessableEntityTitle"].Value,
                localizer["InsufficientWindowLength"].Value),
            MonitoringError.NoLoggedDays => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["NoLoggedDays"].Value),
            MonitoringError.BothSeriesRequired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["BothSeriesRequired"].Value),
            MonitoringError.ConsistencyThresholdNotConfigured => Problem(
                StatusCodes.Status422UnprocessableEntity, localizer["UnprocessableEntityTitle"].Value,
                localizer["ConsistencyThresholdNotConfigured"].Value),
            MonitoringError.PatientPromptRequiredBeforeEscalation => Problem(
                StatusCodes.Status422UnprocessableEntity, localizer["UnprocessableEntityTitle"].Value,
                localizer["PatientPromptRequiredBeforeEscalation"].Value),
            MonitoringError.NotEnoughData => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["NotEnoughData"].Value),
            MonitoringError.InvalidWeekStart => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidWeekStart"].Value),
            MonitoringError.ThreeWeeksInAlertRequired => Problem(
                StatusCodes.Status422UnprocessableEntity, localizer["UnprocessableEntityTitle"].Value,
                localizer["ThreeWeeksInAlertRequired"].Value),

            _ => Problem(StatusCodes.Status500InternalServerError,
                localizer["UnexpectedServerError"].Value, localizer["UnexpectedError"].Value)
        }).WithErrorCode(error);
    }

    /// <summary>
    ///     IA-0. The shared technical AI errors of a generation run by this context (IA-2, IA-4 and IA-5), with the texts of
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
    ///     IA-2/IA-4/IA-5. The answer of an AI function of this context: its resource, a business error of the
    ///     context (<see cref="MonitoringMessages" />) or a technical AI error (<see cref="AiMessages" />).
    /// </summary>
    public static IActionResult ToAiFunctionResult<T>(Result<T, MonitoringAiFailure> result,
        Func<T, object> toResource, IStringLocalizer<MonitoringMessages> localizer,
        IStringLocalizer<AiMessages> aiLocalizer)
    {
        return result switch
        {
            Result<T, MonitoringAiFailure>.Success s => new OkObjectResult(toResource(s.Value)),
            Result<T, MonitoringAiFailure>.Failure { Error.AiError: { } aiError } => ToAiFailureResult(aiError,
                aiLocalizer),
            Result<T, MonitoringAiFailure>.Failure { Error.Error: { } error } => FailureResult(error, localizer),
            _ => FailureResult(MonitoringError.UnexpectedError, localizer)
        };
    }

    private static ObjectResult Problem(int status, string title, string detail)
    {
        return new ObjectResult(ProblemDetailsFactory.Create(status, title, detail)) { StatusCode = status };
    }
}

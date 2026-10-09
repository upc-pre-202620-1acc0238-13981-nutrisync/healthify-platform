using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;

/// <summary>
///     The single place where a <see cref="NutritionalCareError" /> becomes an HTTP status.
/// </summary>
public static class NutritionalCareActionResultAssembler
{
    public static IActionResult ToAssessmentResult(
        Result<NutritionalAssessment, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<NutritionalAssessment, NutritionalCareError>.Success s =>
                new ObjectResult(NutritionalAssessmentResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<NutritionalAssessment, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToDiagnosisResult(
        Result<NutritionalDiagnosis, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<NutritionalDiagnosis, NutritionalCareError>.Success s =>
                new ObjectResult(NutritionalDiagnosisResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<NutritionalDiagnosis, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToPlanResult(
        Result<NutritionPlan, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<NutritionPlan, NutritionalCareError>.Success s =>
                new ObjectResult(NutritionPlanResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<NutritionPlan, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    /// <summary>
    ///     NC-10. 200 with <c>{ reviewItem, planVersion }</c>; the item with the patient's name when
    ///     <paramref name="entry" /> carries it (NC-11).
    /// </summary>
    public static IActionResult ToPlanProposalAcceptanceResult(
        Result<PlanProposalAcceptanceOutcome, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer,
        ReviewInboxEntry? entry = null)
    {
        return result switch
        {
            Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Success s =>
                new OkObjectResult(PlanAdjustmentProposalResourceAssembler.ToResource(s.Value, entry)),
            Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    /// <summary>
    ///     NC-11. The resolved item with the patient's name and the other inbox fields, as <c>GET /review-items</c>
    ///     shows it; without <paramref name="entry" />, the item alone.
    /// </summary>
    public static IActionResult ToReviewItemResult(
        Result<ReviewItem, NutritionalCareError> result,
        ReviewInboxEntry? entry,
        IStringLocalizer<NutritionalCareMessages> localizer)
    {
        return result switch
        {
            Result<ReviewItem, NutritionalCareError>.Success s => new OkObjectResult(entry is not null &&
                entry.ReviewItem.Id.Value == s.Value.Id.Value
                    ? ReviewItemResourceAssembler.ToResource(entry)
                    : ReviewItemResourceAssembler.ToResource(s.Value)),
            Result<ReviewItem, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToReviewItemResult(
        Result<ReviewItem, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<ReviewItem, NutritionalCareError>.Success s =>
                new ObjectResult(ReviewItemResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<ReviewItem, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToBaselineResult(
        Result<PatientBaseline, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer,
        DateOnly today,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<PatientBaseline, NutritionalCareError>.Success s =>
                new ObjectResult(PatientBaselineResourceAssembler.ToResource(s.Value, today))
                    { StatusCode = successStatus },
            Result<PatientBaseline, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    /// <summary>
    ///     NC-2. A step of the consultation: on success, the consultation as it now reads (<paramref name="details" />,
    ///     read again after the command).
    /// </summary>
    public static IActionResult ToConsultationResult<TOutcome>(
        Result<TOutcome, NutritionalCareError> result,
        ConsultationDetails? details,
        IStringLocalizer<NutritionalCareMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<TOutcome, NutritionalCareError>.Success when details is not null =>
                new ObjectResult(ConsultationResourceAssembler.ToResource(details)) { StatusCode = successStatus },
            Result<TOutcome, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    /// <summary>
    ///     NC-2. Start a consultation. The 409 of a second one carries the identifier of the consultation in
    ///     progress in <c>extensions.consultationId</c>, so the app offers "Continuar consulta".
    /// </summary>
    /// <param name="result">What the command produced.</param>
    /// <param name="details">The consultation in progress of the patient: the new one, or the one that blocks it.</param>
    /// <param name="localizer">The messages of this context.</param>
    public static IActionResult ToStartConsultationResult(
        Result<Consultation, NutritionalCareError> result,
        ConsultationDetails? details,
        IStringLocalizer<NutritionalCareMessages> localizer)
    {
        if (result is Result<Consultation, NutritionalCareError>.Failure
            {
                Error: NutritionalCareError.ConsultationAlreadyInProgress
            })
        {
            var conflict = FailureResult(NutritionalCareError.ConsultationAlreadyInProgress, localizer);
            if (details is not null)
                ((Microsoft.AspNetCore.Mvc.ProblemDetails)conflict.Value!).Extensions["consultationId"] =
                    details.Consultation.Id.Value;
            return conflict;
        }

        return ToConsultationResult(result, details, localizer, StatusCodes.Status201Created);
    }

    /// <summary>NC-5. The target proposal of step 3 (EV-4).</summary>
    public static IActionResult ToTargetProposalResult(
        Result<ConsultationTargetProposalOutcome, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer)
    {
        return result switch
        {
            Result<ConsultationTargetProposalOutcome, NutritionalCareError>.Success s =>
                new OkObjectResult(ConsultationResourceAssembler.ToResource(s.Value)),
            Result<ConsultationTargetProposalOutcome, NutritionalCareError>.Failure f =>
                FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    /// <summary>NC-2. A command with nothing to return (DELETE).</summary>
    public static IActionResult ToNoContentResult<TOutcome>(
        Result<TOutcome, NutritionalCareError> result,
        IStringLocalizer<NutritionalCareMessages> localizer)
    {
        return result switch
        {
            Result<TOutcome, NutritionalCareError>.Success => new NoContentResult(),
            Result<TOutcome, NutritionalCareError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(NutritionalCareError.UnexpectedError, localizer)
        };
    }

    /// <summary>NC-2. An error found by a read endpoint before or without a Result (bad filter, not yours).</summary>
    public static IActionResult ToErrorResult(NutritionalCareError error,
        IStringLocalizer<NutritionalCareMessages> localizer)
    {
        return FailureResult(error, localizer);
    }

    /// <summary>Not-found response for the read endpoints, which do not return a Result.</summary>
    public static IActionResult ToNotFoundResult(NutritionalCareError error,
        IStringLocalizer<NutritionalCareMessages> localizer)
    {
        return FailureResult(error, localizer);
    }

    private static ObjectResult FailureResult(NutritionalCareError error,
        IStringLocalizer<NutritionalCareMessages> localizer)
    {
        return (error switch
        {
            NutritionalCareError.AssessmentNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["AssessmentNotFound"].Value),
            NutritionalCareError.DiagnosisNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["DiagnosisNotFound"].Value),
            NutritionalCareError.PlanNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["PlanNotFound"].Value),
            NutritionalCareError.ReviewItemNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["ReviewItemNotFound"].Value),
            NutritionalCareError.BaselineNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["BaselineNotFound"].Value),
            NutritionalCareError.ConsultationNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["ConsultationNotFound"].Value),

            NutritionalCareError.PractitionerOnly => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["PractitionerOnly"].Value),
            NutritionalCareError.ActiveCareLinkRequired => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["ActiveCareLinkRequired"].Value),

            NutritionalCareError.AssessmentAlreadyClosed => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["AssessmentAlreadyClosed"].Value),
            NutritionalCareError.PatientAlreadyHasActiveDiagnosis => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PatientAlreadyHasActiveDiagnosis"].Value),
            NutritionalCareError.PatientAlreadyHasActivePlanVersion => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PatientAlreadyHasActivePlanVersion"].Value),
            NutritionalCareError.PlanVersionAlreadySuperseded => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PlanVersionAlreadySuperseded"].Value),
            NutritionalCareError.ReviewItemAlreadyOpenForSignalType => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ReviewItemAlreadyOpenForSignalType"].Value),
            NutritionalCareError.PlanNotInExpectedState => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PlanNotInExpectedState"].Value),
            NutritionalCareError.BaselineAlreadyRecorded => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["BaselineAlreadyRecorded"].Value),
            NutritionalCareError.ConsultationAlreadyInProgress => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ConsultationAlreadyInProgress"].Value),
            NutritionalCareError.ConsultationNotInProgress => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ConsultationNotInProgress"].Value),
            NutritionalCareError.ConsultationStepOutOfOrder => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["ConsultationStepOutOfOrder"].Value),
            NutritionalCareError.UnknownDiagnosisCode => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownDiagnosisCode"].Value),
            NutritionalCareError.InvalidDiagnosisSource => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidDiagnosisSource"].Value),
            NutritionalCareError.UnknownRestriction => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownRestriction"].Value),
            NutritionalCareError.UnknownGuideline => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownGuideline"].Value),
            NutritionalCareError.InvalidCustomGuideline => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidCustomGuideline"].Value),
            NutritionalCareError.TooManyCustomGuidelines => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["TooManyCustomGuidelines"].Value),
            NutritionalCareError.InvalidIdempotencyKey => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidIdempotencyKey"].Value),
            NutritionalCareError.InvalidConsultationState => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidConsultationState"].Value),
            NutritionalCareError.InvalidReviewItemState => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidReviewItemState"].Value),
            NutritionalCareError.InvalidPatientMessage => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidPatientMessage"].Value),
            NutritionalCareError.PlanProposalEditsRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["PlanProposalEditsRequired"].Value),
            NutritionalCareError.PlanProposalNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["PlanProposalNotFound"].Value),
            NutritionalCareError.PlanProposalAlreadyDecided => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PlanProposalAlreadyDecided"].Value),
            NutritionalCareError.ReviewItemNotSustainedDeviation => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ReviewItemNotSustainedDeviation"].Value),
            NutritionalCareError.PlanProposalOutOfSafetyBounds => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["PlanProposalOutOfSafetyBounds"].Value),

            NutritionalCareError.HabitsHistoryAndActivityRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["HabitsHistoryAndActivityRequired"].Value),
            NutritionalCareError.MeasurementProtocolRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["MeasurementProtocolRequired"].Value),
            NutritionalCareError.ClinicalRationaleRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ClinicalRationaleRequired"].Value),
            NutritionalCareError.OverrideReasonRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["OverrideReasonRequired"].Value),
            NutritionalCareError.ChangeReasonRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ChangeReasonRequired"].Value),
            NutritionalCareError.ResolutionOutcomeRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ResolutionOutcomeRequired"].Value),
            NutritionalCareError.UnsupportedEquation => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnsupportedEquation"].Value),
            NutritionalCareError.InvalidActivityFactor => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidActivityFactor"].Value),
            NutritionalCareError.InvalidDeficitStrategy => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidDeficitStrategy"].Value),
            NutritionalCareError.InvalidReferenceWeight => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidReferenceWeight"].Value),
            NutritionalCareError.IncompleteCalculationBasis => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["IncompleteCalculationBasis"].Value),
            NutritionalCareError.InvalidBirthDate => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidBirthDate"].Value),
            NutritionalCareError.InvalidHeight => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidHeight"].Value),
            NutritionalCareError.UnknownMedicalCondition => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownMedicalCondition"].Value),
            NutritionalCareError.InvalidBiologicalSex => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidBiologicalSex"].Value),
            NutritionalCareError.InvalidActivityLevel => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidActivityLevel"].Value),
            NutritionalCareError.ProtocolChecklistEmpty => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ProtocolChecklistEmpty"].Value),
            NutritionalCareError.UnknownProtocolCheck => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["UnknownProtocolCheck"].Value),
            NutritionalCareError.ImplausibleMeasurement => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ImplausibleMeasurement"].Value),
            NutritionalCareError.ImplausibleBiochemistry => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ImplausibleBiochemistry"].Value),
            NutritionalCareError.InvalidEatingHabits => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidEatingHabits"].Value),

            // Business rules that are not input validation: the request is well formed, the clinical
            // sequence is not.
            NutritionalCareError.ClosedAssessmentRequired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["ClosedAssessmentRequired"].Value),
            NutritionalCareError.ActiveDiagnosisRequired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["ActiveDiagnosisRequired"].Value),
            NutritionalCareError.PreviousProposalRequired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["PreviousProposalRequired"].Value),
            NutritionalCareError.PlanRequiresDiagnosis => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["PlanRequiresDiagnosis"].Value),
            NutritionalCareError.CalculationBasisRequired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["CalculationBasisRequired"].Value),
            NutritionalCareError.ClinicalMeasurementRequired => Problem(
                StatusCodes.Status422UnprocessableEntity, localizer["UnprocessableEntityTitle"].Value,
                localizer["ClinicalMeasurementRequired"].Value),
            NutritionalCareError.BaselineRequired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["BaselineRequired"].Value),

            _ => Problem(StatusCodes.Status500InternalServerError,
                localizer["UnexpectedServerError"].Value, localizer["UnexpectedError"].Value)
        }).WithErrorCode(error);
    }

    /// <summary>
    ///     IA-0. The shared technical AI errors of a generation run by this context (IA-6, IA-7 and IA-8), with the texts of
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

    private static ObjectResult Problem(int status, string title, string detail)
    {
        return new ObjectResult(ProblemDetailsFactory.Create(status, title, detail)) { StatusCode = status };
    }
}

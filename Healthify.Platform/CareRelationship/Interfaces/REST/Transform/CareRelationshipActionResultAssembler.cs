using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

/// <summary>
///     The single place where a <see cref="CareRelationshipError" /> becomes an HTTP status.
/// </summary>
public static class CareRelationshipActionResultAssembler
{
    /// <summary>Subflow 2.1 - Issue Invitation. The only response that carries the token.</summary>
    public static IActionResult ToIssueInvitationResult(
        Result<Invitation, CareRelationshipError> result,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return result switch
        {
            Result<Invitation, CareRelationshipError>.Success s =>
                new ObjectResult(InvitationResourceAssembler.ToResourceWithToken(s.Value))
                    { StatusCode = StatusCodes.Status201Created },
            Result<Invitation, CareRelationshipError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(CareRelationshipError.UnexpectedError, localizer)
        };
    }

    /// <summary>
    ///     Subflow 2.2 - Redeem Invitation. Answers with the care link the redemption policy created,
    ///     because the care link is what the patient actually gained.
    /// </summary>
    public static IActionResult ToRedeemInvitationResult(
        Result<InvitationRedemptionOutcome, CareRelationshipError> result,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return result switch
        {
            Result<InvitationRedemptionOutcome, CareRelationshipError>.Success { Value.CareLink: not null } s =>
                new ObjectResult(CareLinkResourceAssembler.ToResource(s.Value.CareLink!))
                    { StatusCode = StatusCodes.Status201Created },
            // Redeemed, but the policy did not produce a link. Both guards in the command service
            // make this practically unreachable; if it happens it is a server-side problem.
            Result<InvitationRedemptionOutcome, CareRelationshipError>.Success =>
                FailureResult(CareRelationshipError.UnexpectedError, localizer),
            Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure f =>
                FailureResult(f.Error, localizer),
            _ => FailureResult(CareRelationshipError.UnexpectedError, localizer)
        };
    }

    /// <summary>Subflows 2.3, 2.4 and 2.5 - responses that return the care link itself.</summary>
    public static IActionResult ToCareLinkResult(
        Result<CareLink, CareRelationshipError> result,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return result switch
        {
            Result<CareLink, CareRelationshipError>.Success s =>
                new ObjectResult(CareLinkResourceAssembler.ToResource(s.Value))
                    { StatusCode = StatusCodes.Status200OK },
            Result<CareLink, CareRelationshipError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(CareRelationshipError.UnexpectedError, localizer)
        };
    }

    /// <summary>Subflow 2.5 - Withdraw Consent. Success carries no body.</summary>
    public static IActionResult ToWithdrawConsentResult(
        Result<CareLink, CareRelationshipError> result,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return result switch
        {
            Result<CareLink, CareRelationshipError>.Success =>
                new StatusCodeResult(StatusCodes.Status204NoContent),
            Result<CareLink, CareRelationshipError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(CareRelationshipError.UnexpectedError, localizer)
        };
    }

    /// <summary>CR-2 - Change AI Processing Consent. Success carries no body.</summary>
    public static IActionResult ToNoContentResult(
        Result<CareLink, CareRelationshipError> result,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return ToWithdrawConsentResult(result, localizer);
    }

    /// <summary>IA-1 - Update AI Preferences. Answers with the preferences as they now are.</summary>
    public static IActionResult ToAiPreferencesResult(
        Result<AiPreferencesStatus, CareRelationshipError> result,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return result switch
        {
            Result<AiPreferencesStatus, CareRelationshipError>.Success s =>
                new ObjectResult(AiPreferencesResourceAssembler.ToResource(s.Value))
                    { StatusCode = StatusCodes.Status200OK },
            Result<AiPreferencesStatus, CareRelationshipError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(CareRelationshipError.UnexpectedError, localizer)
        };
    }

    /// <summary>Not-found response for the read endpoints, which do not return a Result.</summary>
    public static IActionResult ToNotFoundResult(CareRelationshipError error,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return FailureResult(error, localizer);
    }

    private static ObjectResult FailureResult(CareRelationshipError error,
        IStringLocalizer<CareRelationshipMessages> localizer)
    {
        return (error switch
        {
            CareRelationshipError.InvitationNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["InvitationNotFound"].Value),
            CareRelationshipError.CareLinkNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["CareLinkNotFound"].Value),

            CareRelationshipError.PractitionerOnly => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["PractitionerOnly"].Value),
            CareRelationshipError.PatientCannotSelfLink => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["PatientCannotSelfLink"].Value),
            CareRelationshipError.NoActiveConsent => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["NoActiveConsent"].Value),
            CareRelationshipError.CareLinkNotActive => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["CareLinkNotActive"].Value),

            CareRelationshipError.PatientAlreadyHasActiveLink => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PatientAlreadyHasActiveLink"].Value),
            CareRelationshipError.AlreadyLinkedToThisPractitioner => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["AlreadyLinkedToThisPractitioner"].Value),
            CareRelationshipError.InvitationAlreadyRedeemed => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["InvitationAlreadyRedeemed"].Value),
            CareRelationshipError.ConsentAlreadyGranted => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ConsentAlreadyGranted"].Value),
            CareRelationshipError.CareLinkAlreadyRevoked => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["CareLinkAlreadyRevoked"].Value),
            CareRelationshipError.DischargedLinkCannotBeReactivated => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["DischargedLinkCannotBeReactivated"].Value),
            CareRelationshipError.PendingVersionAlreadyExists => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["PendingVersionAlreadyExists"].Value),
            CareRelationshipError.RedeemedInvitationCannotExpire => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["RedeemedInvitationCannotExpire"].Value),
            // IA-1: a conflict with the state of the consent, which the app resolves by offering it first.
            CareRelationshipError.AiConsentRequiredToEnableFeature => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["AiConsentRequiredToEnableFeature"].Value),

            CareRelationshipError.ExpirationDateRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ExpirationDateRequired"].Value),
            CareRelationshipError.ConsentScopeRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ConsentScopeRequired"].Value),
            CareRelationshipError.ClinicalReasonRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ClinicalReasonRequired"].Value),
            CareRelationshipError.InvitationNotValid => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvitationNotValid"].Value),

            // Business rules that are not input validation.
            CareRelationshipError.InvitationExpired => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["InvitationExpired"].Value),
            CareRelationshipError.NoPendingTargetsVersion => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["NoPendingTargetsVersion"].Value),
            CareRelationshipError.AcknowledgedVersionNewerThanActive => Problem(
                StatusCodes.Status422UnprocessableEntity, localizer["UnprocessableEntityTitle"].Value,
                localizer["AcknowledgedVersionNewerThanActive"].Value),

            _ => Problem(StatusCodes.Status500InternalServerError,
                localizer["UnexpectedServerError"].Value, localizer["UnexpectedError"].Value)
        }).WithErrorCode(error);
    }

    /// <summary>
    ///     IA-0. The shared technical AI errors of a generation run by this context (CR-2 and IA-1), with the texts of
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

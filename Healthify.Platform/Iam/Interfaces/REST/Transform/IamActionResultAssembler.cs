using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

/// <summary>
///     The single place where an <see cref="IamError" /> becomes an HTTP status. Keeping the mapping
///     in one file per bounded context is what stops the same rule reporting two different codes.
/// </summary>
public static class IamActionResultAssembler
{
    /// <summary>Subflow 1.1 - Account Registration.</summary>
    public static IActionResult ToRegisterAccountResult(
        Result<User, IamError> result,
        IStringLocalizer<IamMessages> localizer)
    {
        return result switch
        {
            Result<User, IamError>.Success s =>
                new ObjectResult(UserResourceAssembler.ToResource(s.Value))
                    { StatusCode = StatusCodes.Status201Created },
            Result<User, IamError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IamError.UnexpectedError, localizer)
        };
    }

    /// <summary>Subflow 1.2 - Sign In and Role Claim.</summary>
    public static IActionResult ToSignInResult(
        Result<SignInOutcome, IamError> result,
        IStringLocalizer<IamMessages> localizer)
    {
        return result switch
        {
            Result<SignInOutcome, IamError>.Success s =>
                new ObjectResult(SignInResponseResourceAssembler.ToResource(s.Value))
                    { StatusCode = StatusCodes.Status200OK },
            Result<SignInOutcome, IamError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IamError.UnexpectedError, localizer)
        };
    }

    /// <summary>IAM-3 - Change Preferred Language. Success carries no body.</summary>
    public static IActionResult ToChangePreferredLanguageResult(
        Result<User, IamError> result,
        IStringLocalizer<IamMessages> localizer)
    {
        return result switch
        {
            Result<User, IamError>.Success => new StatusCodeResult(StatusCodes.Status204NoContent),
            Result<User, IamError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IamError.UnexpectedError, localizer)
        };
    }

    /// <summary>IAM-4 - Refresh Session.</summary>
    public static IActionResult ToTokenRefreshResult(
        Result<SignInOutcome, IamError> result,
        IStringLocalizer<IamMessages> localizer)
    {
        return result switch
        {
            Result<SignInOutcome, IamError>.Success s =>
                new OkObjectResult(TokenRefreshResponseResourceAssembler.ToResource(s.Value)),
            Result<SignInOutcome, IamError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IamError.UnexpectedError, localizer)
        };
    }

    /// <summary>Subflow 1.3 - Sign Out. Success carries no body.</summary>
    public static IActionResult ToSignOutResult(
        Result<UserSession, IamError> result,
        IStringLocalizer<IamMessages> localizer)
    {
        return result switch
        {
            Result<UserSession, IamError>.Success => new StatusCodeResult(StatusCodes.Status204NoContent),
            Result<UserSession, IamError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(IamError.UnexpectedError, localizer)
        };
    }

    /// <summary>
    ///     Not-found response for the read endpoints. Query services return null rather than a
    ///     <see cref="Result{TValue,TError}" />, so this keeps their 404 on the same mapping as every
    ///     other failure of the context.
    /// </summary>
    public static IActionResult ToNotFoundResult(IamError error, IStringLocalizer<IamMessages> localizer)
    {
        return FailureResult(error, localizer);
    }

    private static ObjectResult FailureResult(IamError error, IStringLocalizer<IamMessages> localizer)
    {
        return (error switch
        {
            IamError.UserNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["UserNotFound"].Value),
            IamError.SessionNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["SessionNotFound"].Value),

            IamError.InvalidCredentials => Problem(StatusCodes.Status401Unauthorized,
                localizer["UnauthorizedTitle"].Value, localizer["InvalidCredentials"].Value),
            IamError.AccountLocked => Problem(StatusCodes.Status401Unauthorized,
                localizer["UnauthorizedTitle"].Value, localizer["AccountLocked"].Value),
            IamError.RefreshTokenInvalid => Problem(StatusCodes.Status401Unauthorized,
                localizer["UnauthorizedTitle"].Value, localizer["RefreshTokenInvalid"].Value),

            IamError.EmailAlreadyTaken => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["EmailAlreadyTaken"].Value),
            IamError.SessionAlreadyTerminated => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["SessionAlreadyTerminated"].Value),
            IamError.ShellAlreadySelectedForSession => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["ShellAlreadySelectedForSession"].Value),

            IamError.InvalidEmail => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidEmail"].Value),
            IamError.WeakPassword => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["WeakPassword"].Value),
            IamError.RoleNotDeclared => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["RoleNotDeclared"].Value),
            IamError.InvalidRole => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidRole"].Value),
            IamError.NameRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["NameRequired"].Value),
            IamError.InvalidPreferredLanguage => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InvalidPreferredLanguage"].Value),

            // Business rules that are not input validation.
            IamError.RoleChangeRequiresReAuthentication => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value,
                localizer["RoleChangeRequiresReAuthentication"].Value),
            IamError.RoleImmutablePerSession => Problem(StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["RoleImmutablePerSession"].Value),

            _ => Problem(StatusCodes.Status500InternalServerError,
                localizer["UnexpectedServerError"].Value, localizer["UnexpectedError"].Value)
        }).WithErrorCode(error);
    }

    private static ObjectResult Problem(int status, string title, string detail)
    {
        return new ObjectResult(ProblemDetailsFactory.Create(status, title, detail)) { StatusCode = status };
    }
}

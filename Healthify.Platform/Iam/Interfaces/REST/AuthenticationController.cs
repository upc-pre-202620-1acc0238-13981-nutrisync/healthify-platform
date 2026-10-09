using Healthify.Platform.Iam.Application.CommandServices;
using Healthify.Platform.Iam.Interfaces.REST.Resources;
using Healthify.Platform.Iam.Interfaces.REST.Transform;
using Healthify.Platform.Iam.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Healthify.Platform.Shared.Interfaces.REST.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.Iam.Interfaces.REST;

/// <summary>Subflows 1.1, 1.2 and 1.3 of the Iam bounded context.</summary>
[ApiController]
[Route("api/v1/authentication")]
[Authorize]
[Tags("Authentication")]
[Produces("application/json")]
[Consumes("application/json")]
public class AuthenticationController(
    IUserCommandService userCommandService,
    IUserSessionCommandService sessionCommandService,
    IStringLocalizer<IamMessages> localizer) : ControllerBase
{
    [HttpPost("sign-up")]
    [EnableRateLimiting(RateLimitingPolicies.Auth)]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Register an account",
        Description =
            "Creates an account as a patient or as a practitioner. Registering does not give access to anything on " +
            "its own: a patient without a practitioner sees no targets and cannot log meals. Given names and family " +
            "names are required, and they are what other screens show for the account.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The account was created.", typeof(UserResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The names, the email, the password or the role is missing or invalid.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "That email already has an account.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "Too many requests from this network in a short time; wait the Retry-After seconds.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> SignUp([FromBody] SignUpResource resource)
    {
        var command = RegisterAccountCommandAssembler.ToCommand(resource);
        var result = await userCommandService.Handle(command, HttpContext.RequestAborted);
        return IamActionResultAssembler.ToRegisterAccountResult(result, localizer);
    }

    [HttpPost("sign-in")]
    [EnableRateLimiting(RateLimitingPolicies.Auth)]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Sign in and receive the role claim",
        Description =
            "Checks the email and password, opens a session and returns an access token that carries the role of " +
            "the account, which stays the same for the whole session. After five failed attempts the account is " +
            "locked for a while. The response also includes the preferred language of the user, a refresh token and " +
            "the moment the access token expires; older versions of the app that ignore them keep working.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The session was opened.", typeof(SignInResponseResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized,
        "The credentials are not valid, or the account is locked.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "Too many requests from this network in a short time; wait the Retry-After seconds.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> SignIn([FromBody] SignInResource resource)
    {
        var command = SignInCommandAssembler.ToCommand(resource);
        var result = await sessionCommandService.Handle(command, HttpContext.RequestAborted);
        return IamActionResultAssembler.ToSignInResult(result, localizer);
    }

    [HttpPost("token-refreshes")]
    [EnableRateLimiting(RateLimitingPolicies.Auth)]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Refresh the session token",
        Description =
            "Exchanges a refresh token for a new access token for the same session, together with a new refresh " +
            "token; the one sent stops working. If the app repeats the same request within a few seconds (for " +
            "example after losing the connection), it gets the same answer again. Sending a refresh token that was " +
            "already used ends the session for safety, and the user has to sign in again. Every refusal gets the " +
            "same 'unauthorized' answer.")]
    [SwaggerResponse(StatusCodes.Status200OK, "A new token and refresh token.",
        typeof(TokenRefreshResponseResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized,
        "The refresh token is unknown, expired, already used or its session ended.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status429TooManyRequests,
        "Too many requests from this network in a short time; wait the Retry-After seconds.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> RefreshToken([FromBody] TokenRefreshResource resource)
    {
        var command = RefreshSessionCommandAssembler.ToCommand(resource);
        var result = await sessionCommandService.Handle(command, HttpContext.RequestAborted);
        return IamActionResultAssembler.ToTokenRefreshResult(result, localizer);
    }

    [HttpPost("sign-out")]
    [SwaggerOperation(
        Summary = "Sign out",
        Description =
            "Ends the session the current token belongs to. From then on the token gives no access, and its refresh " +
            "token is deleted.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The session was terminated.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No open session was found for this token.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The session had already been terminated.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> SignOutSession()
    {
        var command = SignOutCommandAssembler.ToCommand(
            this.GetAuthenticatedSessionId(), this.GetAuthenticatedUserId());
        var result = await sessionCommandService.Handle(command, HttpContext.RequestAborted);
        return IamActionResultAssembler.ToSignOutResult(result, localizer);
    }
}

using Healthify.Platform.Iam.Application.CommandServices;
using Healthify.Platform.Iam.Application.QueryServices;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.Queries;
using Healthify.Platform.Iam.Interfaces.REST.Resources;
using Healthify.Platform.Iam.Interfaces.REST.Transform;
using Healthify.Platform.Iam.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.Iam.Interfaces.REST;

/// <summary>Read models Welcome Screen and Session Context of the Iam bounded context.</summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
[Tags("Users")]
[Produces("application/json")]
public class UsersController(
    IUserCommandService userCommandService,
    IUserQueryService userQueryService,
    IUserSessionQueryService sessionQueryService,
    IStringLocalizer<IamMessages> localizer) : ControllerBase
{
    [HttpGet("{userId:int}")]
    [SwaggerOperation(
        Summary = "Get an account",
        Description =
            "Returns the account of the signed-in user. Users can only read their own account.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The account.", typeof(UserResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the account owner may read it.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No account was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetUserById(int userId)
    {
        if (userId != this.GetAuthenticatedUserId()) return Forbid();

        var user = await userQueryService.Handle(new GetUserByIdQuery(userId), HttpContext.RequestAborted);
        if (user is null) return IamActionResultAssembler.ToNotFoundResult(IamError.UserNotFound, localizer);

        return Ok(UserResourceAssembler.ToResource(user));
    }

    [HttpPut("{userId:int}/preferred-language")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Change the preferred language of an account",
        Description =
            "Changes the language the account prefers (Spanish or English). Only the account owner can change it. " +
            "From the next sign-in it is used for the platform's messages whenever the app does not say which " +
            "language it wants. Only the interface is translated, never clinical data.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The language was saved.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The language is not es or en.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the account owner may change it.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No account was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> ChangePreferredLanguage(int userId,
        [FromBody] ChangePreferredLanguageResource resource)
    {
        if (userId != this.GetAuthenticatedUserId()) return Forbid();

        var result = await userCommandService.Handle(
            ChangePreferredLanguageCommandAssembler.ToCommand(userId, resource), HttpContext.RequestAborted);

        return IamActionResultAssembler.ToChangePreferredLanguageResult(result, localizer);
    }

    [HttpGet("{userId:int}/sessions")]
    [SwaggerOperation(
        Summary = "List the sessions of an account",
        Description =
            "Lists the sessions of an account, each with the role it was opened with.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The sessions, most recent first.",
        typeof(IEnumerable<UserSessionResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the account owner may read its sessions.")]
    public async Task<IActionResult> GetSessionsByUserId(int userId)
    {
        if (userId != this.GetAuthenticatedUserId()) return Forbid();

        var sessions = await sessionQueryService.Handle(
            new GetUserSessionsByUserIdQuery(userId), HttpContext.RequestAborted);

        return Ok(sessions.Select(UserSessionResourceAssembler.ToResource));
    }
}

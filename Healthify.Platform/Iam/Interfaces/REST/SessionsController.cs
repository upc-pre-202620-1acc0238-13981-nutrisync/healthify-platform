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

/// <summary>Read model App Shell of the Iam bounded context.</summary>
[ApiController]
[Route("api/v1/sessions")]
[Authorize]
[Tags("Sessions")]
[Produces("application/json")]
public class SessionsController(
    IUserSessionQueryService sessionQueryService,
    IStringLocalizer<IamMessages> localizer) : ControllerBase
{
    [HttpGet("{sessionId:int}/navigation-shell")]
    [SwaggerOperation(
        Summary = "Get the navigation shell of a session",
        Description =
            "Returns which navigation the app should show for this session (patient or practitioner), chosen from " +
            "the role of the account. A session that has ended reports no role and no navigation.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The navigation shell of the session.",
        typeof(NavigationShellResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the session owner may read it.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No session was found.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetNavigationShell(int sessionId)
    {
        var session = await sessionQueryService.Handle(
            new GetUserSessionByIdQuery(sessionId), HttpContext.RequestAborted);

        if (session is null)
            return IamActionResultAssembler.ToNotFoundResult(IamError.SessionNotFound, localizer);
        if (session.UserId != this.GetAuthenticatedUserId()) return Forbid();

        return Ok(NavigationShellResourceAssembler.ToResource(session));
    }
}

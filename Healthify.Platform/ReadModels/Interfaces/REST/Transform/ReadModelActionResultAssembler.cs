using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.ReadModels.Interfaces.REST.Transform;

/// <summary>
///     The single place where a refused composite read becomes an HTTP status.
/// </summary>
/// <remarks>
///     Every bounded context has one of these keyed on its own error enum. Read models have no error
///     enum and are not going to grow one, because a composite view has no failures of its own: it
///     either may be read or it may not, and every other unhappy path is a section that came back
///     empty. So there is exactly one refusal here, and its words come from the shared vocabulary.
///     It lives outside the controller for the ordinary reason: <c>ControllerBase</c> already has a
///     property called <c>ProblemDetailsFactory</c>, and calling the shared factory from inside a
///     controller binds to that property instead.
/// </remarks>
public static class ReadModelActionResultAssembler
{
    /// <summary>Response for the ownership guard of both composite endpoints.</summary>
    /// <param name="localizer">Shared vocabulary, in the language the request asked for.</param>
    public static IActionResult ToForbiddenResult(IStringLocalizer<SharedResource> localizer)
    {
        var problem = ProblemDetailsFactory.Create(StatusCodes.Status403Forbidden,
            localizer["Forbidden"].Value, localizer["AccessNotAllowed"].Value,
            code: ProblemDetailsErrorCodes.AccessNotAllowed);

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AspNetProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;

/// <summary>
///     X-3. Machine-readable error code of every problem response, in <c>extensions.code</c>. Additive only: status,
///     title, detail, type and every other member of the problem stay exactly as they were.
/// </summary>
/// <remarks>
///     The code is the name of the error enum value (<c>InvalidCredentials</c>, <c>AiConsentRequired</c>…), so a client
///     can tell apart two errors that share a status (<c>InvalidCredentials</c> and <c>AccountLocked</c> are both 401)
///     without parsing a localized text. Three codes do not come from an enum: <see cref="ValidationFailed" />,
///     <see cref="InternalError" /> and the ones of the framework responses (<see cref="AuthenticationRequired" />,
///     <see cref="AccessNotAllowed" />), which reuse the resource keys of their existing texts.
/// </remarks>
public static class ProblemDetailsErrorCodes
{
    /// <summary>Name of the extension member.</summary>
    public const string ExtensionKey = "code";

    /// <summary>Model binding or data annotation failure (<c>ValidationProblemDetails</c>, 400 with <c>errors</c>).</summary>
    public const string ValidationFailed = "ValidationFailed";

    /// <summary>Any 500: an unhandled exception or the <c>UnexpectedError</c> of a context. Never carries internals.</summary>
    public const string InternalError = "InternalError";

    /// <summary>The 401 challenge of the bearer handler (no token, or an expired or invalid one).</summary>
    public const string AuthenticationRequired = "AuthenticationRequired";

    /// <summary>The ownership refusal of the composite reads (ReadModels has no error enum).</summary>
    public const string AccessNotAllowed = "AccessNotAllowed";

    /// <summary>
    ///     D-RL. The request rate limiter refused the request (429, with <c>Retry-After</c>). Not the AI daily quota,
    ///     which keeps its own code (<c>AiRateLimited</c>).
    /// </summary>
    public const string TooManyRequests = "TooManyRequests";

    private const string UnexpectedErrorName = "UnexpectedError";

    /// <summary>
    ///     The code of a domain or technical error: the enum value name, except <c>UnexpectedError</c>, which every
    ///     context maps to 500 and is reported as <see cref="InternalError" />.
    /// </summary>
    /// <remarks>DECISIÓN X-3: a 500 has one code for every context, as an unhandled exception does.</remarks>
    public static string For<TError>(TError error) where TError : struct, Enum
    {
        var name = error.ToString();
        return name == UnexpectedErrorName ? InternalError : name;
    }

    /// <summary>Sets <c>extensions.code</c> on the problem carried by an assembler result and returns the same result.</summary>
    public static ObjectResult WithErrorCode<TError>(this ObjectResult result, TError error)
        where TError : struct, Enum
    {
        if (result.Value is AspNetProblemDetails problem) problem.Extensions[ExtensionKey] = For(error);
        return result;
    }

    /// <summary>
    ///     Fills <c>extensions.code</c> of the problems the framework writes (validation, exception handler, empty
    ///     status results) when nobody set it. Hooked in <c>ProblemDetailsOptions.CustomizeProblemDetails</c>.
    /// </summary>
    public static void ApplyDefault(AspNetProblemDetails problem)
    {
        if (problem.Extensions.ContainsKey(ExtensionKey)) return;

        string? code = problem switch
        {
            ValidationProblemDetails => ValidationFailed,
            { Status: >= StatusCodes.Status500InternalServerError } or { Status: null } => InternalError,
            _ => null
        };
        if (code is not null) problem.Extensions[ExtensionKey] = code;
    }
}

using Microsoft.AspNetCore.Http;
using AspNetProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;

/// <summary>Builds RFC 7807 <see cref="AspNetProblemDetails" /> payloads with a status-derived Type link.</summary>
public static class ProblemDetailsFactory
{
    /// <param name="status">HTTP status of the problem.</param>
    /// <param name="title">Localized short title.</param>
    /// <param name="detail">Localized explanation.</param>
    /// <param name="instance">Request path, when known.</param>
    /// <param name="code">X-3: machine-readable error code written to <c>extensions.code</c> (see <see cref="ProblemDetailsErrorCodes" />).</param>
    public static AspNetProblemDetails Create(int status, string title, string detail, string? instance = null,
        string? code = null)
    {
        var problem = new AspNetProblemDetails
        {
            Type = TypeForStatus(status),
            Title = title,
            Status = status,
            Detail = detail,
            Instance = instance
        };
        if (code is not null) problem.Extensions[ProblemDetailsErrorCodes.ExtensionKey] = code;
        return problem;
    }

    private static string TypeForStatus(int status)
    {
        return status switch
        {
            StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc7235#section-3.1",
            StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            StatusCodes.Status422UnprocessableEntity => "https://tools.ietf.org/html/rfc4918#section-11.2",
            StatusCodes.Status500InternalServerError => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            _ => "about:blank"
        };
    }
}

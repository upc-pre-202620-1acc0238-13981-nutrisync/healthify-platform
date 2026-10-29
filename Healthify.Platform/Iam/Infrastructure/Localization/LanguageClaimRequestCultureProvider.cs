using Healthify.Platform.Iam.Infrastructure.Tokens.JWT;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;

namespace Healthify.Platform.Iam.Infrastructure.Localization;

/// <summary>
///     Takes the request culture from the <c>lang</c> claim of the session token, and only when the
///     client sent no <c>Accept-Language</c>.
/// </summary>
/// <remarks>
///     The header always wins: a device that says which language it wants is answered in it, whatever the
///     account says. The claim is the fallback for clients that send nothing, so the language saved on the
///     account also applies to error messages. Without a header and without a session the platform default
///     applies.
///     It reads the authenticated principal, so the localization middleware runs after
///     <c>UseAuthentication</c> (see <c>Program.cs</c>).
/// </remarks>
public class LanguageClaimRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(httpContext.Request.Headers[HeaderNames.AcceptLanguage]))
            return NullProviderCultureResult;

        if (httpContext.User.Identity?.IsAuthenticated != true) return NullProviderCultureResult;

        var language = httpContext.User.FindFirst(JwtTokenService.LanguageClaimType)?.Value;
        return string.IsNullOrWhiteSpace(language)
            ? NullProviderCultureResult
            : Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(language));
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Healthify.Platform.Shared.Interfaces.REST.Extensions;

/// <summary>Claim readers used by every protected endpoint to enforce ownership and role.</summary>
public static class ControllerBaseExtensions
{
    /// <summary>Identifier of the authenticated user, taken from the session token.</summary>
    public static int GetAuthenticatedUserId(this ControllerBase controller)
    {
        var value = controller.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? controller.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return int.Parse(value!);
    }

    /// <summary>Role claim of the current session: Patient or Practitioner. Immutable per session.</summary>
    public static string GetAuthenticatedRole(this ControllerBase controller)
    {
        return controller.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    }

    /// <summary>
    ///     Identifier of the session the current token was issued for, or 0 when the claim is absent.
    /// </summary>
    /// <remarks>
    ///     Needed by endpoints that act on the caller own session without taking its identifier from
    ///     the route, such as sign-out. Reading it from the token rather than from the request body
    ///     removes the need for an ownership check that could be spoofed.
    /// </remarks>
    public static int GetAuthenticatedSessionId(this ControllerBase controller)
    {
        var value = controller.User.FindFirstValue("sessionId");
        return int.TryParse(value, out var sessionId) ? sessionId : 0;
    }
}

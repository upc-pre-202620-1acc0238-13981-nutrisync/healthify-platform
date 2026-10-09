using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class NavigationShellResourceAssembler
{
    /// <summary>
    ///     Exposes the role through ActiveRoleClaim, so a terminated session reports no role:
    ///     business rule Role Claim Discarded On Sign Out (Subflow 1.3).
    /// </summary>
    public static NavigationShellResource ToResource(UserSession session)
    {
        return new NavigationShellResource(
            session.Id.Value,
            session.ActiveRoleClaim?.Value,
            session.IsActive ? session.NavigationShell?.Value : null,
            session.IsActive);
    }
}

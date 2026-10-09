using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class UserSessionResourceAssembler
{
    public static UserSessionResource ToResource(UserSession session)
    {
        return new UserSessionResource(
            session.Id.Value,
            session.UserId,
            session.RoleClaim.Value,
            session.NavigationShell?.Value,
            session.StartedAt,
            session.TerminatedAt,
            session.IsActive);
    }
}

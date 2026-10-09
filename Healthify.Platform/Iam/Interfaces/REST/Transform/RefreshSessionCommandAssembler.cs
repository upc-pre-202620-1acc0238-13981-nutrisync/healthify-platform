using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class RefreshSessionCommandAssembler
{
    public static RefreshSessionCommand ToCommand(TokenRefreshResource resource)
    {
        return new RefreshSessionCommand(resource.RefreshToken);
    }
}

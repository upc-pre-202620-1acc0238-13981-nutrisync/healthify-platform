using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class ChangePreferredLanguageCommandAssembler
{
    /// <summary>The account always comes from the route, already checked against the token.</summary>
    public static ChangePreferredLanguageCommand ToCommand(int userId, ChangePreferredLanguageResource resource)
    {
        return new ChangePreferredLanguageCommand(userId, resource.Language);
    }
}

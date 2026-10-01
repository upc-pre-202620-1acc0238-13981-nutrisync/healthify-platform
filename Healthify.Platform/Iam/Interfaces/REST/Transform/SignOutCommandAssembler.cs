using Healthify.Platform.Iam.Domain.Model.Commands;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class SignOutCommandAssembler
{
    /// <summary>Both identifiers come from the token, so the caller cannot sign anyone else out.</summary>
    public static SignOutCommand ToCommand(int sessionId, int userId)
    {
        return new SignOutCommand(sessionId, userId);
    }
}

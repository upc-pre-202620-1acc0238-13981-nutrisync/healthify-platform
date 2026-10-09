using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class SignInCommandAssembler
{
    public static SignInCommand ToCommand(SignInResource resource)
    {
        return new SignInCommand(resource.Email, resource.Password);
    }
}

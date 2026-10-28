using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class PasswordResetCommandAssembler
{
    public static RequestPasswordResetCommand ToCommand(PasswordResetRequestResource resource)
    {
        return new RequestPasswordResetCommand(resource.Email);
    }

    public static ResetPasswordCommand ToCommand(PasswordResetResource resource)
    {
        return new ResetPasswordCommand(resource.Email, resource.Code, resource.NewPassword);
    }
}

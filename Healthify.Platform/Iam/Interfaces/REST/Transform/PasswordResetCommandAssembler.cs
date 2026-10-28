using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class PasswordResetCommandAssembler
{
    public static RequestPasswordResetCommand ToCommand(PasswordResetRequestResource resource)
    {
        return new RequestPasswordResetCommand(resource.Email);
    }

    public static VerifyPasswordResetCodeCommand ToCommand(PasswordResetVerificationResource resource)
    {
        return new VerifyPasswordResetCodeCommand(resource.Email, resource.Code);
    }

    public static ResetPasswordCommand ToCommand(PasswordResetResource resource)
    {
        return new ResetPasswordCommand(resource.ResetToken, resource.NewPassword);
    }

    public static PasswordResetVerificationResponseResource ToResource(PasswordResetVerification verification)
    {
        return new PasswordResetVerificationResponseResource(verification.ResetToken, verification.ExpiresAt);
    }
}

using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.Iam.Application.CommandServices;

public interface IPasswordResetCommandService
{
    /// <summary>
    ///     Request Password Reset. Always a success: an unknown email, a malformed one or a failure to send
    ///     look the same from outside.
    /// </summary>
    Task<Result<bool, IamError>> Handle(RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Verify Password Reset Code. A right code is exchanged for a reset token.</summary>
    Task<Result<PasswordResetVerification, IamError>> Handle(VerifyPasswordResetCodeCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reset Password, with the reset token.</summary>
    Task<Result<bool, IamError>> Handle(ResetPasswordCommand command,
        CancellationToken cancellationToken = default);
}

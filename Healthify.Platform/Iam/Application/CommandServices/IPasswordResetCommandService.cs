using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.Iam.Application.CommandServices;

public interface IPasswordResetCommandService
{
    /// <summary>
    ///     IAM-2 - Request Password Reset. Always a success: an unknown email, a malformed one or a failure to send
    ///     look the same from outside.
    /// </summary>
    Task<Result<bool, IamError>> Handle(RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>IAM-2 - Reset Password.</summary>
    Task<Result<bool, IamError>> Handle(ResetPasswordCommand command,
        CancellationToken cancellationToken = default);
}

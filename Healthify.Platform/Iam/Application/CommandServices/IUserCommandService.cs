using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.Iam.Application.CommandServices;

public interface IUserCommandService
{
    /// <summary>Subflow 1.1 - Account Registration.</summary>
    Task<Result<User, IamError>> Handle(RegisterAccountCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>IAM-3 - Change Preferred Language. Only the account owner reaches it.</summary>
    Task<Result<User, IamError>> Handle(ChangePreferredLanguageCommand command,
        CancellationToken cancellationToken = default);
}

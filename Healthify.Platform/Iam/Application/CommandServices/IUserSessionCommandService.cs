using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.Iam.Application.CommandServices;

public interface IUserSessionCommandService
{
    /// <summary>Subflow 1.2 - Sign In and Role Claim.</summary>
    Task<Result<SignInOutcome, IamError>> Handle(SignInCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 1.2 - Select Navigation Shell. Invoked by a policy, never by an endpoint.</summary>
    Task<Result<UserSession, IamError>> Handle(SelectNavigationShellCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 1.3 - Sign Out.</summary>
    Task<Result<UserSession, IamError>> Handle(SignOutCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>IAM-4 - Refresh Session. Rotates the refresh token; reusing a rotated one ends the session.</summary>
    Task<Result<SignInOutcome, IamError>> Handle(RefreshSessionCommand command,
        CancellationToken cancellationToken = default);
}

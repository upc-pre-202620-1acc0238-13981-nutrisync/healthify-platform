using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.Iam.Domain.Model.Events;

/// <summary>
///     IAM-4. A rotated refresh token was presented again, so somebody else holds a copy and the session was
///     terminated. Internal to Iam; published together with <see cref="SessionTerminated" />.
/// </summary>
/// <param name="SessionId">The session that was terminated.</param>
/// <param name="UserId">Whose session it was.</param>
public record RefreshTokenReuseDetected(int SessionId, int UserId) : DomainEventBase;

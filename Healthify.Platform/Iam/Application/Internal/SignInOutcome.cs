using Healthify.Platform.Iam.Domain.Model.Aggregates;

namespace Healthify.Platform.Iam.Application.Internal;

/// <summary>
/// Application DTO carrying what a successful sign-in produced. Read model: Session Context.
///     Not a domain type and not an HTTP resource.
/// </summary>
/// <remarks>
///     IAM-4 appends the refresh token (handed to the client once, never stored in clear) and the expiry of the
///     access token. Also the outcome of Refresh Session, which opens no new session.
/// </remarks>
public record SignInOutcome(
    User User,
    UserSession Session,
    string Token,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null);

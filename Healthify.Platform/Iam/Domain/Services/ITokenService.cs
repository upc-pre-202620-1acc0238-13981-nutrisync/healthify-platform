using Healthify.Platform.Iam.Domain.Model.Aggregates;

namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>
///     Issues the session token. The role claim travels inside it, which is why Iam integrates with
///     the other contexts as a Conformist and publishes no cross-boundary event.
/// </summary>
public interface ITokenService
{
    /// <summary>
    ///     Produces a signed token carrying the subject, the account email, the immutable role claim
    ///     and the session identifier.
    /// </summary>
    string GenerateToken(User user, UserSession session);

    /// <summary>
    ///     IAM-4. Same token as <see cref="GenerateToken(User, UserSession)" />, issued at <paramref name="issuedAt" />
    ///     with the identifier <paramref name="tokenId" />. With the same inputs it returns the same string, which is
    ///     what a retried refresh hands back.
    /// </summary>
    string GenerateToken(User user, UserSession session, DateTimeOffset issuedAt, string tokenId);

    /// <summary>IAM-4. When a token generated now stops being accepted (<c>TokenSettings:ExpiresInMinutes</c>).</summary>
    DateTimeOffset ExpiresAt(DateTimeOffset issuedAt);
}

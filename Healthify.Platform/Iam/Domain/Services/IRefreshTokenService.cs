namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>IAM-4. A refresh token as issued: the value for the client and what the server keeps of it.</summary>
/// <param name="Token">The opaque value handed to the client once. Never stored, never logged.</param>
/// <param name="Hash">SHA-256 (hex, 64 chars) of <paramref name="Token" />: the only thing persisted.</param>
/// <param name="ExpiresAt">When it stops being accepted.</param>
public record IssuedRefreshToken(string Token, string Hash, DateTimeOffset ExpiresAt);

/// <summary>
///     IAM-4. Issues and hashes refresh tokens. The lifetime is <c>TokenSettings:RefreshTokenDays</c> (30 days by
///     default).
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>A new random token, its hash and its expiry. Used when a session is opened.</summary>
    IssuedRefreshToken Issue();

    /// <summary>
    ///     IAM-4. The token <paramref name="presentedToken" /> rotates into, its hash and its expiry. Deterministic
    ///     (keyed with the server secret), so a retried refresh of the same token can hand back the same successor
    ///     without the server ever storing a token in clear.
    /// </summary>
    IssuedRefreshToken Successor(string presentedToken);

    /// <summary>IAM-4. <c>TokenSettings:RefreshReuseGraceSeconds</c> (30 by default).</summary>
    TimeSpan ReuseGrace();

    /// <summary>The hash a presented token is looked up by.</summary>
    string Hash(string token);

    /// <summary>The instant the expiry is compared against.</summary>
    DateTimeOffset Now();
}

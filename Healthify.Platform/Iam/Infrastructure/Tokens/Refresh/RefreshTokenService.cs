using System.Security.Cryptography;
using System.Text;
using Healthify.Platform.Iam.Domain.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace Healthify.Platform.Iam.Infrastructure.Tokens.Refresh;

/// <summary>
///     IAM-4. Refresh tokens are 32 bytes (base64url) and are stored as their SHA-256. A plain hash is enough
///     here, unlike passwords: the token is random and long, so there is nothing to brute force.
/// </summary>
/// <remarks>
///     The token issued at sign-in is random. Each rotation derives the next token as HMAC-SHA256 of the presented
///     one, keyed with <c>TokenSettings:Secret</c>: unpredictable without the secret, and reproducible from the
///     presented token, which is what makes a retried refresh idempotent.
///     Lifetime: <c>TokenSettings:RefreshTokenDays</c>, then <c>Jwt:RefreshTokenDays</c> (the key the MD names),
///     30 days by default. Grace: <c>TokenSettings:RefreshReuseGraceSeconds</c>, 30 by default.
/// </remarks>
public class RefreshTokenService(IConfiguration configuration, TimeProvider timeProvider) : IRefreshTokenService
{
    private const int DefaultRefreshTokenDays = 30;
    private const int DefaultReuseGraceSeconds = 30;
    private const int TokenBytes = 32;

    public IssuedRefreshToken Issue()
    {
        return Build(WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes)));
    }

    public IssuedRefreshToken Successor(string presentedToken)
    {
        var secret = configuration["TokenSettings:Secret"]
                     ?? throw new InvalidOperationException("TokenSettings:Secret is not configured.");
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("refresh-rotation:" + secret));
        return Build(WebEncoders.Base64UrlEncode(
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(presentedToken))));
    }

    public string Hash(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public DateTimeOffset Now()
    {
        return timeProvider.GetUtcNow();
    }

    public TimeSpan ReuseGrace()
    {
        var seconds = configuration.GetValue<int?>("TokenSettings:RefreshReuseGraceSeconds") ?? DefaultReuseGraceSeconds;
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    private IssuedRefreshToken Build(string token)
    {
        var days = configuration.GetValue<int?>("TokenSettings:RefreshTokenDays")
                   ?? configuration.GetValue<int?>("Jwt:RefreshTokenDays")
                   ?? DefaultRefreshTokenDays;

        return new IssuedRefreshToken(token, Hash(token), Now().AddDays(Math.Max(1, days)));
    }
}

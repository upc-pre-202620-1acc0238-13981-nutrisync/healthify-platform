using System.Security.Cryptography;
using System.Text;
using Healthify.Platform.Iam.Domain.Services;

namespace Healthify.Platform.Iam.Infrastructure.Security;

/// <summary>
///     IAM-2. Codes are six random digits, stored as HMAC-SHA256 of <c>{userId}:{code}</c> keyed with
///     <c>TokenSettings:Secret</c>.
/// </summary>
/// <remarks>
///     A plain SHA-256 would not do here, unlike refresh tokens: there are only a million codes, so a leaked table
///     could be reversed in a moment. Keyed with the server secret, the hash is useless without it; the user id in
///     the input keeps two accounts with the same digits from sharing a hash.
///     Lifetime: <c>Iam:PasswordResetCodeMinutes</c>, 15 by default. Resend cooldown:
///     <c>Iam:PasswordResetResendSeconds</c>, 60 by default.
/// </remarks>
public class PasswordResetCodeService(IConfiguration configuration, TimeProvider timeProvider)
    : IPasswordResetCodeService
{
    private const int DefaultCodeMinutes = 15;
    private const int DefaultResendSeconds = 60;
    private const int CodeDigits = 6;

    public IssuedPasswordResetCode Issue(int userId)
    {
        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, CodeDigits)).ToString($"D{CodeDigits}");
        var minutes = configuration.GetValue<int?>("Iam:PasswordResetCodeMinutes") ?? DefaultCodeMinutes;
        var now = Now();
        return new IssuedPasswordResetCode(code, Hash(userId, code), now, now.AddMinutes(Math.Max(1, minutes)));
    }

    public bool Matches(int userId, string presentedCode, string codeHash)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(userId, presentedCode)),
            Encoding.ASCII.GetBytes(codeHash));
    }

    public TimeSpan ResendCooldown()
    {
        var seconds = configuration.GetValue<int?>("Iam:PasswordResetResendSeconds") ?? DefaultResendSeconds;
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    public DateTimeOffset Now()
    {
        return timeProvider.GetUtcNow();
    }

    private string Hash(int userId, string code)
    {
        var secret = configuration["TokenSettings:Secret"]
                     ?? throw new InvalidOperationException("TokenSettings:Secret is not configured.");
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("password-reset:" + secret));
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{userId}:{code}")));
    }
}

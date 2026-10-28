namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>IAM-2. A reset code as issued: the digits for the email and what the server keeps of them.</summary>
/// <param name="Code">Six digits, handed to the email only. Never stored, never logged.</param>
/// <param name="Hash">Keyed hash (hex, 64 chars) of <paramref name="Code" />: the only thing persisted.</param>
/// <param name="IssuedAt">When it was issued.</param>
/// <param name="ExpiresAt">When it stops being accepted.</param>
public record IssuedPasswordResetCode(string Code, string Hash, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

/// <summary>IAM-2. A reset token as issued: the value for the client and what the server keeps of it.</summary>
/// <param name="Token">The opaque value handed to the client once. Never stored, never logged.</param>
/// <param name="Hash">SHA-256 (hex, 64 chars) of <paramref name="Token" />: the only thing persisted.</param>
/// <param name="ExpiresAt">When it stops being accepted.</param>
public record IssuedPasswordResetToken(string Token, string Hash, DateTimeOffset ExpiresAt);

/// <summary>
///     IAM-2. Issues and hashes password reset codes and the reset tokens their verification hands out. Lifetimes:
///     <c>Iam:PasswordResetCodeMinutes</c> (15 by default) and <c>Iam:PasswordResetTokenMinutes</c> (10 by default).
/// </summary>
public interface IPasswordResetCodeService
{
    /// <summary>New random digits for the account <paramref name="userId" />, their hash and their expiry.</summary>
    IssuedPasswordResetCode Issue(int userId);

    /// <summary>
    ///     Whether <paramref name="presentedCode" /> is the code behind <paramref name="codeHash" /> for the account
    ///     <paramref name="userId" />. Constant time.
    /// </summary>
    bool Matches(int userId, string presentedCode, string codeHash);

    /// <summary>A new random reset token, its hash and its expiry. Used when a code is verified.</summary>
    IssuedPasswordResetToken IssueResetToken();

    /// <summary>The hash a presented reset token is looked up by.</summary>
    string HashResetToken(string token);

    /// <summary>
    ///     How long an account waits before a new code is emailed: <c>Iam:PasswordResetResendSeconds</c> (60 by
    ///     default). Keeps the endpoint from flooding an inbox.
    /// </summary>
    TimeSpan ResendCooldown();

    /// <summary>The instant the expiry is compared against.</summary>
    DateTimeOffset Now();
}

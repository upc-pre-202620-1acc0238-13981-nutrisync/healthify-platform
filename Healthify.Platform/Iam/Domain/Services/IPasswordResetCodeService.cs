namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>IAM-2. A reset code as issued: the digits for the email and what the server keeps of them.</summary>
/// <param name="Code">Six digits, handed to the email only. Never stored, never logged.</param>
/// <param name="Hash">Keyed hash (hex, 64 chars) of <paramref name="Code" />: the only thing persisted.</param>
/// <param name="IssuedAt">When it was issued.</param>
/// <param name="ExpiresAt">When it stops being accepted.</param>
public record IssuedPasswordResetCode(string Code, string Hash, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

/// <summary>
///     IAM-2. Issues and hashes password reset codes. The lifetime is <c>Iam:PasswordResetCodeMinutes</c> (15 by
///     default).
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

    /// <summary>
    ///     How long an account waits before a new code is emailed: <c>Iam:PasswordResetResendSeconds</c> (60 by
    ///     default). Keeps the endpoint from flooding an inbox.
    /// </summary>
    TimeSpan ResendCooldown();

    /// <summary>The instant the expiry is compared against.</summary>
    DateTimeOffset Now();
}

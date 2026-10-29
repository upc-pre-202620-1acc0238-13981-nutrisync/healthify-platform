namespace Healthify.Platform.Iam.Domain.Model.Aggregates;

/// <summary>
///     A single-use code that lets the owner of an account set a new password without signing in. The code
///     itself goes by email and is never stored: only its keyed hash is.
/// </summary>
/// <remarks>
///     An account holds at most one code at a time; asking for a new one removes the previous one. A code stops
///     working when it expires, when it is verified, or after <see cref="MaxFailedAttempts" /> wrong guesses: six digits
///     are easy to guess, so the attempts are what protects them.
///     Verifying the code issues a reset token: random and long, short-lived and single-use. Only the token sets the
///     password, so the code is checked once and the password can be retried without spending attempts.
/// </remarks>
public partial class PasswordResetCode
{
    /// <summary>Business rule: Reset Code Attempts Are Limited.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Required by EF Core.</summary>
    protected PasswordResetCode()
    {
    }

    public PasswordResetCode(int userId, string codeHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        if (userId <= 0) throw new InvalidOperationException("A reset code requires the owning user identifier.");
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new InvalidOperationException("A reset code requires the hash of the code.");
        if (expiresAt <= issuedAt) throw new InvalidOperationException("A reset code must expire after it is issued.");

        UserId = userId;
        CodeHash = codeHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        FailedAttempts = 0;
        UsedAt = null;
    }

    public int Id { get; private set; }

    /// <summary>Identifier of the owning account. Same bounded context, separate aggregate root.</summary>
    public int UserId { get; private set; }

    /// <summary>Keyed hash (hex, 64 chars) of the code. The code itself is never stored, never logged.</summary>
    public string CodeHash { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }

    /// <summary>When the password was set with this code. Null while it has not been.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>When the code was verified and the reset token issued. Null while it has not been.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>
    ///     SHA-256 (hex, 64 chars) of the reset token. The token itself is never stored. Null before the code is
    ///     verified and after the token is used.
    /// </summary>
    public string? ResetTokenHash { get; private set; }

    /// <summary>When the reset token stops being accepted.</summary>
    public DateTimeOffset? ResetTokenExpiresAt { get; private set; }

    /// <summary>The code can still be verified at <paramref name="now" />: not verified, not expired, attempts left.</summary>
    public bool IsUsableAt(DateTimeOffset now)
    {
        return UsedAt is null && VerifiedAt is null && now < ExpiresAt && FailedAttempts < MaxFailedAttempts;
    }

    /// <summary>Records a wrong code. The last allowed failure leaves the code unusable.</summary>
    public void RegisterFailedAttempt()
    {
        if (FailedAttempts < MaxFailedAttempts) FailedAttempts++;
    }

    /// <summary>
    ///     Business rule: A Reset Code Is Verified Once. Accepts the code and keeps the hash of the reset
    ///     token that will set the password.
    /// </summary>
    public void Verify(DateTimeOffset now, string resetTokenHash, DateTimeOffset resetTokenExpiresAt)
    {
        if (!IsUsableAt(now)) throw new InvalidOperationException("This reset code can no longer be verified.");
        if (string.IsNullOrWhiteSpace(resetTokenHash))
            throw new InvalidOperationException("Verifying a reset code requires the hash of the reset token.");
        if (resetTokenExpiresAt <= now)
            throw new InvalidOperationException("A reset token must expire after it is issued.");

        VerifiedAt = now;
        ResetTokenHash = resetTokenHash;
        ResetTokenExpiresAt = resetTokenExpiresAt;
    }

    /// <summary>The reset token is current at <paramref name="now" />: issued, not expired and not used.</summary>
    public bool AcceptsResetTokenAt(DateTimeOffset now)
    {
        return UsedAt is null && ResetTokenHash is not null && ResetTokenExpiresAt is { } expiresAt && now < expiresAt;
    }

    /// <summary>Consumes the code and its reset token: they cannot set a password twice.</summary>
    public void MarkAsUsed(DateTimeOffset now)
    {
        if (!AcceptsResetTokenAt(now)) throw new InvalidOperationException("This reset token can no longer be used.");
        UsedAt = now;
        ResetTokenHash = null;
        ResetTokenExpiresAt = null;
    }
}

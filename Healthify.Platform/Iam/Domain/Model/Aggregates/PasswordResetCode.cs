namespace Healthify.Platform.Iam.Domain.Model.Aggregates;

/// <summary>
///     IAM-2. A single-use code that lets the owner of an account set a new password without signing in. The code
///     itself goes by email and is never stored: only its keyed hash is.
/// </summary>
/// <remarks>
///     An account holds at most one code at a time; asking for a new one removes the previous one. A code stops
///     working when it expires, when it is used, or after <see cref="MaxFailedAttempts" /> wrong guesses: six digits
///     are easy to guess, so the attempts are what protects them.
/// </remarks>
public partial class PasswordResetCode
{
    /// <summary>Business rule: Reset Code Attempts Are Limited (IAM-2).</summary>
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
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Not used, not expired and with attempts left at <paramref name="now" />.</summary>
    public bool IsUsableAt(DateTimeOffset now)
    {
        return UsedAt is null && now < ExpiresAt && FailedAttempts < MaxFailedAttempts;
    }

    /// <summary>Records a wrong code. The last allowed failure leaves the code unusable.</summary>
    public void RegisterFailedAttempt()
    {
        if (FailedAttempts < MaxFailedAttempts) FailedAttempts++;
    }

    /// <summary>Consumes the code: it cannot set a password twice.</summary>
    public void MarkAsUsed(DateTimeOffset now)
    {
        if (!IsUsableAt(now)) throw new InvalidOperationException("This reset code can no longer be used.");
        UsedAt = now;
    }
}

namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>IAM-2. Body of <c>POST /authentication/password-resets</c>.</summary>
public record PasswordResetResource(string? ResetToken, string? NewPassword)
{
    /// <summary>The token returned by <c>POST /authentication/password-reset-verifications</c>. Valid for one use.</summary>
    public string? ResetToken { get; init; } = ResetToken;

    /// <summary>
    ///     The new password: 8 to 128 characters, with an uppercase letter, a lowercase letter, a digit and a
    ///     symbol, and different from the current one.
    /// </summary>
    public string? NewPassword { get; init; } = NewPassword;
}

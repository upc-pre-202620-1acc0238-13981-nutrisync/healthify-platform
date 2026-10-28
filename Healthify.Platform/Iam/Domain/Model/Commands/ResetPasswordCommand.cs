namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>
///     IAM-2 - Reset Password. Sets a new password with the reset token issued when the code was verified; every open
///     session of the account ends.
/// </summary>
/// <param name="ResetToken">The token returned by the verification. Never logged.</param>
/// <param name="NewPassword">Never logged.</param>
public record ResetPasswordCommand(string? ResetToken, string? NewPassword);

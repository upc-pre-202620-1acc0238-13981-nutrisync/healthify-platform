namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>
///     IAM-2 - Reset Password. Sets a new password with the code that was emailed; every open session of the account
///     ends.
/// </summary>
/// <param name="Email">The email the code was sent to.</param>
/// <param name="Code">The six digits received by email. Never logged.</param>
/// <param name="NewPassword">Never logged.</param>
public record ResetPasswordCommand(string? Email, string? Code, string? NewPassword);

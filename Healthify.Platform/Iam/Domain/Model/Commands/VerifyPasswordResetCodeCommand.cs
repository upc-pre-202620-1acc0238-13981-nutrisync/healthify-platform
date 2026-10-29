namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>
///     Verify Password Reset Code. Checks the code that was emailed and, if it is right, issues the reset
///     token that sets the new password.
/// </summary>
/// <param name="Email">The email the code was sent to.</param>
/// <param name="Code">The six digits received by email. Never logged.</param>
public record VerifyPasswordResetCodeCommand(string? Email, string? Code);

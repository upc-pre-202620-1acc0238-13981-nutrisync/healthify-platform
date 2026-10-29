namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>
///     Request Password Reset. Emails a reset code to the account, if there is one. The answer is the same
///     whether the email has an account or not.
/// </summary>
public record RequestPasswordResetCommand(string? Email);

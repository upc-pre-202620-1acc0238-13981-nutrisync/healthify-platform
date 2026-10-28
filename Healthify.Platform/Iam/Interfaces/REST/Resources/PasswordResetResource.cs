namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>IAM-2. Body of <c>POST /authentication/password-resets</c>.</summary>
public record PasswordResetResource(string? Email, string? Code, string? NewPassword)
{
    /// <summary>The email the code was sent to.</summary>
    public string? Email { get; init; } = Email;

    /// <summary>The six digits received by email.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>
    ///     The new password: 8 to 128 characters, with an uppercase letter, a lowercase letter, a digit and a
    ///     symbol.
    /// </summary>
    public string? NewPassword { get; init; } = NewPassword;
}

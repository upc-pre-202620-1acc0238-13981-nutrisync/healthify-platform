namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>Body of <c>POST /authentication/password-reset-verifications</c>.</summary>
public record PasswordResetVerificationResource(string? Email, string? Code)
{
    /// <summary>The email the code was sent to.</summary>
    public string? Email { get; init; } = Email;

    /// <summary>The six digits received by email.</summary>
    public string? Code { get; init; } = Code;
}

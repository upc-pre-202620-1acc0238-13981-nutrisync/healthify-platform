namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>Body of <c>POST /authentication/password-reset-requests</c>.</summary>
public record PasswordResetRequestResource(string? Email)
{
    /// <summary>The email of the account to recover.</summary>
    public string? Email { get; init; } = Email;
}

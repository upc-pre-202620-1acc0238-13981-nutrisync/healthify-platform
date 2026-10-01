namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>Payload of Subflow 1.2 - Sign In and Role Claim.</summary>
public record SignInResource(string Email, string Password)
{
    /// <summary>Email address of the account.</summary>
    public string Email { get; init; } = Email;

    /// <summary>Password of the account.</summary>
    public string Password { get; init; } = Password;
}

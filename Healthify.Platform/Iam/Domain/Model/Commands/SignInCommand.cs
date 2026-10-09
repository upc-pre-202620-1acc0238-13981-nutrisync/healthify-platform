namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>Subflow 1.2 - Sign In and Role Claim.</summary>
public record SignInCommand(string Email, string Password);

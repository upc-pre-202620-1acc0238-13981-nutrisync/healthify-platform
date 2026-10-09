namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>Subflow 1.3 - Sign Out.</summary>
public record SignOutCommand(int SessionId, int UserId);

namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>
///     Subflow 1.2 - Select Navigation Shell. Issued by the policy that reacts to Role Claim Issued,
///     never by a user. It has no REST endpoint.
/// </summary>
public record SelectNavigationShellCommand(int SessionId, string NavigationShell);

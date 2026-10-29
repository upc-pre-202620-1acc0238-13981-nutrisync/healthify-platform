namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>Saves the interface language on the account; the app also keeps it on the device.</summary>
/// <param name="UserId">The account, always the authenticated one.</param>
/// <param name="Language"><c>es</c> or <c>en</c>.</param>
public record ChangePreferredLanguageCommand(int UserId, string? Language);

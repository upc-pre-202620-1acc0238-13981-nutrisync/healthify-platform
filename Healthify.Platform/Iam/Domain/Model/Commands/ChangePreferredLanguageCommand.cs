namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>IAM-3. «El idioma se guarda en el dispositivo y en la cuenta».</summary>
/// <param name="UserId">The account, always the authenticated one.</param>
/// <param name="Language"><c>es</c> or <c>en</c>.</param>
public record ChangePreferredLanguageCommand(int UserId, string? Language);

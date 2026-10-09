namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>IAM-3. Body of <c>PUT /users/{userId}/preferred-language</c>.</summary>
public record ChangePreferredLanguageResource(string? Language)
{
    /// <summary><c>es</c> or <c>en</c>. Only the interface is translated, never clinical data.</summary>
    public string? Language { get; init; } = Language;
}

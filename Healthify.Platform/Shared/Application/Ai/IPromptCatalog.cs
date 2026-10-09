namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IA-0. The versioned prompts. Each one is a <c>&lt;feature&gt;@&lt;n&gt;.md</c> file that declares the role,
///     the ethical rules, the language and the output schema.
/// </summary>
public interface IPromptCatalog
{
    /// <summary>The latest version of the prompt of <paramref name="feature" />, or null when it has none.</summary>
    PromptTemplate? FindLatest(AiFeature feature);
}

/// <param name="Version">The audit value, for instance <c>meal-ideas@3</c>.</param>
/// <param name="Text">The prompt without its schema block. <c>{{language}}</c> is replaced at render time.</param>
/// <param name="OutputSchemaJson">The JSON Schema declared in the file.</param>
public record PromptTemplate(string Version, string Text, string OutputSchemaJson)
{
    public const string LanguagePlaceholder = "{{language}}";

    /// <summary>The system instruction for one request: the prompt in the requested language.</summary>
    public string Render(string language)
    {
        return Text.Replace(LanguagePlaceholder, language, StringComparison.Ordinal);
    }
}

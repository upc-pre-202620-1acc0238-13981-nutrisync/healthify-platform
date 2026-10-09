using System.Reflection;
using System.Text.RegularExpressions;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Prompts;

/// <summary>
///     IA-0. The prompts as versioned Markdown files, <c>&lt;feature&gt;@&lt;n&gt;.md</c> (kebab-case feature name,
///     whole version number). They live in the context that owns the function
///     (<c>&lt;Context&gt;/Infrastructure/Ai/Prompts/</c>) and are embedded in the assembly under
///     <see cref="ResourcePrefix" /> (see the csproj), so this catalog references no context. The latest version of
///     each feature is the one used; older files stay for the audit trail of <c>prompt_version</c>.
/// </summary>
/// <remarks>
///     A file declares its output schema in a <c>```json</c> block under a heading whose text is <c>Output schema</c>;
///     that block is removed from the text sent as the system instruction. See <c>README.md</c> in this folder.
/// </remarks>
public partial class PromptCatalog : IPromptCatalog
{
    public const string ResourcePrefix = "AiPrompts/";

    private readonly Dictionary<string, PromptTemplate> _latest;

    /// <param name="files">File name (<c>meal-ideas@2.md</c>) and contents.</param>
    public PromptCatalog(IEnumerable<KeyValuePair<string, string>> files)
    {
        _latest = files
            .Select(f => Parse(f.Key, f.Value))
            .OfType<(string Feature, int Version, PromptTemplate Template)>()
            .GroupBy(p => p.Feature, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.MaxBy(p => p.Version).Template, StringComparer.OrdinalIgnoreCase);
    }

    public PromptTemplate? FindLatest(AiFeature feature)
    {
        return _latest.GetValueOrDefault(feature.PromptName);
    }

    /// <summary>The prompts embedded in <paramref name="assembly" />.</summary>
    public static PromptCatalog FromEmbeddedResources(Assembly assembly)
    {
        var files = new List<KeyValuePair<string, string>>();
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files.Add(new KeyValuePair<string, string>(name[ResourcePrefix.Length..], reader.ReadToEnd()));
        }

        return new PromptCatalog(files);
    }

    /// <exception cref="InvalidOperationException">A versioned file has no output schema block.</exception>
    private static (string Feature, int Version, PromptTemplate Template)? Parse(string fileName, string contents)
    {
        var name = FileNamePattern().Match(Path.GetFileName(fileName));
        if (!name.Success) return null;

        var schema = SchemaBlockPattern().Match(contents);
        if (!schema.Success)
            throw new InvalidOperationException($"Prompt {fileName} declares no '## Output schema' json block.");

        var feature = name.Groups["feature"].Value.ToLowerInvariant();
        var version = int.Parse(name.Groups["version"].Value);
        var text = contents.Remove(schema.Index, schema.Length).Trim();
        return (feature, version,
            new PromptTemplate($"{feature}@{version}", text, schema.Groups["schema"].Value.Trim()));
    }

    [GeneratedRegex(@"^(?<feature>[a-z0-9]+(?:-[a-z0-9]+)*)@(?<version>[0-9]{1,6})\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^#{1,6}[ \t]+Output schema[ \t]*\r?\n+```json[ \t]*\r?\n(?<schema>.*?)\r?\n```[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SchemaBlockPattern();
}

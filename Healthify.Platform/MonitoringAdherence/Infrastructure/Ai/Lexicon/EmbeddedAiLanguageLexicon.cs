using System.Text.Json;
using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Ai.Lexicon;

/// <summary>
///     IA-2/IA-4/IA-5. <see cref="IAiLanguageLexicon" /> over <c>generated-text-lexicon.json</c>, a file of this
///     context embedded in the assembly (see the csproj). Spanish and English terms are merged: a text is checked
///     against both, whatever language it was asked in.
/// </summary>
public sealed class EmbeddedAiLanguageLexicon : IAiLanguageLexicon
{
    public const string ResourceName = "MonitoringAdherence/generated-text-lexicon.json";

    private EmbeddedAiLanguageLexicon(IReadOnlyCollection<string> accusatory, IReadOnlyCollection<string> diagnosis,
        IReadOnlyCollection<string> consistency)
    {
        AccusatoryTerms = accusatory;
        DiagnosisTerms = diagnosis;
        ConsistencyTerms = consistency;
    }

    /// <summary>The lexicon embedded in this assembly, loaded once.</summary>
    public static EmbeddedAiLanguageLexicon Instance { get; } = Load();

    public IReadOnlyCollection<string> AccusatoryTerms { get; }
    public IReadOnlyCollection<string> DiagnosisTerms { get; }
    public IReadOnlyCollection<string> ConsistencyTerms { get; }

    /// <exception cref="InvalidOperationException">The file is missing or has an empty section.</exception>
    private static EmbeddedAiLanguageLexicon Load()
    {
        using var stream = typeof(EmbeddedAiLanguageLexicon).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        using var document = JsonDocument.Parse(stream);
        return new EmbeddedAiLanguageLexicon(Section(document, "accusatory"), Section(document, "diagnosis"),
            Section(document, "consistency"));
    }

    private static IReadOnlyCollection<string> Section(JsonDocument document, string name)
    {
        var terms = document.RootElement.GetProperty(name).EnumerateObject()
            .SelectMany(language => language.Value.EnumerateArray().Select(t => t.GetString()))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (terms.Count == 0) throw new InvalidOperationException($"The lexicon section '{name}' is empty.");
        return terms;
    }
}

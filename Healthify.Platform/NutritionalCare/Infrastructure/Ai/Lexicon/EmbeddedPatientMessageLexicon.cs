using System.Text.Json;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Ai.Lexicon;

/// <summary>
///     IA-8. <see cref="IPatientMessageLexicon" /> over <c>patient-message-lexicon.json</c>, a file of this context
///     embedded in the assembly (see the csproj). Spanish and English terms are merged: a message is checked against
///     both, whatever language it was asked in.
/// </summary>
public sealed class EmbeddedPatientMessageLexicon : IPatientMessageLexicon
{
    public const string ResourceName = "NutritionalCare/patient-message-lexicon.json";

    private EmbeddedPatientMessageLexicon(IReadOnlyCollection<string> accusatory, IReadOnlyCollection<string> diagnosis)
    {
        AccusatoryTerms = accusatory;
        DiagnosisTerms = diagnosis;
    }

    /// <summary>The lexicon embedded in this assembly, loaded once.</summary>
    public static EmbeddedPatientMessageLexicon Instance { get; } = Load();

    public IReadOnlyCollection<string> AccusatoryTerms { get; }
    public IReadOnlyCollection<string> DiagnosisTerms { get; }

    /// <exception cref="InvalidOperationException">The file is missing or has an empty section.</exception>
    private static EmbeddedPatientMessageLexicon Load()
    {
        using var stream = typeof(EmbeddedPatientMessageLexicon).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        using var document = JsonDocument.Parse(stream);
        return new EmbeddedPatientMessageLexicon(Section(document, "accusatory"), Section(document, "diagnosis"));
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

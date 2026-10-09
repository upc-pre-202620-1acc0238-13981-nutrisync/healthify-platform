namespace Healthify.Platform.IntakeBodyResponse.Domain.Services;

/// <summary>
///     IA-3. Which ingredients break a dietary restriction of the plan (NC-6), as a map ingredient → category
///     (shellfish, gluten, dairy…) → restriction, and which words a text for the patient must never carry. The map
///     lives in <c>Infrastructure/Ai/Lexicon/RestrictionLexicon.json</c>.
/// </summary>
/// <remarks>
///     A lexicon errs on the side of the patient: a doubtful ingredient is a broken restriction, and the idea is
///     discarded. An unknown restriction code restricts nothing here; the code list is closed upstream (NC-6).
/// </remarks>
public interface IRestrictionLexicon
{
    /// <summary>The codes of <paramref name="restrictionCodes" /> that <paramref name="text" /> breaks; empty when none.</summary>
    IReadOnlyList<string> ViolatedRestrictions(string text, IReadOnlyCollection<string> restrictionCodes);

    /// <summary>
    ///     Words a text for the patient must not carry: accusatory ones (Invitation Tone Never Accusation) and any
    ///     diagnosis, weight category or index (Diagnosis Never Leaves The Context). Empty when none.
    /// </summary>
    IReadOnlyList<string> ForbiddenTermsIn(string text);
}

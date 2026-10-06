namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>
///     IA-3. The two-hour cache of meal ideas, per patient and key (day, 50 kcal bucket of what is left, restrictions,
///     plan version, language and the ideas already seen).
/// </summary>
/// <remarks>
///     A cache, never a source of truth: what it loses is generated again. A hit is served only after the AI gates
///     (kill switch, flag, consent and the patient's preference) pass again, so ideas cached before a revocation are
///     never served after it; the revocation also evicts them (§12-#14).
/// </remarks>
public interface IMealIdeasCache
{
    bool TryGet(int patientId, string key, out MealIdeasView? view);

    void Set(int patientId, string key, MealIdeasView view, TimeSpan lifetime);

    /// <summary>"Ver otras ideas": the names of ideas this patient was shown, by identifier, while they are cached.</summary>
    IReadOnlyList<string> FindIdeaNames(int patientId, IReadOnlyCollection<string> mealIdeaIds);

    /// <summary>§12-#14. Removes every entry of the patient.</summary>
    /// <returns>Entries removed.</returns>
    int Evict(int patientId);
}

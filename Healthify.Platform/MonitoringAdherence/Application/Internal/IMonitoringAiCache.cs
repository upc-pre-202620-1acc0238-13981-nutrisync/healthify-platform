using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     IA-4/IA-5. The short-lived cache of generated content of this context (suggested questions per visit,
///     monitoring summaries for six hours), keyed by patient, function and key.
/// </summary>
/// <remarks>
///     A cache, never a source of truth: what it loses is generated again. A hit is served only after the AI gates
///     (kill switch, flag, consent) pass again, so content cached before a revocation is never served after it.
/// </remarks>
public interface IMonitoringAiCache
{
    bool TryGet<T>(AiFeature feature, int patientId, string key, out T? value) where T : class;

    void Set<T>(AiFeature feature, int patientId, string key, T value, TimeSpan lifetime) where T : class;

    /// <summary>§12-#14. Removes every entry of the patient, or only those of one function.</summary>
    /// <returns>Entries removed.</returns>
    int Evict(int patientId, AiFeature? feature = null);
}

using System.Collections.Concurrent;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Ai.Caching;

/// <summary>
///     IA-4/IA-5. <see cref="IMonitoringAiCache" /> in the memory of the process (singleton), with an absolute
///     lifetime per entry measured on <see cref="TimeProvider" />.
/// </summary>
/// <remarks>
///     DECISIÓN IA-4: in memory, not in a table. The MD allows it ("en memoria o en ai_generations", IA-3) and it is
///     the safer side of §12-#14: generated content that nobody stored cannot outlive a revocation, a restart only
///     costs a new generation, and the validated output stays audited in <c>ai_generations</c> anyway. With several
///     instances each keeps its own entries; a hit is served only after the consent gate passes again (see the
///     command services), so an instance that missed the revocation event still serves nothing.
/// </remarks>
public sealed class InMemoryMonitoringAiCache(TimeProvider timeProvider) : IMonitoringAiCache
{
    /// <summary>Above this many entries, expired ones are swept on the next write.</summary>
    public const int SweepThreshold = 5_000;

    private readonly ConcurrentDictionary<(string Feature, int PatientId, string Key), Entry> _entries = new();

    public bool TryGet<T>(AiFeature feature, int patientId, string key, out T? value) where T : class
    {
        value = null;
        var id = (feature.Name, patientId, key);
        if (!_entries.TryGetValue(id, out var entry)) return false;
        if (entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            _entries.TryRemove(id, out _);
            return false;
        }

        value = entry.Value as T;
        return value is not null;
    }

    public void Set<T>(AiFeature feature, int patientId, string key, T value, TimeSpan lifetime) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var now = timeProvider.GetUtcNow();
        if (_entries.Count > SweepThreshold)
            foreach (var expired in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToList())
                _entries.TryRemove(expired, out _);

        _entries[(feature.Name, patientId, key)] = new Entry(value, now + lifetime);
    }

    public int Evict(int patientId, AiFeature? feature = null)
    {
        var removed = 0;
        foreach (var id in _entries.Keys
                     .Where(k => k.PatientId == patientId && (feature is null || k.Feature == feature.Name))
                     .ToList())
            if (_entries.TryRemove(id, out _))
                removed++;
        return removed;
    }

    private sealed record Entry(object Value, DateTimeOffset ExpiresAt);
}

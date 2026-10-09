using System.Collections.Concurrent;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Caching;

/// <summary>
///     IA-3. <see cref="IMealIdeasCache" /> in the memory of the process (singleton), with an absolute lifetime per
///     entry measured on <see cref="TimeProvider" />.
/// </summary>
/// <remarks>
///     DECISIÓN IA-3: in memory, not in a table, as the MD allows («en memoria o en ai_generations») and as IA-4/IA-5
///     already do: ideas nobody stored cannot outlive a revocation (§12-#14), a restart only costs a new generation,
///     and the validated output stays audited in <c>ai_generations</c>. With several instances each keeps its own
///     entries; a hit is served only after the consent gate passes again.
/// </remarks>
public sealed class InMemoryMealIdeasCache(TimeProvider timeProvider) : IMealIdeasCache
{
    /// <summary>Above this many entries, expired ones are swept on the next write.</summary>
    public const int SweepThreshold = 5_000;

    private readonly ConcurrentDictionary<(int PatientId, string Key), Entry> _entries = new();

    public bool TryGet(int patientId, string key, out MealIdeasView? view)
    {
        view = null;
        var id = (patientId, key);
        if (!_entries.TryGetValue(id, out var entry)) return false;
        if (entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            _entries.TryRemove(id, out _);
            return false;
        }

        view = entry.View;
        return true;
    }

    public void Set(int patientId, string key, MealIdeasView view, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(view);
        var now = timeProvider.GetUtcNow();
        if (_entries.Count > SweepThreshold)
            foreach (var expired in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToList())
                _entries.TryRemove(expired, out _);

        _entries[(patientId, key)] = new Entry(view, now + lifetime);
    }

    public IReadOnlyList<string> FindIdeaNames(int patientId, IReadOnlyCollection<string> mealIdeaIds)
    {
        if (mealIdeaIds.Count == 0) return [];
        var now = timeProvider.GetUtcNow();
        return _entries.Where(e => e.Key.PatientId == patientId && e.Value.ExpiresAt > now)
            .SelectMany(e => e.Value.View.Ideas)
            .Where(i => mealIdeaIds.Contains(i.MealIdeaId))
            .Select(i => i.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int Evict(int patientId)
    {
        var removed = 0;
        foreach (var id in _entries.Keys.Where(k => k.PatientId == patientId).ToList())
            if (_entries.TryRemove(id, out _))
                removed++;
        return removed;
    }

    private sealed record Entry(MealIdeasView View, DateTimeOffset ExpiresAt);
}

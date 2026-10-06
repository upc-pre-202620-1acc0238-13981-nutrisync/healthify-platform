using Healthify.Platform.IntakeBodyResponse.Application.Internal;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Caching;

/// <summary>
///     IN-7. <see cref="ICatalogNameHintsCache" /> in the memory of the process (singleton), with an absolute lifetime
///     measured on <see cref="TimeProvider" />. With several instances each keeps its own copy; a restart only costs one
///     query.
/// </summary>
public sealed class InMemoryCatalogNameHintsCache(TimeProvider timeProvider) : ICatalogNameHintsCache
{
    private volatile Entry? _entry;

    public bool TryGet(out IReadOnlyList<string> names)
    {
        var entry = _entry;
        if (entry is not null && entry.ExpiresAt > timeProvider.GetUtcNow())
        {
            names = entry.Names;
            return true;
        }

        names = [];
        return false;
    }

    public void Set(IReadOnlyList<string> names, TimeSpan lifetime)
    {
        _entry = new Entry(names.ToList(), timeProvider.GetUtcNow().Add(lifetime));
    }

    private sealed record Entry(IReadOnlyList<string> Names, DateTimeOffset ExpiresAt);
}

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>
///     IN-7. The catalog names passed to the photo recognition prompt, kept in memory so <c>diary_entries</c> is not
///     aggregated on every analysis. One entry for the whole platform: the list names foods, never a patient, so there
///     is nothing to purge on a revocation.
/// </summary>
public interface ICatalogNameHintsCache
{
    bool TryGet(out IReadOnlyList<string> names);

    void Set(IReadOnlyList<string> names, TimeSpan lifetime);
}

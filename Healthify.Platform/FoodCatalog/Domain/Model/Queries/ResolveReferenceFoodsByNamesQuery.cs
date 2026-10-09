namespace Healthify.Platform.FoodCatalog.Domain.Model.Queries;

/// <summary>
///     FC-2. The catalog entries that free names stand for, searched only in the local catalog (what was seeded,
///     cached from an import or created as a local override). No external provider is called.
/// </summary>
/// <param name="Names">The names, in the caller's order.</param>
/// <param name="Max">How many distinct names are resolved at most (1..50); the rest come back unresolved.</param>
public record ResolveReferenceFoodsByNamesQuery(IReadOnlyList<string> Names, int Max);

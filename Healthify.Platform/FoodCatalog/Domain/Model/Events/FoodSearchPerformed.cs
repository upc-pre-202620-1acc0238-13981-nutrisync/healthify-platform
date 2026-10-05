using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Events;

/// <summary>Subflow 6.3. Stays inside Food Catalog.</summary>
/// <param name="Term">What was searched for.</param>
/// <param name="ResultCount">How many catalog entries the search returned.</param>
/// <param name="ExternalProvidersConsulted">
///     False when the local catalog answered on its own, which is the ordinary case and the one the
///     fallback rule is about.
/// </param>
public record FoodSearchPerformed(
    string Term,
    int ResultCount,
    bool ExternalProvidersConsulted) : DomainEventBase;

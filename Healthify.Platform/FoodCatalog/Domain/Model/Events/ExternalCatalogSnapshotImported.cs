using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Events;

/// <summary>
///     Subflow 6.1. One snapshot from one provider finished. Stays inside Food Catalog.
/// </summary>
/// <param name="ProviderName">The name this platform gives the provider, not an upstream identifier.</param>
/// <param name="Term">What the provider was asked for.</param>
/// <param name="TranslatedCount">Records that crossed the anti-corruption layer.</param>
/// <param name="FailedCount">Records dropped because they could not be translated.</param>
public record ExternalCatalogSnapshotImported(
    string ProviderName,
    string Term,
    int TranslatedCount,
    int FailedCount) : DomainEventBase;

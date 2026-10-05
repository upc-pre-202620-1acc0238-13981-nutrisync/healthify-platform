using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Events;

/// <summary>
///     Subflow 6.1, negative branch. An upstream record could not be expressed in this platform
///     vocabulary and was dropped.
/// </summary>
/// <remarks>
///     Dropping it is the correct outcome. The alternative is letting a half-translated record into
///     the catalog, where it would later be logged as a meal and counted as intake. Nothing is
///     retried and no exception propagates: the import continues with the records that did translate.
///     The reason never carries an upstream identifier, for the same rule that keeps one out of the
///     aggregate.
/// </remarks>
public record TranslationFailed(string ProviderName, string Reason) : DomainEventBase;

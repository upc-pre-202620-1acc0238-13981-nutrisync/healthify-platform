using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Events;

/// <summary>
///     Subflow 6.2. The translated food is now in the local catalog.
/// </summary>
/// <remarks>
///     This event deliberately does not cross a boundary. Intake reads the Local Food Catalog
///     synchronously through the ACL facade, because logging a meal needs the food at that moment,
///     not whenever the catalog next refreshes.
/// </remarks>
public record ReferenceFoodCached(int ReferenceFoodId, string LocalName) : DomainEventBase;

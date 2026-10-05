using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Events;

/// <summary>Subflow 6.4. Stays inside Food Catalog.</summary>
public record LocalFoodOverrideCreated(
    int ReferenceFoodId,
    int PractitionerId,
    string LocalName) : DomainEventBase;

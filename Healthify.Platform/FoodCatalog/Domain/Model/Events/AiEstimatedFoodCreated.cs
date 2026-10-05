using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Events;

/// <summary>IN-7. A food created from nutrients the AI estimated. Stays inside Food Catalog; carries no patient.</summary>
public record AiEstimatedFoodCreated(
    int ReferenceFoodId,
    string LocalName,
    long AiGenerationId) : DomainEventBase;

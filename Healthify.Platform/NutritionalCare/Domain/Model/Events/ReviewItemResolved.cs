using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>Subflow 3.7. Stays inside Nutritional Care.</summary>
public record ReviewItemResolved(
    int ReviewItemId,
    int PatientId,
    bool ResolvedWithAdjustment) : DomainEventBase;

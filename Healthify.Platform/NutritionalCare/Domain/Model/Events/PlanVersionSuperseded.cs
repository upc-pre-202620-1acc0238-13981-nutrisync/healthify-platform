using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>Subflow 3.6. Stays inside Nutritional Care. The superseded row is kept, never deleted.</summary>
public record PlanVersionSuperseded(
    int SupersededPlanId,
    int PatientId,
    int SupersededVersion,
    int NewVersion) : DomainEventBase;

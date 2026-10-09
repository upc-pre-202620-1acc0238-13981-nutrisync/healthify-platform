using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     Subflow 3.6. Consumed by this context own policy, which publishes the active targets contract.
/// </summary>
public record NutritionPlanAdjusted(
    int PlanId,
    int PatientId,
    int Version,
    string ChangeReason) : DomainEventBase;

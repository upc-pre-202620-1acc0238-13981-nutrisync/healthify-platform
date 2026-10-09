using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     Subflow 3.4, the other outcome of Prescribe Targets. Crosses no boundary: it carries the
///     override reason, which is professional information.
/// </summary>
public record TargetsOverridden(
    int PlanId,
    int PatientId,
    decimal EnergyKcal,
    string OverrideReason) : DomainEventBase;

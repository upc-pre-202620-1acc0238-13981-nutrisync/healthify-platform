using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     Subflow 3.4, one of the two mutually exclusive outcomes of Prescribe Targets. Crosses no
///     boundary.
/// </summary>
public record TargetsAcceptedAsProposed(int PlanId, int PatientId, decimal EnergyKcal) : DomainEventBase;

using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     Subflow 3.3. Deliberately crosses no boundary: the calculation basis is professional
///     information, and the patient receives the result rather than the procedure.
/// </summary>
public record TargetsProposed(
    int PlanId,
    int PatientId,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG) : DomainEventBase;

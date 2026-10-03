using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     NC-2/NC-7. A consultation published its plan and closed. Integration event: Monitoring consumes it in
///     MA-2 to mark the appointment it started from as completed. It carries no diagnosis and no calculation
///     basis.
/// </summary>
/// <remarks>Consumed by Monitoring (MA-2, OnConsultationCompletedHandler).</remarks>
public record ConsultationCompleted(
    int ConsultationId,
    int PatientId,
    int PractitionerId,
    int PlanVersion,
    DateTimeOffset CompletedAt,
    int? ScheduledFollowUpId) : DomainEventBase;

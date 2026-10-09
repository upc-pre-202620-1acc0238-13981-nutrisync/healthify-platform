using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>NC-2 (minimal, created with NC-3). Stays inside Nutritional Care.</summary>
public record ConsultationStarted(int ConsultationId, int PatientId, int PractitionerId, int? ScheduledFollowUpId)
    : DomainEventBase;

using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>A guided consultation started. Stays inside Nutritional Care.</summary>
public record ConsultationStarted(int ConsultationId, int PatientId, int PractitionerId, int? ScheduledFollowUpId)
    : DomainEventBase;

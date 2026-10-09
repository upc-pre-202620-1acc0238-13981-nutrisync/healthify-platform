using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>Subflow 3.1. Stays inside Nutritional Care.</summary>
public record NutritionalAssessmentRecorded(int AssessmentId, int PatientId, int PractitionerId)
    : DomainEventBase;

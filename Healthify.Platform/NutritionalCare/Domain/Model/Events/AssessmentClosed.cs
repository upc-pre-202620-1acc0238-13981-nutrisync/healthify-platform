using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>Subflow 3.1. Stays inside Nutritional Care.</summary>
public record AssessmentClosed(int AssessmentId, int PatientId, DateTimeOffset ClosedAt) : DomainEventBase;

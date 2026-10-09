using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>NC-1. Stays inside Nutritional Care.</summary>
public record PatientBaselineRecorded(int PatientId) : DomainEventBase;

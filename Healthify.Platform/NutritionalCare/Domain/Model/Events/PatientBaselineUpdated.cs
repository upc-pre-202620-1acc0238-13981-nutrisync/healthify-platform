using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>NC-1. Stays inside Nutritional Care. Past assessments keep their own snapshot.</summary>
public record PatientBaselineUpdated(int PatientId) : DomainEventBase;

using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>Subflow 3.7. Stays inside Nutritional Care and dies in the practitioner inbox.</summary>
public record ReviewItemCreated(
    int ReviewItemId,
    int PatientId,
    int PractitionerId,
    string SignalType) : DomainEventBase;

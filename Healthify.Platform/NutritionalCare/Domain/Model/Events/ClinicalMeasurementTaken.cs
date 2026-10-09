using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     Subflow 3.1. Integration event 3 of 13: crosses into Monitoring and Adherence, whose policy
///     appends an anthropometry point (Subflow 5.3). A clinical measurement outranks a self weigh-in
///     and the two series are never merged, which is why the source travels with the value.
/// </summary>
public record ClinicalMeasurementTaken(
    int AssessmentId,
    int PatientId,
    decimal WeightKg,
    DateTimeOffset TakenAt) : DomainEventBase;

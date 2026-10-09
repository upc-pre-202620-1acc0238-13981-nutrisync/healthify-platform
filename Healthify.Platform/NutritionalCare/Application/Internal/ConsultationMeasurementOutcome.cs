using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>NC-3. What step 1 of the consultation produced: the consultation and its new closed assessment.</summary>
public record ConsultationMeasurementOutcome(Consultation Consultation, NutritionalAssessment Assessment);

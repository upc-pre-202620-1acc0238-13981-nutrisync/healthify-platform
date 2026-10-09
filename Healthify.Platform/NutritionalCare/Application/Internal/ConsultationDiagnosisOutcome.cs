using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>NC-4. What step 2 of the consultation produced: the consultation and its new active diagnosis.</summary>
public record ConsultationDiagnosisOutcome(Consultation Consultation, NutritionalDiagnosis Diagnosis);

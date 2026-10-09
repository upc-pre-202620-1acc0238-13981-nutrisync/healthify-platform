using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     Subflow 3.2. Deliberately crosses no boundary: the patient does not read their diagnosis in
///     the app. No other bounded context may declare a handler for it.
/// </summary>
public record NutritionalDiagnosisIssued(int DiagnosisId, int PatientId, int AssessmentId) : DomainEventBase;

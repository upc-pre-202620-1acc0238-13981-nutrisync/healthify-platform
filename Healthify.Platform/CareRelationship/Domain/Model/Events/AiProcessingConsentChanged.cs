using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     CR-2. The patient turned AI processing on or off: explicitly (PUT ai-processing-consent), when granting
///     consent with the switch on, or by withdrawing the whole consent while it was on. Published only when the
///     state really changes.
/// </summary>
/// <remarks>
///     Crosses the boundary on purpose: this context syncs the AI preferences (IA-1) and purges the technical audit
///     of the patient; each context that keeps AI content (MonitoringAdherence, IntakeBodyResponse,
///     NutritionalCare) purges its own when <paramref name="Granted" /> is false (§12-#14).
/// </remarks>
public record AiProcessingConsentChanged(int PatientId, bool Granted, DateTimeOffset At) : DomainEventBase;

using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     NC-4. A consultation issued a new diagnosis and the previous active one was superseded; since NC-7 this
///     happens when the consultation publishes its plan. Internal: like the diagnosis itself, it crosses no
///     boundary. The superseded row is kept, never deleted.
/// </summary>
public record NutritionalDiagnosisSuperseded(int DiagnosisId, int PatientId) : DomainEventBase;

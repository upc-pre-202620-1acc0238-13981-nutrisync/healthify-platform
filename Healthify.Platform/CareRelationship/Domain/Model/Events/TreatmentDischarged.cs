using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     Subflow 2.5. Integration event since CR-4: Monitoring and Adherence closes the evaluation window and
///     cancels the visits still ahead with this practitioner.
/// </summary>
public record TreatmentDischarged(
    int CareLinkId,
    int PatientId,
    int PractitionerId,
    string ClinicalReason,
    DateTimeOffset DischargedAt) : DomainEventBase;

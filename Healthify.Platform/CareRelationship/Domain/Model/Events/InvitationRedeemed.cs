using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     Subflow 2.2. Consumed by this context own policy, which establishes the care link. The
///     patient identifier travels with it because a care link cannot be created without knowing who
///     redeemed the invitation.
/// </summary>
public record InvitationRedeemed(int InvitationId, int PatientId, int PractitionerId) : DomainEventBase;

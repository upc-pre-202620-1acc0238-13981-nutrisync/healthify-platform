namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.2 - Establish Care Link. Issued by the policy "When Invitation Redeemed", never by
///     a user: a patient cannot create their own link. It has no REST endpoint.
/// </summary>
public record EstablishCareLinkCommand(int PatientId, int PractitionerId, int InvitationId);

using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class CareLinkResourceAssembler
{
    public static CareLinkResource ToResource(CareLink careLink)
    {
        return new CareLinkResource(
            careLink.Id.Value,
            careLink.PatientId,
            careLink.PractitionerId,
            careLink.IsActive,
            careLink.Consent is { IsGranted: true },
            careLink.Consent?.Scope,
            careLink.EstablishedAt,
            careLink.ConsentGrantedAt,
            careLink.ConsentWithdrawnAt,
            careLink.RevokedAt,
            careLink.DischargedAt,
            careLink.DischargeReason,
            careLink.PendingTargetsVersion,
            careLink.LastAcknowledgedVersion,
            careLink.ConsentAiProcessingGranted,
            careLink.ConsentAiProcessingDecidedAt);
    }
}

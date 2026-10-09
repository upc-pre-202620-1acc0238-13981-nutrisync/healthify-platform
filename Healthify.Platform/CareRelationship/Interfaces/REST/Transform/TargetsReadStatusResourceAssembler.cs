using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class TargetsReadStatusResourceAssembler
{
    public static TargetsReadStatusResource ToResource(CareLink careLink)
    {
        return new TargetsReadStatusResource(
            careLink.Id.Value,
            careLink.PatientId,
            careLink.PendingTargetsVersion,
            careLink.LastAcknowledgedVersion,
            careLink.PendingTargetsVersion is not null,
            careLink.LastAcknowledgedAt);
    }
}

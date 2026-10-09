using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;

namespace Healthify.Platform.CareRelationship.Application.QueryServices;

public interface IAiPreferencesQueryService
{
    /// <summary>
    ///     IA-1. Never null: a patient who never decided has no consent to AI processing and every function off.
    /// </summary>
    Task<AiPreferencesStatus> Handle(GetAiPreferencesByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}

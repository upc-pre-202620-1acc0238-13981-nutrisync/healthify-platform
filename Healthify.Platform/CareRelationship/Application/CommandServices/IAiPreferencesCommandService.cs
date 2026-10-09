using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.CareRelationship.Application.CommandServices;

public interface IAiPreferencesCommandService
{
    /// <summary>IA-1 - Update AI Preferences.</summary>
    Task<Result<AiPreferencesStatus, CareRelationshipError>> Handle(UpdateAiPreferencesCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>IA-1. Invoked by the policy that reacts to AI Processing Consent Changed, never by an endpoint.</summary>
    Task<Result<AiPreferencesStatus, CareRelationshipError>> Handle(SyncAiPreferencesWithConsentCommand command,
        CancellationToken cancellationToken = default);
}

using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

/// <summary>
///     NC-10 - The explicit action of the practitioner on an AI plan proposal. The only path from a review item to a
///     plan change, and it starts at the practitioner's POST: no policy, worker or handler resolves this service.
/// </summary>
public interface IPlanProposalCommandService
{
    /// <summary>
    ///     Accept Plan Proposal, as is or with edits: in one transaction the adjusted version is created, the version
    ///     in force is superseded and the item is resolved.
    /// </summary>
    Task<Result<PlanProposalAcceptanceOutcome, NutritionalCareError>> Handle(AcceptPlanProposalCommand command,
        CancellationToken cancellationToken = default);
}

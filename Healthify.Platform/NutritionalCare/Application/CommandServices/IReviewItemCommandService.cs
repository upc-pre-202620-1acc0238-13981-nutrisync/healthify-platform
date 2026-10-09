using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

public interface IReviewItemCommandService
{
    /// <summary>
    ///     Subflow 3.7 - Open Review Item. Invoked by the policies that react to signals from
    ///     Monitoring, never by an endpoint. It never modifies a plan.
    /// </summary>
    Task<Result<ReviewItem, NutritionalCareError>> Handle(OpenReviewItemCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 3.7 - Resolve Review Item.</summary>
    Task<Result<ReviewItem, NutritionalCareError>> Handle(ResolveReviewItemCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     NC-10 - Generate Plan Proposal (IA-8) for a sustained deviation, from the background worker. It attaches a
    ///     proposal to the item, or leaves it without one; it never modifies a plan.
    /// </summary>
    Task<Result<ReviewItem, NutritionalCareError>> Handle(GeneratePlanProposalCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     NC-10 - Open the scheduled rechecks whose date arrived, from the clock. Returns how many were opened. It
    ///     never modifies a plan.
    /// </summary>
    Task<Result<int, NutritionalCareError>> Handle(OpenDueRechecksCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     NC-10 - Queue again the plan proposals that were lost or never generated. Returns how many were queued.
    ///     Idempotent: an item that has a proposal or is already waiting is left alone. It never modifies a plan.
    /// </summary>
    Task<Result<int, NutritionalCareError>> Handle(RecoverPlanProposalsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     IA-8 - Purge the AI plan proposals of the patient that no practitioner accepted, when the patient's AI
    ///     processing ends. Returns how many were removed. Accepted proposals and the items stay. It never modifies a
    ///     plan.
    /// </summary>
    Task<Result<int, NutritionalCareError>> Handle(PurgeUnacceptedPlanProposalsCommand command,
        CancellationToken cancellationToken = default);
}

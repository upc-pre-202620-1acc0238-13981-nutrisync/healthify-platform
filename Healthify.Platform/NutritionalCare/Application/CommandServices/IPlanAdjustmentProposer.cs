using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

/// <summary>
///     Plan Adjustment Proposal, the engine of AI plan proposals: <c>ProposePlanAdjustment(PlanAdjustmentInput)</c>.
/// </summary>
/// <remarks>
///     The design puts it on <c>IAiAssistanceFacade</c>, but there is no AiAssistance context, so
///     it is a service of Nutritional Care, the owner of the plan and of its rules, called only by the review item
///     command service. It writes nothing but the audit row of the pipeline: it proposes, it never changes a
///     plan.
/// </remarks>
public interface IPlanAdjustmentProposer
{
    /// <summary>
    ///     One proposal through the shared pipeline (kill switch, the patient's AI consent, quota per
    ///     practitioner, the provider) and the hard validation of <see cref="PlanAdjustmentProposalOutputValidator" />.
    ///     Any <see cref="AiError" /> means there is no proposal and the review item shows without AI.
    /// </summary>
    Task<Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError>> ProposePlanAdjustment(
        PlanAdjustmentInput input, CancellationToken cancellationToken = default);
}

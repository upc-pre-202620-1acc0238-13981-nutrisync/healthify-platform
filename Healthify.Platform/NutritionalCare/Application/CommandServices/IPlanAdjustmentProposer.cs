using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

/// <summary>
///     IA-8 - Plan Adjustment Proposal, the engine of NC-10: <c>ProposePlanAdjustment(PlanAdjustmentInput)</c>.
/// </summary>
/// <remarks>
///     DECISIÓN IA-8: the MD puts it on <c>IAiAssistanceFacade</c>; there is no AiAssistance context (CLAUDE.md §1), so
///     it is a service of Nutritional Care, the owner of the plan and of its rules, called only by the review item
///     command service (NC-10). It writes nothing but the audit row of the pipeline: it proposes, it never changes a
///     plan.
/// </remarks>
public interface IPlanAdjustmentProposer
{
    /// <summary>
    ///     One proposal through the shared pipeline (kill switch, the patient's AI consent, §12-#5, quota per
    ///     practitioner, the provider) and the hard validation of <see cref="PlanAdjustmentProposalOutputValidator" />.
    ///     Any <see cref="AiError" /> means there is no proposal and PR14 shows the item without AI.
    /// </summary>
    Task<Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError>> ProposePlanAdjustment(
        PlanAdjustmentInput input, CancellationToken cancellationToken = default);
}

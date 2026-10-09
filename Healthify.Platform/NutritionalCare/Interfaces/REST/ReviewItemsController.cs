using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST;

/// <summary>
///     Subflow 3.7 of the Nutritional Care bounded context: the practitioner review inbox.
/// </summary>
/// <remarks>
///     Open Review Item has no endpoint. Items arrive through the policies that react to signals from
///     Monitoring, and this is where the automation stops: there is no route, automatic or manual,
///     from a signal to a plan change. Adjusting the plan is a separate, deliberate act on a
///     different endpoint.
///     NC-10: a sustained deviation may carry an AI plan proposal (PR14.IA). Reading it changes nothing; the plan
///     changes only through <c>POST /plan-proposal/acceptance</c>, the explicit action of the practitioner.
/// </remarks>
[ApiController]
[Route("api/v1/review-items")]
[Authorize(Roles = "Practitioner")]
[Tags("Review Inbox")]
[Produces("application/json")]
public class ReviewItemsController(
    IReviewItemCommandService commandService,
    IReviewItemQueryService queryService,
    IPlanProposalCommandService planProposalCommandService,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    /// <summary>NC-10. Seconds the client waits before asking again for a proposal being generated.</summary>
    private const int PlanProposalRetryAfterSeconds = 5;

    [HttpGet]
    [SwaggerOperation(
        Summary = "List the review items",
        Description =
            "Lists the alerts waiting for the practitioner's decision. Every alert was raised by the patient's " +
            "follow-up, and none of them has changed a plan. The open ones are listed by default; the resolved ones " +
            "can be listed instead. Each alert shows the patient's name, whether there is a plan proposal from the " +
            "AI, and the evidence as numbers.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The items in that state, most recent first.",
        typeof(IEnumerable<ReviewItemResource>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The state filter is not Open or Resolved.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    public async Task<IActionResult> GetOpenReviewItems([FromQuery] string? state)
    {
        if (!ReviewItemQueryAssembler.TryToQuery(this.GetAuthenticatedUserId(), state, out var query))
            return NutritionalCareActionResultAssembler.ToErrorResult(
                NutritionalCareError.InvalidReviewItemState, localizer);

        var entries = await queryService.Handle(query, HttpContext.RequestAborted);

        return Ok(entries.Select(ReviewItemResourceAssembler.ToResource));
    }

    [HttpGet("{reviewItemId:int}/plan-proposal")]
    [SwaggerOperation(
        Summary = "Read the AI plan proposal of a review item",
        Description =
            "Returns the plan adjustment the AI proposed for an alert. Only a sustained deviation gets one, and it " +
            "is prepared in the background after the alert opens. While it is being prepared, the answer is " +
            "'accepted' with a suggestion of when to ask again. Returns not found when there is none (AI off, no " +
            "agreement from the patient, a provider failure, or a proposal that broke a safety rule); then the " +
            "practitioner decides without it. Reading the proposal never changes the plan.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The proposal.", typeof(PlanAdjustmentProposalResource))]
    [SwaggerResponse(StatusCodes.Status202Accepted, "The proposal is being generated. Try again after Retry-After.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The item belongs to another practitioner inbox.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No such item, or it has no proposal.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetPlanProposal(int reviewItemId)
    {
        var lookup = await queryService.Handle(new GetPlanProposalByReviewItemIdQuery(reviewItemId),
            HttpContext.RequestAborted);
        if (lookup is null)
            return NutritionalCareActionResultAssembler.ToNotFoundResult(NutritionalCareError.ReviewItemNotFound,
                localizer);
        if (lookup.ReviewItem.PractitionerId != this.GetAuthenticatedUserId()) return Forbid();

        if (PlanAdjustmentProposalResourceAssembler.ToResource(lookup) is { } proposal) return Ok(proposal);
        if (!lookup.IsGenerating)
            return NutritionalCareActionResultAssembler.ToNotFoundResult(NutritionalCareError.PlanProposalNotFound,
                localizer);

        Response.Headers.RetryAfter = PlanProposalRetryAfterSeconds.ToString();
        return Accepted(new ProblemDetails
        {
            Status = StatusCodes.Status202Accepted,
            Title = localizer["PlanProposalGenerating"].Value
        });
    }

    [HttpPost("{reviewItemId:int}/plan-proposal/acceptance")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Assign the proposed plan",
        Description =
            "The practitioner assigns the proposed plan, as it is or with their own edits (targets, guidelines and " +
            "a message for the patient). All at once it creates the adjusted plan version with an automatic change " +
            "reason, replaces the version in force and resolves the alert; then the patient receives the new plan. " +
            "Restrictions stay as they are. Edits are limited only by the minimum calories. A recheck is scheduled " +
            "after the number of days the proposal suggests. To resolve the alert without assigning a plan, use the " +
            "resolution endpoint.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The item resolved and the version now in force.",
        typeof(PlanProposalAcceptanceResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "Edits missing, an unknown guideline code or a message longer than 500 characters.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "The item belongs to another practitioner inbox, or the care link is no longer active.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No such item, no proposal, or no version in force.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The proposal was already decided, the item is not a sustained deviation or the version was superseded.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "The energy is below the calorie floor.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> AcceptPlanProposal(int reviewItemId,
        [FromBody] AcceptPlanProposalResource resource)
    {
        var command = AcceptPlanProposalCommandAssembler.ToCommand(reviewItemId, this.GetAuthenticatedUserId(),
            resource);
        var result = await planProposalCommandService.Handle(command, HttpContext.RequestAborted);
        var entry = result.IsSuccess
            ? await queryService.Handle(new GetReviewInboxEntryByIdQuery(reviewItemId), HttpContext.RequestAborted)
            : null;
        return NutritionalCareActionResultAssembler.ToPlanProposalAcceptanceResult(result, localizer, entry);
    }

    [HttpPost("{reviewItemId:int}/resolution")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Resolve a review item",
        Description =
            "Closes an alert and records whether the plan was adjusted because of it. Saying so is required, so " +
            "there is a record of what each alert caused. The answer includes the same details as the list of " +
            "alerts. If an AI proposal was still pending, it is discarded and the plan does not change.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The item was resolved.", typeof(ReviewItemResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The resolution does not say whether the plan was adjusted.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The item belongs to another practitioner inbox.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No open review item was found.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> ResolveReviewItem(int reviewItemId,
        [FromBody] ResolveReviewItemResource resource)
    {
        var command = ResolveReviewItemCommandAssembler.ToCommand(
            reviewItemId, this.GetAuthenticatedUserId(), resource);
        var result = await commandService.Handle(command, HttpContext.RequestAborted);
        // NC-11: the same fields as GET /review-items, the patient's name included.
        var entry = result.IsSuccess
            ? await queryService.Handle(new GetReviewInboxEntryByIdQuery(reviewItemId), HttpContext.RequestAborted)
            : null;
        return NutritionalCareActionResultAssembler.ToReviewItemResult(result, entry, localizer);
    }
}

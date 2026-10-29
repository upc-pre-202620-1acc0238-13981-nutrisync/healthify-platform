namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>The AI plan proposal of a review item, or whether it is still being generated.</summary>
/// <param name="ReviewItemId">The item.</param>
public record GetPlanProposalByReviewItemIdQuery(int ReviewItemId);

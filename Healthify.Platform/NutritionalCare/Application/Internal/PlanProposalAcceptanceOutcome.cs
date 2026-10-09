using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>NC-10. What an accepted proposal leaves: the resolved item and the version now in force.</summary>
/// <param name="ReviewItem">The item, resolved with the adjustment.</param>
/// <param name="PlanVersion">The adjusted version assigned from the proposal.</param>
public record PlanProposalAcceptanceOutcome(ReviewItem ReviewItem, NutritionPlan PlanVersion);

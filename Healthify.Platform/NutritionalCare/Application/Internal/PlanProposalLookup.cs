using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>NC-10. PR14.IA: the item, and whether its proposal is still being generated.</summary>
/// <param name="ReviewItem">The item, with its proposal when there is one.</param>
/// <param name="IsGenerating">The proposal is queued or being generated (202 with Retry-After).</param>
/// <param name="CurrentEnergyKcal">Energy of the version in force ("1 796 → 1 650 kcal"), or null.</param>
public record PlanProposalLookup(ReviewItem ReviewItem, bool IsGenerating, decimal? CurrentEnergyKcal = null);

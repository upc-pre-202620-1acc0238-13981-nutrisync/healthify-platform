using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>IN-6. The entries a meal logged in a group became, in the order of its items.</summary>
/// <param name="MealGroupId">The group they share.</param>
/// <param name="Entries">One confirmed <c>Manual</c> entry per food.</param>
public record MealGroupLogOutcome(Guid MealGroupId, IReadOnlyList<DiaryEntry> Entries);

using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;

namespace Healthify.Platform.FoodCatalog.Application.Internal;

/// <summary>FC-2. One name and the catalog entry it stands for, or null when nothing in the catalog matches it.</summary>
/// <param name="Name">The name as the caller sent it.</param>
/// <param name="ReferenceFood">The entry chosen by <c>FoodNameMatcher</c>, or null.</param>
public record FoodNameResolution(string Name, ReferenceFood? ReferenceFood);

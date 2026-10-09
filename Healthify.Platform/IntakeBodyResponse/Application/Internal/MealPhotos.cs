using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>IN-7. What the model answers (schema of <c>meal-photo-recognition@n.md</c>).</summary>
public sealed record MealPhotoRecognitionOutput(
    string DishName,
    decimal EstimatedGrams,
    decimal Confidence,
    IReadOnlyList<MealPhotoAlternativeOutput> Alternatives,
    MealPhotoNutrientsOutput NutrientsPer100g);

/// <summary>IN-7. Another dish the photo could show, as the model wrote it.</summary>
public sealed record MealPhotoAlternativeOutput(string Name, decimal Grams);

/// <summary>IN-7. The nutrients per 100 g the model estimated for the main dish.</summary>
public sealed record MealPhotoNutrientsOutput(decimal Kcal, decimal Protein, decimal Carb, decimal Fat);

/// <summary>
///     IN-7, guard 6. The business rules of the recognition the schema cannot say: there is a dish (an empty name is
///     the model saying it sees none), names are names (they carry letters), the portion is in range and the
///     alternatives are complete. Nutrients are not judged here: they matter only if the dish has to be created, and
///     then the catalog judges them (AI Estimated Nutrients Coherent).
/// </summary>
public sealed class MealPhotoRecognitionOutputValidator : IAiOutputValidator<MealPhotoRecognitionOutput>
{
    public const int MaximumNameLength = 120;

    public IReadOnlyList<string> Validate(MealPhotoRecognitionOutput output)
    {
        var reasons = new List<string>();
        if (!IsName(output.DishName)) reasons.Add("$.dishName: no dish recognized.");
        if (output.EstimatedGrams is < MealPhotoPortion.MinimumGrams or > MealPhotoPortion.MaximumGrams)
            reasons.Add("$.estimatedGrams: outside 5–2000.");
        if (output.Confidence is < 0m or > 1m) reasons.Add("$.confidence: outside 0–1.");

        var alternatives = output.Alternatives ?? [];
        if (alternatives.Count > 3) reasons.Add("$.alternatives: more than 3.");
        for (var i = 0; i < alternatives.Count; i++)
        {
            if (!IsName(alternatives[i].Name)) reasons.Add($"$.alternatives[{i}].name: not a name.");
            if (alternatives[i].Grams is < MealPhotoPortion.MinimumGrams or > MealPhotoPortion.MaximumGrams)
                reasons.Add($"$.alternatives[{i}].grams: outside 5–2000.");
        }

        if (output.NutrientsPer100g is null) reasons.Add("$.nutrientsPer100g: missing.");
        return reasons;
    }

    private static bool IsName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaximumNameLength && name.Any(char.IsLetter);
    }
}

/// <summary>
///     IN-7. Application DTO: the analysis as the patient sees it on PT7. Says nothing about whether the food was in
///     the catalog or created from the estimate.
/// </summary>
/// <param name="AnalysisId">What <c>photo-logs</c> references.</param>
/// <param name="ReferenceFoodId">The catalog entry of the recognized dish.</param>
/// <param name="FoodName">Its name in the catalog.</param>
/// <param name="EstimatedGrams">The estimated portion.</param>
/// <param name="Confidence">How sure the AI was, from 0 to 1.</param>
/// <param name="Alternatives">Up to three, resolved to the local catalog when it carries them.</param>
/// <param name="ExpiresAt">After this instant the analysis can no longer be logged.</param>
public sealed record MealPhotoAnalysisView(
    Guid AnalysisId,
    int ReferenceFoodId,
    string FoodName,
    decimal EstimatedGrams,
    decimal Confidence,
    IReadOnlyList<MealPhotoAlternative> Alternatives,
    DateTimeOffset ExpiresAt);

namespace Healthify.Platform.FoodCatalog.Domain.Model.Commands;

/// <summary>
///     IN-7 - Create AI Estimated Food. Issued through the ACL by Intake when a dish recognized in a photo is in
///     neither the local catalog nor the external providers. Never by an endpoint.
/// </summary>
/// <remarks>Idempotent: the same name (normalized) always yields the same food, also when two requests race.</remarks>
/// <param name="Name">The dish as the AI named it (Spanish, the catalog's vocabulary).</param>
/// <param name="EnergyKcalPer100g">Estimated energy per 100 g.</param>
/// <param name="ProteinGPer100g">Estimated protein per 100 g.</param>
/// <param name="CarbGPer100g">Estimated carbohydrate per 100 g.</param>
/// <param name="FatGPer100g">Estimated fat per 100 g.</param>
/// <param name="AiGenerationId">The <c>ai_generations</c> row of the estimate.</param>
public record CreateAiEstimatedFoodCommand(
    string Name,
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g,
    long AiGenerationId);

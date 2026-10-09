using System.Globalization;
using System.Text.Json;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Services;

namespace Healthify.Platform.FoodCatalog.Infrastructure.External.Usda;

/// <summary>
///     Anti-corruption layer over USDA FoodData Central.
/// </summary>
/// <remarks>
///     The second of the two places where upstream vocabulary is allowed to appear. Same contract as
///     the other provider: it translates or it reports a failure, it never throws, and no upstream
///     identifier leaves this file.
///     USDA reports nutrients as a list keyed by nutrient number rather than as named fields, so the
///     translation here is a lookup rather than a rename. The numbers are the standard ones: 208
///     energy in kilocalories, 203 protein, 205 carbohydrate, 204 total fat.
/// </remarks>
public class UsdaFoodDataProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<UsdaFoodDataProvider> logger) : IExternalFoodCatalogProvider
{
    private const string EnergyKcalNutrientNumber = "208";
    private const string ProteinNutrientNumber = "203";
    private const string CarbohydrateNutrientNumber = "205";
    private const string FatNutrientNumber = "204";

    public string ProviderName => "USDA FoodData Central";

    public async Task<ExternalCatalogSnapshot> FetchSnapshotAsync(string term, int max,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Usda:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Unavailable("The provider is not configured with a key.");

        var url = "/foods/search"
                  + $"?api_key={Uri.EscapeDataString(apiKey)}"
                  + $"&query={Uri.EscapeDataString(term)}"
                  + $"&pageSize={Math.Clamp(max, 1, 100)}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Unavailable($"The provider answered {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return Translate(document);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "USDA FoodData Central could not be consulted for term {Term}", term);
            return Unavailable("The provider could not be reached.");
        }
    }

    private ExternalCatalogSnapshot Translate(JsonDocument document)
    {
        var translated = new List<ExternalFoodRecord>();
        var failures = new List<string>();

        if (!document.RootElement.TryGetProperty("foods", out var foods)
            || foods.ValueKind != JsonValueKind.Array)
            return new ExternalCatalogSnapshot(ProviderName, translated,
                ["The payload carried no result collection."]);

        foreach (var food in foods.EnumerateArray())
        {
            // The upstream key is read here, folded into the digest, and never named again.
            var upstreamKey = ReadIdentifier(food, "fdcId");
            var upstreamLabel = ReadString(food, "description");

            if (upstreamKey is null || string.IsNullOrWhiteSpace(upstreamLabel))
            {
                failures.Add("A record was dropped: it carried no usable name.");
                continue;
            }

            var nutrients = ReadNutrients(food);
            if (nutrients is null)
            {
                failures.Add("A record was dropped: its nutrient data was incomplete.");
                continue;
            }

            try
            {
                translated.Add(new ExternalFoodRecord(
                    new LocalName(upstreamLabel),
                    nutrients,
                    SourceHash.Of(ProviderName, upstreamKey)));
            }
            catch (ArgumentException ex)
            {
                failures.Add($"A record was dropped: {ex.Message}");
            }
        }

        return new ExternalCatalogSnapshot(ProviderName, translated, failures);
    }

    /// <summary>Folds the nutrient list into the four values this platform keeps.</summary>
    private static NutrientsPer100g? ReadNutrients(JsonElement food)
    {
        if (!food.TryGetProperty("foodNutrients", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;

        decimal? energy = null, protein = null, carb = null, fat = null;

        foreach (var nutrient in list.EnumerateArray())
        {
            var number = ReadString(nutrient, "nutrientNumber");
            var value = ReadDecimal(nutrient, "value");
            if (number is null || value is null) continue;

            switch (number)
            {
                case EnergyKcalNutrientNumber:
                    energy ??= value;
                    break;
                case ProteinNutrientNumber:
                    protein ??= value;
                    break;
                case CarbohydrateNutrientNumber:
                    carb ??= value;
                    break;
                case FatNutrientNumber:
                    fat ??= value;
                    break;
            }
        }

        if (energy is null || protein is null || carb is null || fat is null) return null;

        try
        {
            return new NutrientsPer100g(energy.Value, protein.Value, carb.Value, fat.Value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private ExternalCatalogSnapshot Unavailable(string reason)
    {
        return new ExternalCatalogSnapshot(ProviderName, [], [reason]);
    }

    private static string? ReadIdentifier(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static decimal? ReadDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}

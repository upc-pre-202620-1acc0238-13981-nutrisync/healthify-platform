using System.Globalization;
using System.Text.Json;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Services;

namespace Healthify.Platform.FoodCatalog.Infrastructure.External.OpenFoodFacts;

/// <summary>
///     Anti-corruption layer over Open Food Facts.
/// </summary>
/// <remarks>
///     Business rules: No External Id Enters The Domain and Taxonomy Translation Mandatory (Food
///     Catalog, Subflow 6.1). This class is one of the two places in the platform where the upstream
///     vocabulary is allowed to appear. What leaves it is an <see cref="ExternalFoodRecord" />, which
///     has no field an identifier could travel in: the upstream key is consumed here and survives only
///     as the one-way digest that lets a later import recognise the same record.
///     It never throws. A provider that cannot be reached is the ordinary case that the fallback rule
///     of Subflow 6.3 exists for, so the failure is returned as data.
/// </remarks>
public class OpenFoodFactsProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<OpenFoodFactsProvider> logger) : IExternalFoodCatalogProvider
{
    public string ProviderName => "Open Food Facts";

    public async Task<ExternalCatalogSnapshot> FetchSnapshotAsync(string term, int max,
        CancellationToken cancellationToken = default)
    {
        // TODO: hotspot (event storming 6, hotspot 1) - Peruvian market coverage. Open Food Facts is
        // thin on locally prepared dishes, which is most of what a Peruvian patient eats. The country
        // filter below narrows the snapshot to what the provider does know about; the gap is meant to
        // be closed by practitioners through Create Local Override (Subflow 6.4), and who is expected
        // to do that at scale is still an open question.
        var country = configuration["OpenFoodFacts:Country"] ?? "peru";

        var url = "/cgi/search.pl?search_simple=1&action=process&json=1"
                  + $"&search_terms={Uri.EscapeDataString(term)}"
                  + $"&countries_tags_en={Uri.EscapeDataString(country)}"
                  + $"&page_size={Math.Clamp(max, 1, 100)}";

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
            logger.LogWarning(ex, "Open Food Facts could not be consulted for term {Term}", term);
            return Unavailable("The provider could not be reached.");
        }
    }

    private ExternalCatalogSnapshot Translate(JsonDocument document)
    {
        var translated = new List<ExternalFoodRecord>();
        var failures = new List<string>();

        if (!document.RootElement.TryGetProperty("products", out var products)
            || products.ValueKind != JsonValueKind.Array)
            return new ExternalCatalogSnapshot(ProviderName, translated,
                ["The payload carried no product collection."]);

        foreach (var product in products.EnumerateArray())
        {
            // The upstream key is read here, folded into the digest, and never named again.
            var upstreamKey = ReadString(product, "code");
            var upstreamLabel = ReadString(product, "product_name");

            if (string.IsNullOrWhiteSpace(upstreamKey) || string.IsNullOrWhiteSpace(upstreamLabel))
            {
                failures.Add("A record was dropped: it carried no usable name.");
                continue;
            }

            if (!product.TryGetProperty("nutriments", out var nutriments)
                || nutriments.ValueKind != JsonValueKind.Object)
            {
                failures.Add("A record was dropped: it carried no nutrient data.");
                continue;
            }

            var energy = ReadDecimal(nutriments, "energy-kcal_100g");
            var protein = ReadDecimal(nutriments, "proteins_100g");
            var carb = ReadDecimal(nutriments, "carbohydrates_100g");
            var fat = ReadDecimal(nutriments, "fat_100g");

            if (energy is null || protein is null || carb is null || fat is null)
            {
                failures.Add("A record was dropped: its nutrient data was incomplete.");
                continue;
            }

            try
            {
                translated.Add(new ExternalFoodRecord(
                    new LocalName(upstreamLabel),
                    new NutrientsPer100g(energy.Value, protein.Value, carb.Value, fat.Value),
                    SourceHash.Of(ProviderName, upstreamKey)));
            }
            catch (ArgumentException ex)
            {
                failures.Add($"A record was dropped: {ex.Message}");
            }
        }

        return new ExternalCatalogSnapshot(ProviderName, translated, failures);
    }

    private ExternalCatalogSnapshot Unavailable(string reason)
    {
        return new ExternalCatalogSnapshot(ProviderName, [], [reason]);
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

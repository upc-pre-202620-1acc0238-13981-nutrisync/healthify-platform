using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Repositories;

namespace Healthify.Platform.FoodCatalog.Infrastructure.Persistence.EFC.Seeders;

/// <summary>
///     Puts a starting Peruvian catalog into the database so that logging a meal is possible before
///     any external import has ever run.
/// </summary>
/// <remarks>
///     It writes through Cache Food Locally rather than through the repository, so that seeded rows
///     enter the catalog by the same single door as imported ones and are subject to the same checks.
///     Idempotent by construction: each entry carries a stable digest, and Cache Food Locally treats a
///     digest it already holds as a no-op.
///     Every value below is per 100 grams of the food as it is eaten, and they are round figures from
///     public composition tables. They are reference data for development, not a clinical source.
/// </remarks>
public class ReferenceFoodSeeder(
    IReferenceFoodRepository referenceFoodRepository,
    IReferenceFoodCommandService commandService,
    IConfiguration configuration,
    ILogger<ReferenceFoodSeeder> logger)
{
    private const string SeedProvenance = "healthify-seed-peru";

    /// <summary>Local name, energy kcal, protein g, carbohydrate g, fat g. All per 100 g.</summary>
    private static readonly (string Name, decimal Energy, decimal Protein, decimal Carb, decimal Fat)[]
        StartingCatalog =
        [
            ("Arroz blanco cocido", 130m, 2.7m, 28.2m, 0.3m),
            ("Quinua cocida", 120m, 4.4m, 21.3m, 1.9m),
            ("Kiwicha cocida", 102m, 3.8m, 18.7m, 1.6m),
            ("Cañihua cocida", 120m, 4.5m, 21.0m, 2.0m),
            ("Papa amarilla sancochada", 93m, 2.0m, 21.0m, 0.1m),
            ("Papa blanca sancochada", 87m, 1.9m, 20.1m, 0.1m),
            ("Camote amarillo sancochado", 90m, 2.0m, 20.7m, 0.1m),
            ("Yuca sancochada", 160m, 1.4m, 38.1m, 0.3m),
            ("Olluco crudo", 62m, 1.1m, 14.3m, 0.1m),
            ("Oca cruda", 61m, 1.0m, 13.3m, 0.6m),
            ("Choclo desgranado cocido", 108m, 3.2m, 25.1m, 1.3m),
            ("Maíz morado grano seco", 355m, 6.7m, 76.2m, 4.0m),
            ("Frijol canario cocido", 127m, 8.7m, 22.8m, 0.5m),
            ("Pallar cocido", 115m, 7.8m, 20.9m, 0.4m),
            ("Lenteja cocida", 116m, 9.0m, 20.1m, 0.4m),
            ("Garbanzo cocido", 164m, 8.9m, 27.4m, 2.6m),
            ("Tarwi cocido", 151m, 17.3m, 9.6m, 6.5m),
            ("Habas frescas cocidas", 110m, 7.6m, 19.6m, 0.4m),
            ("Arveja verde cocida", 84m, 5.4m, 15.6m, 0.2m),
            ("Pechuga de pollo sin piel cocida", 165m, 31.0m, 0.0m, 3.6m),
            ("Pierna de pollo sin piel cocida", 174m, 24.2m, 0.0m, 8.1m),
            ("Carne de res magra cocida", 205m, 30.0m, 0.0m, 8.5m),
            ("Hígado de res cocido", 175m, 26.5m, 5.1m, 4.9m),
            ("Lomo de cerdo cocido", 190m, 28.0m, 0.0m, 8.0m),
            ("Cuy cocido", 155m, 21.0m, 0.0m, 7.8m),
            ("Carne de alpaca cocida", 145m, 24.5m, 0.0m, 4.8m),
            ("Bonito cocido", 168m, 27.0m, 0.0m, 6.3m),
            ("Jurel cocido", 158m, 26.0m, 0.0m, 5.6m),
            ("Trucha cocida", 148m, 22.9m, 0.0m, 5.8m),
            ("Perico cocido", 109m, 23.7m, 0.0m, 0.9m),
            ("Anchoveta en conserva", 210m, 24.0m, 0.0m, 12.4m),
            ("Langostinos cocidos", 99m, 20.9m, 0.2m, 1.1m),
            ("Conchas de abanico crudas", 88m, 16.8m, 2.4m, 0.8m),
            ("Huevo de gallina sancochado", 155m, 12.6m, 1.1m, 10.6m),
            ("Leche entera de vaca", 61m, 3.2m, 4.8m, 3.3m),
            ("Leche evaporada", 134m, 6.8m, 10.0m, 7.6m),
            ("Yogur natural sin azúcar", 61m, 3.5m, 4.7m, 3.3m),
            ("Queso fresco", 264m, 17.4m, 3.4m, 20.1m),
            ("Palta fuerte", 160m, 2.0m, 8.5m, 14.7m),
            ("Plátano de seda", 89m, 1.1m, 22.8m, 0.3m),
            ("Plátano bellaco sancochado", 116m, 0.8m, 31.2m, 0.2m),
            ("Mango", 60m, 0.8m, 15.0m, 0.4m),
            ("Papaya", 43m, 0.5m, 10.8m, 0.3m),
            ("Piña", 50m, 0.5m, 13.1m, 0.1m),
            ("Lúcuma pulpa", 99m, 1.5m, 25.0m, 0.5m),
            ("Chirimoya", 75m, 1.6m, 17.7m, 0.7m),
            ("Camu camu pulpa", 24m, 0.5m, 5.9m, 0.1m),
            ("Aguaymanto", 49m, 1.9m, 11.2m, 0.7m),
            ("Maracuyá pulpa", 97m, 2.2m, 23.4m, 0.7m),
            ("Granadilla", 92m, 2.2m, 22.4m, 0.7m),
            ("Tomate", 18m, 0.9m, 3.9m, 0.2m),
            ("Cebolla roja", 40m, 1.1m, 9.3m, 0.1m),
            ("Zapallo macre cocido", 26m, 1.0m, 6.5m, 0.1m),
            ("Zanahoria cocida", 35m, 0.8m, 8.2m, 0.2m),
            ("Espinaca cocida", 23m, 3.0m, 3.8m, 0.3m),
            ("Brócoli cocido", 35m, 2.4m, 7.2m, 0.4m),
            ("Rocoto fresco", 30m, 1.3m, 6.2m, 0.3m),
            ("Ají amarillo fresco", 40m, 1.5m, 8.8m, 0.4m),
            ("Aceite de oliva", 884m, 0.0m, 0.0m, 100.0m),
            ("Azúcar rubia", 387m, 0.0m, 99.8m, 0.0m)
        ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("Seeder:Enabled", false))
        {
            logger.LogInformation("Reference food seeding is disabled");
            return;
        }

        var minimum = configuration.GetValue("Seeder:ReferenceFoods:MinCount", 50);
        var existing = await referenceFoodRepository.CountAsync(cancellationToken);
        if (existing >= minimum)
        {
            logger.LogInformation(
                "The reference food catalog already holds {Count} entries, at or above the minimum {Minimum}",
                existing, minimum);
            return;
        }

        var seeded = 0;

        foreach (var (name, energy, protein, carb, fat) in StartingCatalog)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var sourceHash = SourceHash.Of(SeedProvenance, name.ToLowerInvariant());

            var result = await commandService.Handle(
                new CacheFoodLocallyCommand(name, energy, protein, carb, fat, sourceHash.Value),
                cancellationToken);

            if (result.IsSuccess) seeded++;
            else logger.LogWarning("The starting catalog entry {LocalName} could not be seeded", name);
        }

        logger.LogInformation("Reference food seeding finished: {Seeded} of {Total} entries accepted",
            seeded, StartingCatalog.Length);
    }
}

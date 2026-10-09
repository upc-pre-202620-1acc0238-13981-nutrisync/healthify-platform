using System.Reflection;
using System.Text.Json;
using Cortex.Mediator;
using Healthify.Platform.FoodCatalog.Application.Acl;
using Healthify.Platform.FoodCatalog.Application.Internal.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.FoodCatalog.Domain.Model.Events;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Services;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.FoodCatalog.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.FoodCatalog;

/// <summary>
///     IN-7. Foods created from the nutrients the AI estimated: coherent or not created, verified automatically,
///     idempotent by the normalized name (also when two requests race), never overwritten by an import, and never
///     exposed as such by any resource or ACL item.
/// </summary>
public class AiEstimatedFoodTests
{
    private const long GenerationId = 77;

    private readonly InMemoryReferenceFoods _catalog = new();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    [Theory]
    [InlineData(150, 10, 12, 7, true)] // 4·10 + 4·12 + 9·7 = 151
    [InlineData(0, 0, 0, 0, true)] // water
    [InlineData(900, 0, 0, 100, true)] // pure fat
    [InlineData(901, 0, 0, 100, false)] // above 900
    [InlineData(400, 40, 40, 30, false)] // 110 g of macronutrients in 100 g
    [InlineData(500, 10, 10, 1, false)] // 89 kcal by Atwater: 411 apart, far beyond max(15 %, 20 kcal)
    [InlineData(100, 10, 10, 1, true)] // 89 vs 100: within 15 %
    // Changed on purpose (IN-7, second pass): the tolerance is now max(15 %, 20 kcal), so 10 vs 18 kcal is accepted.
    [InlineData(10, 0, 0, 2, true)]
    [InlineData(25, 2, 6, 0.2, true)] // a vegetable with much fibre: 33.8 by Atwater, 8.8 apart, within 20 kcal
    [InlineData(10, 0, 0, 10, false)] // 90 kcal of fat declared as 10: impossible
    [InlineData(300, 5, 5, 5, false)] // 85 by Atwater: 215 apart
    [InlineData(-1, 0, 0, 0, false)]
    [InlineData(200, 101, 0, 0, false)]
    public void Nutrients_are_coherent_only_within_the_ranges_and_the_atwater_tolerance(double kcal, double protein,
        double carb, double fat, bool coherent)
    {
        Assert.Equal(coherent, NutrientCoherence.IsCoherent((decimal)kcal, (decimal)protein, (decimal)carb,
            (decimal)fat));
    }

    [Fact]
    public void The_energy_tolerance_is_the_larger_of_the_percent_and_the_kcal_and_both_are_configurable()
    {
        Assert.Equal(20m, NutrientTolerance.Default.AllowedFor(25m)); // 15 % of 25 = 3.75 < 20
        Assert.Equal(75m, NutrientTolerance.Default.AllowedFor(500m)); // 15 % of 500 = 75 > 20

        // Without the fixed kilocalories, the fibrous vegetable is refused again.
        Assert.False(NutrientCoherence.IsCoherent(25m, 2m, 6m, 0.2m, new NutrientTolerance(15m, 0m)));
        Assert.True(NutrientCoherence.IsCoherent(25m, 2m, 6m, 0.2m, new NutrientTolerance(40m, 0m)));
    }

    [Fact]
    public async Task The_command_service_reads_the_tolerance_from_configuration()
    {
        Assert.True((await Service().Handle(new CreateAiEstimatedFoodCommand("Ensalada de espinaca", 25m, 2m, 6m,
            0.2m, GenerationId))).IsSuccess);

        var strict = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FoodCatalog:AiEstimatedNutrients:EnergyTolerancePercent"] = "15",
                ["FoodCatalog:AiEstimatedNutrients:EnergyToleranceKcal"] = "0"
            }).Build();
        var request = _catalog.NewRequest();
        var service = new ReferenceFoodCommandService(request, request, [],
            NullLogger<ReferenceFoodCommandService>.Instance, _mediator, strict);
        Assert.Equal(FoodCatalogError.InconsistentNutrients, Error(await service.Handle(
            new CreateAiEstimatedFoodCommand("Ensalada de acelga", 25m, 2m, 6m, 0.2m, GenerationId))));
    }

    [Theory]
    [InlineData("Lomo saltado de res", "Lomo saltado", true)] // the catalog name is the AI's minus one word
    [InlineData("Lomo saltado", "Lomo saltado de res", true)] // and the other way round
    [InlineData("lomos  saltados", "Lomo saltado", true)] // plural, case and spaces
    [InlineData("Ají de gallina", "Aji de gallina", true)] // accents
    [InlineData("Pollo a la brasa", "Pollo", false)] // one shared word is not a clear match
    [InlineData("Arroz con pollo", "Arroz blanco cocido", false)]
    [InlineData("Lomo saltado con papas fritas", "Lomo saltado", false)] // two words more: another dish
    [InlineData("Seco de cordero", "Seco de res", false)]
    public void A_similar_dish_is_matched_only_when_it_is_clearly_the_same(string name, string catalog, bool matches)
    {
        var food = Identity.Assign(new ReferenceFood(new LocalName(catalog), new NutrientsPer100g(150m, 10m, 12m, 7m),
            SourceHash.Of("seed", catalog)), new ReferenceFoodId(3));

        Assert.Equal(matches, FoodNameMatcher.ClearSimilarMatch(name, [food]) is not null);
    }

    [Fact]
    public void Two_entries_equally_close_are_not_a_clear_match()
    {
        var withBeef = Identity.Assign(new ReferenceFood(new LocalName("Lomo saltado de res"),
            new NutrientsPer100g(150m, 10m, 12m, 7m), SourceHash.Of("seed", "1")), new ReferenceFoodId(1));
        var withChicken = Identity.Assign(new ReferenceFood(new LocalName("Lomo saltado de pollo"),
            new NutrientsPer100g(150m, 10m, 12m, 7m), SourceHash.Of("seed", "2")), new ReferenceFoodId(2));

        Assert.Null(FoodNameMatcher.ClearSimilarMatch("Lomo saltado", [withBeef, withChicken]));
        Assert.Same(withBeef, FoodNameMatcher.ClearSimilarMatch("Lomo saltado de res", [withBeef, withChicken]));
    }

    [Fact]
    public async Task A_dish_the_catalog_already_has_under_a_shorter_name_is_reused_not_created()
    {
        var lomo = _catalog.Seed(new ReferenceFood(new LocalName("Lomo saltado"),
            new NutrientsPer100g(180m, 12m, 10m, 10m), SourceHash.Of("seed", "lomo")));

        var reused = Food(await Create(Service(), "Lomo saltado de res"));
        var created = Food(await Create(Service(), "Carapulcra con sopa seca"));

        Assert.Same(lomo, reused);
        Assert.Equal(2, _catalog.Stored.Count);
        Assert.True(created.Source.IsAiEstimated);
        Assert.Single(Fakes.Published(_mediator).OfType<AiEstimatedFoodCreated>());
    }

    [Fact]
    public void An_ai_estimated_food_is_verified_automatically_and_traced_to_its_generation()
    {
        var food = ReferenceFood.AiEstimated(new LocalName("Lomo saltado"), new NutrientsPer100g(150m, 10m, 12m, 7m),
            GenerationId);

        Assert.Equal(FoodSource.AiEstimated, food.Source.Value);
        Assert.True(food.IsVerified);
        Assert.Null(food.VerifiedBy);
        Assert.Equal(GenerationId, food.AiGenerationId);
        Assert.False(food.IsLocalOverride);
        Assert.Equal(SourceHash.ForAiEstimated("lomo saltado"), food.SourceHash);
        Assert.Throws<InvalidOperationException>(() =>
            food.RefreshFromUpstream(new LocalName("Lomo saltado (import)"), new NutrientsPer100g(1m, 0m, 0m, 0m)));

        Assert.Throws<ArgumentException>(() => ReferenceFood.AiEstimated(new LocalName("Raro"),
            new NutrientsPer100g(500m, 10m, 10m, 1m), GenerationId));
        Assert.Throws<ArgumentException>(() => ReferenceFood.AiEstimated(new LocalName("Lomo saltado"),
            new NutrientsPer100g(150m, 10m, 12m, 7m), 0));
    }

    [Fact]
    public void Existing_foods_are_imported_or_local_overrides_and_verified()
    {
        var imported = new ReferenceFood(new LocalName("Arroz blanco cocido"), new NutrientsPer100g(130m, 2.7m, 28.2m,
            0.3m), SourceHash.Of("seed", "arroz"));
        var local = new ReferenceFood(new CreateLocalOverrideCommand(20, "Cuy al horno", 155m, 21m, 0m, 7.8m));

        Assert.Equal((FoodSource.Imported, true, (int?)null), (imported.Source.Value, imported.IsVerified,
            imported.VerifiedBy));
        Assert.Equal((FoodSource.LocalOverride, true), (local.Source.Value, local.IsVerified));
    }

    [Fact]
    public void The_identity_of_an_ai_food_is_its_name_normalized()
    {
        Assert.Equal("ai:aji de gallina", SourceHash.AiMaterial("  Ají   de GALLINA "));
        Assert.Equal(SourceHash.ForAiEstimated("Ají de gallina"), SourceHash.ForAiEstimated("aji  de gallina"));
        Assert.NotEqual(SourceHash.ForAiEstimated("Ají de gallina"), SourceHash.ForLocalOverride("Ají de gallina"));
    }

    [Fact]
    public async Task Creating_the_same_dish_twice_creates_it_once()
    {
        var first = Food(await Create(Service(), "Ají de gallina"));
        var second = Food(await Create(Service(), "  aji de  GALLINA"));

        Assert.Same(first, second);
        var stored = Assert.Single(_catalog.Stored);
        Assert.Equal("Ají de gallina", stored.LocalNameText);
        Assert.Equal((FoodSource.AiEstimated, true, (int?)null, (long?)GenerationId),
            (stored.Source.Value, stored.IsVerified, stored.VerifiedBy, stored.AiGenerationId));
        Assert.Single(Fakes.Published(_mediator).OfType<AiEstimatedFoodCreated>());
    }

    [Fact]
    public async Task Incoherent_nutrients_create_nothing()
    {
        var result = await Service().Handle(new CreateAiEstimatedFoodCommand("Plato raro", 500m, 10m, 10m, 1m,
            GenerationId));

        Assert.Equal(FoodCatalogError.InconsistentNutrients, Error(result));
        Assert.Empty(_catalog.Stored);
        Assert.Equal(FoodCatalogError.AiGenerationRequired, Error(await Service().Handle(
            new CreateAiEstimatedFoodCommand("Lomo saltado", 150m, 10m, 12m, 7m, 0))));
        Assert.Equal(FoodCatalogError.ExternalIdNotAllowed, Error(await Service().Handle(
            new CreateAiEstimatedFoodCommand("7750001112223", 150m, 10m, 12m, 7m, GenerationId))));
    }

    [Fact]
    public async Task Two_requests_racing_for_the_same_dish_end_with_one_food_and_both_get_it()
    {
        // Both requests read "not there" before either commits; the unique index decides at commit.
        using var barrier = new Barrier(2);
        _catalog.BeforeCommit = () => Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
        var one = _catalog.NewRequest();
        var two = _catalog.NewRequest();

        var results = await Task.WhenAll(
            Task.Run(() => Service(one).Handle(Command("Seco de res"))),
            Task.Run(() => Service(two).Handle(Command("seco de res"))));

        var stored = Assert.Single(_catalog.Stored);
        Assert.All(results, r => Assert.Same(stored, Food(r)));
        Assert.Equal(1, one.Removed + two.Removed); // the losing insert was detached, not saved later
    }

    [Fact]
    public async Task An_import_never_overwrites_an_ai_estimated_food()
    {
        var food = _catalog.Seed(ReferenceFood.AiEstimated(new LocalName("Causa limeña"),
            new NutrientsPer100g(160m, 4m, 22m, 6m), GenerationId));

        var result = await Service().Handle(new CacheFoodLocallyCommand("Causa limeña", 300m, 1m, 1m, 1m,
            food.SourceHash.Value));

        Assert.Same(food, Food(result));
        Assert.Equal(160m, food.EnergyKcalPer100g);
    }

    [Fact]
    public async Task The_facade_creates_or_finds_the_food_and_says_nothing_about_its_origin()
    {
        var request = _catalog.NewRequest();
        var facade = new FoodCatalogContextFacade(new ReferenceFoodQueryService(request), Service(request));

        var item = await facade.CreateAiEstimatedFood("Lomo saltado", new FoodNutrientsItem(150m, 10m, 12m, 7m),
            GenerationId);
        Assert.NotNull(item);
        Assert.Equal(("Lomo saltado", false), (item.LocalName, item.IsLocalOverride));
        Assert.Null(await facade.CreateAiEstimatedFood("Plato raro", new FoodNutrientsItem(500m, 10m, 10m, 1m),
            GenerationId));

        // Afterwards it is a food like any other: the local resolution of FC-2 finds it.
        var resolved = Assert.Single(await facade.ResolveByNames(["lomo saltado"], 5));
        Assert.Equal(item.ReferenceFoodId, resolved.ReferenceFoodId);
    }

    [Fact]
    public async Task Resolving_with_providers_searches_them_then_matches_the_topped_up_catalog()
    {
        var request = _catalog.NewRequest();
        var provider = Substitute.For<IExternalFoodCatalogProvider>();
        provider.ProviderName.Returns("Usda");
        provider.FetchSnapshotAsync("Pan con chicharrón", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ExternalCatalogSnapshot("Usda", [], []));
        // The provider answered; the cache policy (Subflow 6.2) stored the translated record meanwhile.
        _catalog.Seed(new ReferenceFood(new LocalName("Pan con chicharrón"), new NutrientsPer100g(280m, 12m, 30m, 12m),
            SourceHash.Of("usda", "123")));
        var service = Service(request, provider);

        var result = await service.Handle(new ResolveFoodWithProvidersCommand("Pan con chicharrón"));

        var resolution = Assert.IsType<Result<Healthify.Platform.FoodCatalog.Application.Internal.FoodNameResolution,
            FoodCatalogError>.Success>(result).Value;
        Assert.Equal("Pan con chicharrón", resolution.ReferenceFood!.LocalNameText);
    }

    [Fact]
    public void No_resource_nor_acl_item_exposes_the_source_or_the_verification()
    {
        var hidden = new[] { "Source", "IsVerified", "VerifiedBy", "FoodSource" };
        var assembly = typeof(Program).Assembly;
        var published = assembly.GetTypes().Where(t => t.Namespace is { } ns &&
                                                       (ns.EndsWith(".Interfaces.REST.Resources") ||
                                                        ns.EndsWith(".Interfaces.Acl")))
            .ToList();
        Assert.NotEmpty(published);

        // No published type anywhere carries the catalog's traceability.
        foreach (var type in published)
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                p => p.PropertyType == typeof(FoodSource) || p.Name is "IsVerified" or "VerifiedBy");

        // And the resources a food travels in carry no property named after it at all.
        string[] foodShaped =
        [
            "ReferenceFoodResource", "ReferenceFoodItem", "ResolvedFoodItem", "DiaryEntryResource",
            "MealPhotoAnalysisResource", "MealPhotoAlternativeResource", "PatientMonitoringPanelResource",
            "MealIdeaResource", "MealIdeaIngredientResource"
        ];
        // (The panel's clinical weight points have a Source of their own, ClinicalMeasurement: not a food.)
        foreach (var type in published.Where(t => foodShaped.Contains(t.Name)))
            Assert.DoesNotContain(PropertiesDeep(type), p => hidden.Contains(p.Name) &&
                                                            p.DeclaringType?.Name != "RecordAnthropometryPointResource");

        // An AI-estimated food serialized as the API serializes it.
        var food = ReferenceFood.AiEstimated(new LocalName("Lomo saltado"), new NutrientsPer100g(150m, 10m, 12m, 7m),
            GenerationId);
        Identity.Assign(food, new ReferenceFoodId(5));
        var json = JsonSerializer.Serialize(ReferenceFoodResourceAssembler.ToResource(food),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("source", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verified", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AiEstimated", json, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<PropertyInfo> PropertiesDeep(Type type, HashSet<Type>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(type) || type.Assembly != typeof(Program).Assembly) yield break;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return property;
            var inner = property.PropertyType.IsGenericType
                ? property.PropertyType.GetGenericArguments()
                : [property.PropertyType];
            foreach (var nested in inner.SelectMany(t => PropertiesDeep(t, seen))) yield return nested;
        }
    }

    private ReferenceFoodCommandService Service(InMemoryReferenceFoods.Request? request = null,
        params IExternalFoodCatalogProvider[] providers)
    {
        request ??= _catalog.NewRequest();
        return new ReferenceFoodCommandService(request, request, providers,
            NullLogger<ReferenceFoodCommandService>.Instance, _mediator);
    }

    private static Task<Result<ReferenceFood, FoodCatalogError>> Create(ReferenceFoodCommandService service,
        string name)
    {
        return service.Handle(Command(name));
    }

    private static CreateAiEstimatedFoodCommand Command(string name)
    {
        return new CreateAiEstimatedFoodCommand(name, 150m, 10m, 12m, 7m, GenerationId);
    }

    private static ReferenceFood Food(Result<ReferenceFood, FoodCatalogError> result)
    {
        return Assert.IsType<Result<ReferenceFood, FoodCatalogError>.Success>(result).Value;
    }

    private static FoodCatalogError Error(Result<ReferenceFood, FoodCatalogError> result)
    {
        return Assert.IsType<Result<ReferenceFood, FoodCatalogError>.Failure>(result).Error;
    }
}

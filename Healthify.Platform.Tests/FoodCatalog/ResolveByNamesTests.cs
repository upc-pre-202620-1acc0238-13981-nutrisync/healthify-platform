using Healthify.Platform.FoodCatalog.Application.Acl;
using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.QueryServices;
using Healthify.Platform.FoodCatalog.Application.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Queries;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Repositories;
using Healthify.Platform.FoodCatalog.Domain.Services;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.FoodCatalog;

/// <summary>
///     FC-2. <c>IFoodCatalogContextFacade.ResolveByNames</c>: free ingredient names resolved against the local
///     catalog only, every significant word matching, nutrients of the catalog.
/// </summary>
public class ResolveByNamesTests
{
    private static readonly List<ReferenceFood> Catalog =
    [
        Food(1, "Pechuga de pollo sin piel cocida", 165m, 31m, 0m, 3.6m),
        Food(2, "Pierna de pollo sin piel cocida", 174m, 24.2m, 0m, 8.1m),
        Food(3, "Camote amarillo sancochado", 90m, 2m, 20.7m, 0.1m),
        Food(4, "Langostinos cocidos", 99m, 20.9m, 0.2m, 1.1m),
        Food(5, "Lúcuma pulpa", 99m, 1.5m, 25m, 0.5m),
        Food(6, "Arroz blanco cocido", 130m, 2.7m, 28.2m, 0.3m),
        Food(7, "Arroz", 360m, 7m, 79m, 0.6m)
    ];

    [Theory]
    [InlineData("Pechuga de pollo", 1)]
    [InlineData("pechuga de POLLO", 1)]
    [InlineData("Camote", 3)]
    [InlineData("camotes", 3)]
    [InlineData("Langostino", 4)]
    [InlineData("lucuma", 5)]
    [InlineData("Arroz", 7)]
    [InlineData("Arroz blanco", 6)]
    public void A_name_resolves_to_the_closest_entry_whose_words_cover_it(string name, int expectedId)
    {
        Assert.Equal(expectedId, FoodNameMatcher.BestMatch(name, Catalog)?.Id.Value);
    }

    [Theory]
    [InlineData("Pollo frito")]
    [InlineData("Quinua")]
    [InlineData("de la con")]
    [InlineData("")]
    public void A_name_that_matches_no_entry_on_every_word_stays_unresolved(string name)
    {
        Assert.Null(FoodNameMatcher.BestMatch(name, Catalog));
    }

    [Fact]
    public void A_local_override_wins_between_two_otherwise_equal_entries()
    {
        var generic = Food(10, "Cuy al horno", 200m, 20m, 0m, 12m);
        var local = Identity.Assign(new ReferenceFood(new CreateLocalOverrideCommand(1, "Cuy al horno", 210m, 21m,
            0m, 13m)), new ReferenceFoodId(11));

        Assert.Equal(11, FoodNameMatcher.BestMatch("cuy al horno", [generic, local])?.Id.Value);
    }

    [Fact]
    public async Task The_query_searches_the_local_repository_once_per_distinct_name_and_keeps_the_order()
    {
        var repository = RepositoryOver(Catalog);
        var service = new ReferenceFoodQueryService(repository);

        var resolved = await service.Handle(new ResolveReferenceFoodsByNamesQuery(
            ["Camote", "Pollo frito", "camote", "Pechuga de pollo"], 10));

        Assert.Equal(["Camote", "Pollo frito", "camote", "Pechuga de pollo"], resolved.Select(r => r.Name));
        Assert.Equal([3, (int?)null, 3, 1], resolved.Select(r => r.ReferenceFood?.Id.Value));
        await repository.Received(3).SearchByLocalNameAsync(Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Names_beyond_the_maximum_come_back_unresolved()
    {
        var service = new ReferenceFoodQueryService(RepositoryOver(Catalog));

        var resolved = await service.Handle(new ResolveReferenceFoodsByNamesQuery(["Camote", "Langostino"], 1));

        Assert.Equal(3, resolved[0].ReferenceFood?.Id.Value);
        Assert.Null(resolved[1].ReferenceFood);
    }

    [Fact]
    public async Task The_facade_returns_the_catalog_nutrients_per_100_g_of_each_resolved_name()
    {
        var facade = new FoodCatalogContextFacade(new ReferenceFoodQueryService(RepositoryOver(Catalog)),
            Substitute.For<IReferenceFoodCommandService>());

        var items = await facade.ResolveByNames(["Pechuga de pollo", "Pollo frito"], 10);

        Assert.True(items[0].IsResolved);
        Assert.Equal(1, items[0].ReferenceFoodId);
        Assert.Equal(165m, items[0].EnergyKcalPer100g);
        Assert.Equal(31m, items[0].ProteinGPer100g);
        Assert.Equal("Pechuga de pollo sin piel cocida", items[0].LocalName);
        Assert.False(items[1].IsResolved);
        Assert.Null(items[1].EnergyKcalPer100g);
    }

    [Fact]
    public async Task The_facade_degrades_to_an_empty_list_when_the_lookup_fails()
    {
        var queryService = Substitute.For<IReferenceFoodQueryService>();
        queryService.Handle(Arg.Any<ResolveReferenceFoodsByNamesQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Empty(await new FoodCatalogContextFacade(queryService, Substitute.For<IReferenceFoodCommandService>()).ResolveByNames(["Camote"], 5));
    }

    /// <summary>A repository whose search behaves like the SQL one: a case-insensitive "contains" on the local name.</summary>
    private static IReferenceFoodRepository RepositoryOver(IReadOnlyList<ReferenceFood> foods)
    {
        var repository = Substitute.For<IReferenceFoodRepository>();
        repository.SearchByLocalNameAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => foods.Where(f => FoodNameMatcher.Normalize(f.LocalNameText)
                    .Contains(FoodNameMatcher.Normalize(call.ArgAt<string>(0)), StringComparison.Ordinal))
                .Take(call.ArgAt<int>(1)).AsEnumerable());
        return repository;
    }

    private static ReferenceFood Food(int id, string name, decimal kcal, decimal protein, decimal carb, decimal fat)
    {
        return Identity.Assign(new ReferenceFood(new LocalName(name), new NutrientsPer100g(kcal, protein, carb, fat),
            SourceHash.ForLocalOverride($"test-{id}")), new ReferenceFoodId(id));
    }
}

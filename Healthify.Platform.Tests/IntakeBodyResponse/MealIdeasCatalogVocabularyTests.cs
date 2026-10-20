using System.Text.Json;
using Cortex.Mediator;
using Healthify.Platform.FoodCatalog.Application.Acl;
using Healthify.Platform.FoodCatalog.Application.Internal.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Caching;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Lexicon;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;
using Healthify.Platform.Shared.Infrastructure.Ai.Fakes;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IA-3, «¿No sabes qué comer? · Ideas con IA» answering 502. The model named ingredients the local catalog does not
///     carry («Pan integral», «Avena», «Pechuga de pollo a la plancha»), every idea was discarded by Unresolved
///     Ingredients Stay Minor, the retry did the same and the answer was <see cref="AiError.AiOutputRejected" />.
///     Since <c>meal-ideas@2</c> the model receives the catalog names (the platform list of IN-7) and uses them. Real
///     Food Catalog in memory (FC-2 matcher), real shared pipeline, fake model.
/// </summary>
/// <remarks>The day: 1800 kcal and 100 g of protein, nothing eaten yet, so all of it is left. No restrictions.</remarks>
public class MealIdeasCatalogVocabularyTests
{
    private const int PatientId = 10;
    private static readonly DateOnly Today = new(2026, 9, 15);

    private readonly InMemoryReferenceFoods _foods = new();
    private readonly IActiveTargetsCacheRepository _targets = Substitute.For<IActiveTargetsCacheRepository>();
    private readonly IDiaryEntryRepository _diary = Substitute.For<IDiaryEntryRepository>();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly IAiConsentPolicy _consent = Substitute.For<IAiConsentPolicy>();
    private readonly FakeLanguageModelClient _model = new();
    private readonly InMemoryAiGenerationLog _log = new();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, string?> _configuration = new() { ["Ai:Enabled"] = "true" };
    private readonly InMemoryMealIdeasCache _ideasCache;
    private readonly InMemoryCatalogNameHintsCache _hintsCache;
    private readonly ReferenceFood _pollo;

    public MealIdeasCatalogVocabularyTests()
    {
        _ideasCache = new InMemoryMealIdeasCache(_clock);
        _hintsCache = new InMemoryCatalogNameHintsCache(_clock);
        _consent.IsAllowedAsync(PatientId, AiFeature.MealIdeas, Arg.Any<CancellationToken>()).Returns(true);
        _iam.GetPreferredLanguage(PatientId, Arg.Any<CancellationToken>()).Returns("es");
        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(new ActiveTargetsCache(
            new RefreshActiveTargetsCacheCommand(PatientId, 2, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                1800m, 100m, 200m, 60m, [], [], [])));
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>()).Returns([]);

        // Part of the starting catalog (ReferenceFoodSeeder), as the server has it.
        _pollo = Seed("Pechuga de pollo sin piel cocida", 165m, 31m, 0m, 3.6m);
        Seed("Quinua cocida", 120m, 4.4m, 21.3m, 1.9m);
        Seed("Arroz blanco cocido", 130m, 2.7m, 28.2m, 0.3m);
        Seed("Huevo de gallina sancochado", 155m, 12.6m, 1.1m, 10.6m);
        Seed("Espinaca cocida", 23m, 3m, 3.8m, 0.3m);
        Seed("Tomate", 18m, 0.9m, 3.9m, 0.2m);
        Seed("Aceite de oliva", 884m, 0m, 0m, 100m);
        Seed("Papa amarilla sancochada", 93m, 2m, 21m, 0.1m);
    }

    // What a model without the list answers: generic names, most of them outside the catalog.
    private static readonly object[] IdeasWithItsOwnNames =
    [
        Idea("Pollo a la plancha con quinua", 520m, 48m, 45m, 14m, ("Pechuga de pollo a la plancha", 150m),
            ("Quinua cocida", 150m), ("Lechuga", 60m), ("Limón", 10m)),
        Idea("Tortilla de espinaca con pan integral", 430m, 25m, 35m, 20m, ("Huevo", 120m), ("Espinaca", 80m),
            ("Pan integral", 70m)),
        Idea("Bowl de atún con arroz integral", 540m, 35m, 55m, 18m, ("Atún en agua", 120m),
            ("Arroz integral cocido", 150m), ("Tomate", 60m)),
        Idea("Avena con fruta", 380m, 15m, 65m, 7m, ("Avena", 50m), ("Fresa", 100m))
    ];

    // The same ideas when the model takes the names of catalogIngredientNames (and its own only for lemon and salt).
    private static readonly object[] IdeasWithCatalogNames =
    [
        Idea("Pollo con quinua y tomate", 480m, 52m, 39m, 9m, ("Pechuga de pollo sin piel cocida", 150m),
            ("Quinua cocida", 180m), ("Tomate", 100m), ("Aceite de oliva", 5m), ("Limón", 10m)),
        Idea("Huevos con espinaca y papa", 440m, 24m, 45m, 18m, ("Huevo de gallina sancochado", 120m),
            ("Espinaca cocida", 100m), ("Papa amarilla sancochada", 200m), ("Aceite de oliva", 5m), ("Sal", 1m)),
        Idea("Arroz con pollo sencillo", 560m, 50m, 57m, 12m, ("Pechuga de pollo sin piel cocida", 140m),
            ("Arroz blanco cocido", 200m), ("Aceite de oliva", 5m))
    ];

    [Fact]
    public async Task Ingredients_the_catalog_does_not_carry_discard_every_idea_and_end_in_ai_output_rejected()
    {
        _model.Answers(Answer(IdeasWithItsOwnNames)).Answers(Answer(IdeasWithItsOwnNames));

        var result = await Service().Handle(Generate());

        Assert.Equal(AiError.AiOutputRejected, Failure(result).AiError);
        Assert.Equal(MealIdeasCommandService.MaximumAttempts, _model.Requests.Count);
    }

    [Fact]
    public async Task The_model_receives_the_catalog_names_and_its_ideas_with_them_resolve_and_are_shown()
    {
        _diary.ListMostLoggedReferenceFoodIdsAsync(MealPhotoAnalysisCommandService.MinimumPatientsPerHint,
                MealPhotoAnalysisCommandService.DefaultCatalogNameHints, Arg.Any<CancellationToken>())
            .Returns([_pollo.Id.Value]);
        _model.Answers(Answer(IdeasWithCatalogNames));

        var view = Success(await Service().Handle(Generate()));

        var request = Assert.Single(_model.Requests);
        var names = JsonDocument.Parse(request.UserContent).RootElement.GetProperty("catalogIngredientNames")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        // The most logged first, then the verified foods by name.
        Assert.Equal("Pechuga de pollo sin piel cocida", names[0]);
        Assert.Equal(_foods.Stored.Count, names.Count);
        Assert.Contains("must be named **exactly** as one", request.SystemInstruction);
        Assert.Equal("meal-ideas@2", Assert.Single(_log.Rows).PromptVersion);

        Assert.Equal(3, view.Ideas.Count);
        Assert.All(view.Ideas.SelectMany(i => i.Ingredients).Where(i => i.Name is not ("Limón" or "Sal")),
            i => Assert.True(i.IsResolved));
        Assert.Equal(_pollo.Id.Value, view.Ideas[0].Ingredients[0].ReferenceFoodId);
        // A statistic of the whole platform (foods logged by at least 3 patients), never a patient's own foods.
        await _diary.Received(1).ListMostLoggedReferenceFoodIdsAsync(
            MealPhotoAnalysisCommandService.MinimumPatientsPerHint,
            MealPhotoAnalysisCommandService.DefaultCatalogNameHints, Arg.Any<CancellationToken>());
        Assert.DoesNotContain($"\"{PatientId}\"", request.UserContent);
    }

    [Fact]
    public async Task The_list_is_the_same_cached_list_as_the_meal_photo_one()
    {
        _hintsCache.Set(["Quinua cocida", "Tomate"], TimeSpan.FromHours(1));
        _model.Answers(Answer(IdeasWithCatalogNames));

        Success(await Service().Handle(Generate()));

        Assert.Equal(["Quinua cocida", "Tomate"],
            JsonDocument.Parse(_model.Requests[0].UserContent).RootElement.GetProperty("catalogIngredientNames")
                .EnumerateArray().Select(e => e.GetString()));
        await _diary.DidNotReceiveWithAnyArgs().ListMostLoggedReferenceFoodIdsAsync(default, default, default);
    }

    [Fact]
    public async Task The_list_is_read_only_after_the_gates_and_what_is_left_and_only_when_generating()
    {
        // Kill switch.
        _configuration["Ai:Enabled"] = "false";
        Assert.Equal(AiError.AiFeatureDisabled, Failure(await Service().Handle(Generate())).AiError);
        _configuration["Ai:Enabled"] = "true";

        // Consent or preference.
        _consent.IsAllowedAsync(PatientId, AiFeature.MealIdeas, Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(AiError.AiConsentRequired, Failure(await Service().Handle(Generate())).AiError);
        _consent.IsAllowedAsync(PatientId, AiFeature.MealIdeas, Arg.Any<CancellationToken>()).Returns(true);

        // No published targets.
        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((ActiveTargetsCache?)null);
        Assert.Equal(IntakeError.ActiveTargetsCacheNotFound, Failure(await Service().Handle(Generate())).Error);

        await _diary.DidNotReceiveWithAnyArgs().ListMostLoggedReferenceFoodIdsAsync(default, default, default);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Without_the_list_the_ideas_are_still_generated_as_before()
    {
        _diary.ListMostLoggedReferenceFoodIdsAsync(default, default, default)
            .ReturnsForAnyArgs<IReadOnlyList<int>>(_ => throw new InvalidOperationException("database down"));
        _configuration["FoodCatalog:PromptCatalogNamesCount"] = "0";
        _model.Answers(Answer(IdeasWithCatalogNames));

        var view = Success(await Service().Handle(Generate()));

        Assert.Equal(3, view.Ideas.Count);
        Assert.Equal(0, JsonDocument.Parse(_model.Requests[0].UserContent).RootElement
            .GetProperty("catalogIngredientNames").GetArrayLength());
    }

    private MealIdeasCommandService Service()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build();
        var settings = new ConfiguredAiSettings(configuration, NullLogger<ConfiguredAiSettings>.Instance);
        var pipeline = new AiGenerationPipeline(settings, _consent,
            PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly), _model, _log, _clock,
            NullLogger<AiGenerationPipeline>.Instance);
        var request = _foods.NewRequest();
        var foodCommands = new ReferenceFoodCommandService(request, request, [],
            NullLogger<ReferenceFoodCommandService>.Instance, Substitute.For<IMediator>(), configuration);
        var catalog = new FoodCatalogContextFacade(new ReferenceFoodQueryService(request), foodCommands);
        return new MealIdeasCommandService(_targets, _diary, catalog, _iam, pipeline, settings, _consent,
            EmbeddedRestrictionLexicon.Instance, _ideasCache, _clock, NullLogger<MealIdeasCommandService>.Instance,
            _hintsCache, configuration);
    }

    private ReferenceFood Seed(string name, decimal kcal, decimal protein, decimal carb, decimal fat)
    {
        return _foods.Seed(new ReferenceFood(new LocalName(name), new NutrientsPer100g(kcal, protein, carb, fat),
            SourceHash.Of("seed", name)));
    }

    private static GenerateMealIdeasCommand Generate()
    {
        return new GenerateMealIdeasCommand(PatientId, Today, null);
    }

    private static object Idea(string name, decimal kcal, decimal protein, decimal carb, decimal fat,
        params (string Name, decimal Grams)[] ingredients)
    {
        return new
        {
            name, energyKcal = kcal, proteinG = protein, carbG = carb, fatG = fat,
            ingredients = ingredients.Select(i => new { name = i.Name, grams = i.Grams }).ToList(),
            why = "Cabe en lo que te queda hoy y suma proteína."
        };
    }

    private static string Answer(object[] ideas)
    {
        return JsonSerializer.Serialize(new { ideas });
    }

    private static MealIdeasView Success(Result<MealIdeasView, IntakeAiFailure> result)
    {
        return Assert.IsType<Result<MealIdeasView, IntakeAiFailure>.Success>(result).Value;
    }

    private static IntakeAiFailure Failure(Result<MealIdeasView, IntakeAiFailure> result)
    {
        return Assert.IsType<Result<MealIdeasView, IntakeAiFailure>.Failure>(result).Error;
    }
}

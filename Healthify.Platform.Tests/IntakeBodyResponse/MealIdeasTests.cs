using System.Text.Json;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Caching;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Lexicon;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
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
///     IA-3. Meal ideas: what is left computed by the server, the catalog recalculating the figures (FC-2), the
///     restrictions of the plan through the lexicon, «cabe en lo que te queda», one retry, the two-hour cache and its
///     purge. The real shared pipeline with the fake model.
/// </summary>
/// <remarks>
///     The day: targets 1800 kcal and 100 g of protein, 1210 kcal and 52 g already confirmed, so 590 kcal and 48 g
///     are left (PT14.4). The plan is ShellfishFree.
/// </remarks>
public class MealIdeasTests
{
    private const int PatientId = 10;
    private static readonly DateOnly Today = new(2026, 9, 15);

    private readonly IActiveTargetsCacheRepository _targets = Substitute.For<IActiveTargetsCacheRepository>();
    private readonly IDiaryEntryRepository _diary = Substitute.For<IDiaryEntryRepository>();
    private readonly IFoodCatalogContextFacade _catalog = Substitute.For<IFoodCatalogContextFacade>();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly IAiConsentPolicy _consent = Substitute.For<IAiConsentPolicy>();
    private readonly FakeLanguageModelClient _model = new();
    private readonly InMemoryAiGenerationLog _log = new();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, string?> _configuration = new() { ["Ai:Enabled"] = "true" };
    private readonly InMemoryMealIdeasCache _cache;

    private static readonly Dictionary<string, ReferenceFoodItem> Foods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pechuga de pollo"] = new(231, "Pechuga de pollo sin piel cocida", 165m, 31m, 0m, 3.6m, false),
        ["Camote"] = new(3, "Camote amarillo sancochado", 90m, 2m, 20.7m, 0.1m, false),
        ["Langostinos"] = new(4, "Langostinos cocidos", 99m, 20.9m, 0.2m, 1.1m, false),
        ["Arroz blanco"] = new(6, "Arroz blanco cocido", 130m, 2.7m, 28.2m, 0.3m, false),
        ["Aceite de oliva"] = new(8, "Aceite de oliva", 884m, 0m, 0m, 100m, false),
        ["Lechuga"] = new(9, "Lechuga", 15m, 1.4m, 2.9m, 0.2m, false)
    };

    public MealIdeasTests()
    {
        _cache = new InMemoryMealIdeasCache(_clock);
        _consent.IsAllowedAsync(PatientId, AiFeature.MealIdeas, Arg.Any<CancellationToken>()).Returns(true);
        _iam.GetPreferredLanguage(PatientId, Arg.Any<CancellationToken>()).Returns("es");

        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(Targets(["ShellfishFree"]));

        // 1000 g of a dish of 121 kcal / 5.2 g protein per 100 g: 1210 kcal and 52 g confirmed today.
        _catalog.GetReferenceFoodById(1, Arg.Any<CancellationToken>())
            .Returns(new ReferenceFoodItem(1, "Arroz con pollo", 121m, 5.2m, 15m, 4m, false));
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>()).Returns(_ => [Eaten(1000m)]);

        _catalog.ResolveByNames(Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<ResolvedFoodItem>)call.ArgAt<IReadOnlyList<string>>(0)
                .Select(n => Foods.TryGetValue(n, out var f)
                    ? new ResolvedFoodItem(n, f.ReferenceFoodId, f.EnergyKcalPer100g, f.ProteinGPer100g,
                        f.CarbGPer100g, f.FatGPer100g, f.LocalName)
                    : new ResolvedFoodItem(n, null, null))
                .ToList());
    }

    // The ideas of one answer. Kcal and macros are what the model declares.
    private static readonly object PolloConCamote = Idea("Pollo al horno con camote", 420m, 50m, 28m, 11m,
        ("Pechuga de pollo", 150m), ("Camote", 120m), ("Lechuga", 100m), ("Aceite de oliva", 5m));

    private static readonly object ArrozConLangostinos = Idea("Arroz con langostinos", 480m, 30m, 60m, 8m,
        ("Langostinos", 150m), ("Arroz blanco", 200m));

    // Declares 760 kcal: above what is left, and the palta, not in the catalog, would bring a quarter of it.
    private static readonly object PolloConArrozYPalta = Idea("Pollo con arroz y palta", 760m, 45m, 70m, 30m,
        ("Pechuga de pollo", 150m), ("Arroz blanco", 250m), ("Palta", 100m));

    // Declares 450 kcal, but the catalog adds up 720 (200 g of chicken, 300 g of rice): more than 15 % apart.
    private static readonly object ArrozConPolloGrande = Idea("Arroz con pollo casero", 450m, 40m, 60m, 8m,
        ("Pechuga de pollo", 200m), ("Arroz blanco", 300m));

    // Declares 230 kcal, the catalog adds up 265: more than 15 % apart, so the catalog's figures are shown.
    private static readonly object EnsaladaDePollo = Idea("Ensalada de pollo", 230m, 35m, 4m, 9m,
        ("Pechuga de pollo", 120m), ("Lechuga", 150m), ("Aceite de oliva", 5m));

    [Fact]
    public async Task An_idea_with_shellfish_is_discarded_when_the_plan_is_shellfish_free()
    {
        _model.Answers(Answer(PolloConCamote, ArrozConLangostinos, EnsaladaDePollo));

        var view = Success(await Service().Handle(Generate()));

        Assert.DoesNotContain(view.Ideas, i => i.Name == "Arroz con langostinos");
        Assert.Equal(["Pollo al horno con camote", "Ensalada de pollo"], view.Ideas.Select(i => i.Name));
        Assert.Equal(["ShellfishFree"], view.Restrictions);
    }

    [Fact]
    public async Task An_idea_above_what_is_left_is_discarded_also_when_the_catalog_reveals_it()
    {
        _model.Answers(Answer(PolloConCamote, PolloConArrozYPalta, ArrozConPolloGrande, EnsaladaDePollo));

        var view = Success(await Service().Handle(Generate()));

        Assert.Equal(590m, view.Remaining.EnergyKcal);
        Assert.Equal(48m, view.Remaining.ProteinG);
        // 760 kcal declared, and 450 declared but 720 by the catalog: neither fits the 590 left.
        Assert.Equal(["Pollo al horno con camote", "Ensalada de pollo"], view.Ideas.Select(i => i.Name));
        Assert.All(view.Ideas, i => Assert.True(i.EnergyKcal <= view.Remaining.EnergyKcal));
    }

    [Fact]
    public async Task The_catalog_figures_replace_the_models_only_when_they_differ_by_more_than_15_percent()
    {
        _model.Answers(Answer(PolloConCamote, EnsaladaDePollo));

        var view = Success(await Service().Handle(Generate()));

        // 420 declared, 415 by the catalog: the model's figures stay.
        var pollo = view.Ideas.Single(i => i.Name == "Pollo al horno con camote");
        Assert.False(pollo.NutrientsFromCatalog);
        Assert.Equal(420m, pollo.EnergyKcal);
        // 230 declared, 265 by the catalog: the catalog's figures are shown.
        var ensalada = view.Ideas.Single(i => i.Name == "Ensalada de pollo");
        Assert.True(ensalada.NutrientsFromCatalog);
        Assert.Equal(265m, ensalada.EnergyKcal);
        Assert.Equal(39.3m, ensalada.ProteinG);
        Assert.Equal(231, ensalada.Ingredients[0].ReferenceFoodId);
        Assert.Equal("Pechuga de pollo sin piel cocida", ensalada.Ingredients[0].CatalogName);
    }

    [Fact]
    public async Task An_idea_whose_unresolved_ingredients_are_minor_passes_with_them_marked_unresolved()
    {
        // 150 g of chicken and 120 g of camote are 355.5 kcal in the catalog; garlic and salt, which it does not
        // have, can bring at most 14.5 of the 370 declared (4 %).
        var alAjo = Idea("Pollo con camote al ajo", 370m, 49m, 25m, 6m, ("Pechuga de pollo", 150m),
            ("Camote", 120m), ("Ajo", 5m), ("Sal", 1m));
        _model.Answers(Answer(alAjo, EnsaladaDePollo));

        var view = Success(await Service().Handle(Generate()));

        var idea = view.Ideas.Single(i => i.Name == "Pollo con camote al ajo");
        Assert.Equal(370m, idea.EnergyKcal);
        Assert.False(idea.NutrientsFromCatalog);
        var resource = MealIdeasResourceAssembler.ToResource(view, "disclaimer").Ideas
            .Single(i => i.MealIdeaId == idea.MealIdeaId);
        Assert.Equal([true, true, false, false], resource.Ingredients.Select(i => i.Resolved));
        Assert.All(resource.Ingredients.Where(i => !i.Resolved), i => Assert.Null(i.ReferenceFoodId));
        Assert.Equal(231, resource.Ingredients[0].ReferenceFoodId);
    }

    [Fact]
    public async Task An_idea_whose_unresolved_ingredients_bring_more_than_15_percent_of_its_energy_is_discarded()
    {
        // 120 g of chicken are 198 kcal in the catalog; the quinoa it does not have would bring 182 of the 380
        // declared (48 %): whether the idea fits what is left cannot be verified.
        var conQuinua = Idea("Pollo con quinua", 380m, 45m, 30m, 8m, ("Pechuga de pollo", 120m), ("Quinua", 150m));
        _model.Answers(Answer(PolloConCamote, conQuinua, EnsaladaDePollo));

        var view = Success(await Service().Handle(Generate()));

        Assert.Equal(["Pollo al horno con camote", "Ensalada de pollo"], view.Ideas.Select(i => i.Name));
    }

    [Theory]
    [InlineData(370, 355.5, true)]
    [InlineData(380, 198, false)]
    [InlineData(400, 340, true)]
    [InlineData(400, 339, false)]
    [InlineData(300, 320, true)]
    [InlineData(0, 0, false)]
    public void Unresolved_ingredients_may_bring_at_most_15_percent_of_the_declared_energy(double declared,
        double resolved, bool minor)
    {
        Assert.Equal(minor, MealIdeaRules.UnresolvedIngredientsAreMinor((decimal)declared, (decimal)resolved));
    }

    [Fact]
    public async Task With_150_kcal_or_less_left_there_are_no_ideas_and_the_model_is_never_called()
    {
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>()).Returns(_ => [Eaten(1370m)]);

        AssertFailure(await Service().Handle(Generate()), IntakeError.NotEnoughRemaining);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public void The_not_enough_remaining_texts_do_not_accuse()
    {
        var english = new System.Resources.ResourceManager("Healthify.Platform.IntakeBodyResponse.Resources.IntakeMessages",
            typeof(Program).Assembly);
        foreach (var culture in new[] { "en", "es" })
        {
            var text = english.GetString("NotEnoughRemaining", new System.Globalization.CultureInfo(culture));
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Empty(EmbeddedRestrictionLexicon.Instance.ForbiddenTermsIn(text!));
        }

        Assert.Contains("Ya cubriste tu energía de hoy",
            english.GetString("NotEnoughRemaining", new System.Globalization.CultureInfo("es")));
    }

    [Fact]
    public async Task With_fewer_than_two_valid_ideas_the_generation_is_retried_once()
    {
        // First answer: only one idea fits. Second answer: two do.
        _model.Answers(Answer(PolloConCamote, ArrozConLangostinos, PolloConArrozYPalta))
            .Answers(Answer(PolloConCamote, EnsaladaDePollo));

        var view = Success(await Service().Handle(Generate()));

        Assert.Equal(2, view.Ideas.Count);
        Assert.Equal(2, _model.Requests.Count);
        Assert.Equal([AiGenerationStatus.Rejected, AiGenerationStatus.Succeeded], _log.Rows.Select(r => r.Status));
        Assert.Equal(2L, view.AiGenerationId);
    }

    [Fact]
    public async Task Two_attempts_without_two_valid_ideas_answer_output_rejected_and_there_is_no_third()
    {
        // The second answer passes the model's own figures but the catalog leaves one idea only.
        _model.Answers(Answer(ArrozConLangostinos, PolloConArrozYPalta))
            .Answers(Answer(PolloConCamote, ArrozConPolloGrande))
            .Answers(Answer(PolloConCamote, EnsaladaDePollo));

        var result = await Service().Handle(Generate());

        Assert.Equal(AiError.AiOutputRejected, AiFailure(result));
        Assert.Equal(2, _model.Requests.Count);
    }

    [Fact]
    public async Task The_input_carries_targets_left_and_restrictions_and_never_a_diagnosis()
    {
        _model.Answers(Answer(PolloConCamote, EnsaladaDePollo));

        await Service().Handle(Generate());

        var input = Assert.Single(_model.Requests).UserContent;
        using var json = JsonDocument.Parse(input);
        Assert.Equal(590m, json.RootElement.GetProperty("remaining").GetProperty("energyKcal").GetDecimal());
        Assert.Equal("ShellfishFree", json.RootElement.GetProperty("restrictions")[0].GetString());
        Assert.Equal("ReduceSalt", json.RootElement.GetProperty("planGuidelines")[0].GetString());
        Assert.DoesNotContain("diagnos", input, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Caminar", input, StringComparison.OrdinalIgnoreCase); // custom guideline stays here
        Assert.DoesNotContain("bmi", input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ideas_are_cached_for_two_hours_and_a_hit_still_needs_consent()
    {
        _model.Answers(Answer(PolloConCamote, EnsaladaDePollo)).Answers(Answer(PolloConCamote, EnsaladaDePollo));

        var first = Success(await Service().Handle(Generate()));
        var second = Success(await Service().Handle(Generate()));
        Assert.Same(first, second);
        Assert.Single(_model.Requests);

        _consent.IsAllowedAsync(PatientId, AiFeature.MealIdeas, Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(AiError.AiConsentRequired, AiFailure(await Service().Handle(Generate())));

        _consent.IsAllowedAsync(PatientId, AiFeature.MealIdeas, Arg.Any<CancellationToken>()).Returns(true);
        _clock.Now = _clock.Now.AddHours(2).AddMinutes(1);
        Success(await Service().Handle(Generate()));
        Assert.Equal(2, _model.Requests.Count);
    }

    [Fact]
    public async Task See_other_ideas_sends_the_names_already_seen_and_drops_them()
    {
        _model.Answers(Answer(PolloConCamote, EnsaladaDePollo))
            .Answers(Answer(PolloConCamote, EnsaladaDePollo, Idea("Tortilla de verduras", 300m, 15m, 20m, 12m,
                ("Lechuga", 100m), ("Camote", 150m))));
        var first = Success(await Service().Handle(Generate()));

        var other = await Service().Handle(Generate([first.Ideas[0].MealIdeaId]));

        Assert.Contains("Pollo al horno con camote", _model.Requests[1].UserContent);
        Assert.Equal(["Ensalada de pollo", "Tortilla de verduras"], Success(other).Ideas.Select(i => i.Name));
    }

    [Fact]
    public async Task Withdrawing_ai_consent_or_turning_meal_ideas_off_purges_the_cache()
    {
        _model.Answers(Answer(PolloConCamote, EnsaladaDePollo)).Answers(Answer(PolloConCamote, EnsaladaDePollo))
            .Answers(Answer(PolloConCamote, EnsaladaDePollo));
        var service = Service();
        var scopes = Fakes.ScopeFactoryWith<IMealIdeasCommandService>(service);

        await service.Handle(Generate());
        await new OnAiProcessingConsentChangedIntakeHandler(scopes,
                NullLogger<OnAiProcessingConsentChangedIntakeHandler>.Instance)
            .Handle(new AiProcessingConsentChanged(PatientId, false, _clock.Now), CancellationToken.None);
        await service.Handle(Generate());
        Assert.Equal(2, _model.Requests.Count);

        await new OnAiPreferencesChangedIntakeHandler(scopes, NullLogger<OnAiPreferencesChangedIntakeHandler>.Instance)
            .Handle(new AiPreferencesChanged(PatientId, true, false, true, _clock.Now), CancellationToken.None);
        await service.Handle(Generate());
        Assert.Equal(3, _model.Requests.Count);
    }

    [Fact]
    public async Task The_gates_and_the_date_come_before_the_diary()
    {
        AssertFailure(await Service().Handle(new GenerateMealIdeasCommand(PatientId, null)),
            IntakeError.InvalidLocalDate);
        AssertFailure(await Service().Handle(new GenerateMealIdeasCommand(PatientId, Today.AddDays(-3))),
            IntakeError.InvalidLocalDate);

        _configuration["Ai:Enabled"] = "false";
        Assert.Equal(AiError.AiFeatureDisabled, AiFailure(await Service().Handle(Generate())));

        _configuration["Ai:Enabled"] = "true";
        _consent.IsAllowedAsync(PatientId, AiFeature.MealIdeas, Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(AiError.AiConsentRequired, AiFailure(await Service().Handle(Generate())));

        await _diary.DidNotReceive().ListByPatientIdAsync(Arg.Any<int>(), Arg.Any<DateOnly?>(),
            Arg.Any<CancellationToken>());
        await _targets.DidNotReceive().FindByPatientIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Without_published_targets_there_are_no_ideas()
    {
        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((ActiveTargetsCache?)null);

        AssertFailure(await Service().Handle(Generate()), IntakeError.ActiveTargetsCacheNotFound);
    }

    [Theory]
    [InlineData("Langostinos al ajo", "ShellfishFree", true)]
    [InlineData("Conchas de abanico a la parmesana", "ShellfishFree", true)]
    [InlineData("Pechuga de pollo", "ShellfishFree", false)]
    [InlineData("Leche de almendras", "LactoseFree", false)]
    [InlineData("Leche evaporada", "LactoseFree", true)]
    [InlineData("Queso fresco", "Vegan", true)]
    [InlineData("Huevo de gallina sancochado", "Vegetarian", false)]
    [InlineData("Pollo a la plancha", "Vegetarian", true)]
    [InlineData("Pan sin gluten", "GlutenFree", false)]
    [InlineData("Tallarines rojos", "GlutenFree", true)]
    [InlineData("Pasta de tomate", "GlutenFree", false)]
    [InlineData("Nueces picadas", "TreeNutFree", true)]
    [InlineData("Chicharrón de cerdo", "Halal", true)]
    public void The_lexicon_maps_ingredients_to_the_restrictions_they_break(string ingredient, string restriction,
        bool breaks)
    {
        var violated = EmbeddedRestrictionLexicon.Instance.ViolatedRestrictions(ingredient, [restriction]);

        Assert.Equal(breaks, violated.Contains(restriction));
    }

    [Fact]
    public void The_lexicon_knows_every_restriction_of_the_closed_list()
    {
        Assert.Equal(
            ["GlutenFree", "Halal", "Kosher", "LactoseFree", "ShellfishFree", "TreeNutFree", "Vegan", "Vegetarian"],
            EmbeddedRestrictionLexicon.Instance.KnownRestrictions.Order());
    }

    [Fact]
    public void A_text_for_the_patient_with_accusation_or_a_diagnosis_is_caught_on_whole_words_only()
    {
        Assert.NotEmpty(EmbeddedRestrictionLexicon.Instance.ForbiddenTermsIn("Ayer te pasaste de calorías"));
        Assert.NotEmpty(EmbeddedRestrictionLexicon.Instance.ForbiddenTermsIn("Ideal para tu sobrepeso"));
        Assert.Empty(EmbeddedRestrictionLexicon.Instance.ForbiddenTermsIn("Con malta y maltodextrina"));
    }

    [Fact]
    public void Remaining_never_goes_below_zero_and_buckets_by_50_kcal()
    {
        var remaining = RemainingTargets.Of(1800m, 100m, 200m, 60m, 1210m, 120m, 150m, 40m);

        Assert.Equal(590m, remaining.EnergyKcal);
        Assert.Equal(0m, remaining.ProteinG);
        Assert.Equal(11, remaining.EnergyBucket);
        Assert.True(remaining.LeavesRoomForAMeal);
        Assert.False(new RemainingTargets(150m, 0m, 0m, 0m).LeavesRoomForAMeal);
    }

    private MealIdeasCommandService Service()
    {
        var settings = new ConfiguredAiSettings(new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build(),
            NullLogger<ConfiguredAiSettings>.Instance);
        var pipeline = new AiGenerationPipeline(settings, _consent,
            PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly), _model, _log, _clock,
            NullLogger<AiGenerationPipeline>.Instance);
        return new MealIdeasCommandService(_targets, _diary, _catalog, _iam, pipeline, settings, _consent,
            EmbeddedRestrictionLexicon.Instance, _cache, _clock, NullLogger<MealIdeasCommandService>.Instance);
    }

    private static GenerateMealIdeasCommand Generate(IReadOnlyList<string>? exclude = null)
    {
        return new GenerateMealIdeasCommand(PatientId, Today, exclude);
    }

    private static ActiveTargetsCache Targets(IReadOnlyList<string> restrictions)
    {
        return new ActiveTargetsCache(new RefreshActiveTargetsCacheCommand(PatientId, 2,
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), 1800m, 100m, 200m, 60m,
            ["ReduceSalt", "Caminar 20 minutos"], restrictions,
            [new CachedGuideline("ReduceSalt", null), new CachedGuideline(null, "Caminar 20 minutos")]));
    }

    private static DiaryEntry Eaten(decimal grams)
    {
        var entry = new DiaryEntry(PatientId,
            new LocalTimestamp(new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.FromHours(-5))),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Synced));
        entry.ConfirmDirectly(new ConfirmedEstimate(1, grams, DateTimeOffset.UtcNow),
            new PlanAdherence(PlanAdherence.InPlan));
        return entry;
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

    private static string Answer(params object[] ideas)
    {
        return JsonSerializer.Serialize(new { ideas });
    }

    private static MealIdeasView Success(Result<MealIdeasView, IntakeAiFailure> result)
    {
        return Assert.IsType<Result<MealIdeasView, IntakeAiFailure>.Success>(result).Value;
    }

    private static void AssertFailure(Result<MealIdeasView, IntakeAiFailure> result, IntakeError expected)
    {
        Assert.Equal(expected, Assert.IsType<Result<MealIdeasView, IntakeAiFailure>.Failure>(result).Error.Error);
    }

    private static AiError? AiFailure(Result<MealIdeasView, IntakeAiFailure> result)
    {
        return Assert.IsType<Result<MealIdeasView, IntakeAiFailure>.Failure>(result).Error.AiError;
    }
}

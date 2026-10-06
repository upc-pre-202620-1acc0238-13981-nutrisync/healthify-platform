using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.Acl;
using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.FoodCatalog.Application.Acl;
using Healthify.Platform.FoodCatalog.Application.Internal.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Caching;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Imaging;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Scheduling;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;
using Healthify.Platform.Shared.Infrastructure.Ai.Fakes;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;
using Healthify.Platform.Shared.Resources;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-7. «Viendo tu foto…»: the photo checked, stripped of metadata and sent alone to the AI (real shared pipeline,
///     fake model); the dish resolved local catalog → providers → created from the estimate (real Food Catalog in
///     memory, with its unique index); the gates before any call; the analysis kept without any image; the purges.
/// </summary>
public class MealPhotoRecognitionTests
{
    private const int PatientId = 10;
    private const long FirstGenerationId = 1;

    private readonly InMemoryReferenceFoods _foods = new();
    private readonly InMemoryMealPhotoAnalyses _analyses = new();
    private readonly FakeLanguageModelClient _model = new();
    private readonly InMemoryAiGenerationLog _log = new();
    private readonly IAiConsentPolicy _consent = Substitute.For<IAiConsentPolicy>();
    private readonly IExternalFoodCatalogProvider _provider = Substitute.For<IExternalFoodCatalogProvider>();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, string?> _configuration = new() { ["Ai:Enabled"] = "true" };
    private readonly CapturingLogger<MealPhotoAnalysisCommandService> _serviceLog = new();
    private readonly CapturingLogger<AiGenerationPipeline> _pipelineLog = new();
    private readonly ReferenceFood _lomo;
    private readonly IDiaryEntryRepository _diary = Substitute.For<IDiaryEntryRepository>();
    private readonly InMemoryCatalogNameHintsCache _hintsCache;

    public MealPhotoRecognitionTests()
    {
        _hintsCache = new InMemoryCatalogNameHintsCache(_clock);
        _consent.IsAllowedAsync(PatientId, AiFeature.MealPhotoRecognition, Arg.Any<CancellationToken>()).Returns(true);
        _provider.ProviderName.Returns("Usda");
        _provider.FetchSnapshotAsync(default!, default, default)
            .ReturnsForAnyArgs(new ExternalCatalogSnapshot("Usda", [], []));

        _lomo = _foods.Seed(new ReferenceFood(new LocalName("Lomo saltado"), new NutrientsPer100g(180m, 12m, 10m, 10m),
            SourceHash.Of("seed", "lomo")));
        _foods.Seed(new ReferenceFood(new LocalName("Arroz blanco cocido"), new NutrientsPer100g(130m, 2.7m, 28.2m,
            0.3m), SourceHash.Of("seed", "arroz")));
    }

    [Fact]
    public async Task A_dish_of_the_catalog_is_resolved_without_creating_anything_nor_asking_the_providers()
    {
        _model.Answers(Answer("Lomo saltado", alternatives: [("Arroz blanco", 150m), ("Tallarín saltado", 300m)]));

        var view = Success(await Service().Handle(Analyze()));

        Assert.Equal((_lomo.Id.Value, "Lomo saltado", 320m, 0.82m),
            (view.ReferenceFoodId, view.FoodName, view.EstimatedGrams, view.Confidence));
        Assert.Equal(2, _foods.Stored.Count); // nothing created
        await _provider.DidNotReceiveWithAnyArgs().FetchSnapshotAsync(default!, default, default);
        // Alternatives: the catalog's when it carries them, otherwise unresolved.
        Assert.Equal([("Arroz blanco cocido", 150m, (int?)_foods.Stored[1].Id.Value), ("Tallarín saltado", 300m, null)],
            view.Alternatives.Select(a => (a.Name, a.Grams, a.ReferenceFoodId)));

        var stored = Assert.Single(_analyses.Stored);
        Assert.Equal((view.AnalysisId, PatientId, FirstGenerationId), (stored.Id, stored.PatientId, stored.AiGenerationId));
        Assert.Equal(_clock.GetUtcNow().AddHours(24), stored.ExpiresAt);
    }

    [Fact]
    public async Task A_dish_only_a_provider_carries_is_cached_through_search_and_resolved_without_ai_nutrients()
    {
        _provider.FetchSnapshotAsync("Pan con chicharrón", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // The cache policy (Subflow 6.2) stores what the provider translated.
                _foods.Seed(new ReferenceFood(new LocalName("Pan con chicharrón"),
                    new NutrientsPer100g(280m, 12m, 30m, 12m), SourceHash.Of("usda", "pan-chicharron")));
                return new ExternalCatalogSnapshot("Usda", [], []);
            });
        _model.Answers(Answer("Pan con chicharrón"));

        var view = Success(await Service().Handle(Analyze()));

        var food = _foods.Stored.Single(f => f.Id.Value == view.ReferenceFoodId);
        Assert.Equal(FoodSource.Imported, food.Source.Value);
        Assert.DoesNotContain(_foods.Stored, f => f.Source.IsAiEstimated);
    }

    [Fact]
    public async Task A_dish_nowhere_is_created_from_the_estimate_verified_automatically_and_only_once()
    {
        _model.Answers(Answer("Ají de gallina", kcal: 150m, protein: 10m, carb: 12m, fat: 7m))
            .Answers(Answer("aji de  gallina"));

        var first = Success(await Service().Handle(Analyze()));
        var second = Success(await Service().Handle(Analyze()));

        var created = Assert.Single(_foods.Stored, f => f.Source.IsAiEstimated);
        Assert.Equal((first.ReferenceFoodId, second.ReferenceFoodId), (created.Id.Value, created.Id.Value));
        Assert.Equal(("Ají de gallina", true, (int?)null, (long?)FirstGenerationId),
            (created.LocalNameText, created.IsVerified, created.VerifiedBy, created.AiGenerationId));
        Assert.Equal((150m, 10m, 12m, 7m),
            (created.EnergyKcalPer100g, created.ProteinGPer100g, created.CarbGPer100g, created.FatGPer100g));
        Assert.Equal("Ají de gallina", first.FoodName);
    }

    [Fact]
    public async Task Lomo_saltado_de_res_uses_the_lomo_saltado_of_the_catalog()
    {
        _model.Answers(Answer("Lomo saltado de res"));

        var view = Success(await Service().Handle(Analyze()));

        Assert.Equal((_lomo.Id.Value, "Lomo saltado"), (view.ReferenceFoodId, view.FoodName));
        Assert.DoesNotContain(_foods.Stored, f => f.Source.IsAiEstimated);
    }

    [Fact]
    public async Task The_model_is_given_the_most_logged_dishes_of_the_catalog_and_nothing_of_the_patient()
    {
        _diary.ListMostLoggedReferenceFoodIdsAsync(MealPhotoAnalysisCommandService.MinimumPatientsPerHint,
                MealPhotoAnalysisCommandService.DefaultCatalogNameHints, Arg.Any<CancellationToken>())
            .Returns([_lomo.Id.Value, 999, _foods.Stored[1].Id.Value]);
        _model.Answers(Answer("Lomo saltado"));

        Success(await Service().Handle(Analyze()));

        var request = Assert.Single(_model.Requests);
        var input = JsonDocument.Parse(request.UserContent).RootElement;
        Assert.Equal(["Lomo saltado", "Arroz blanco cocido"],
            input.GetProperty("catalogDishNames").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("use **exactly** that name", request.SystemInstruction);
        Assert.Equal("meal-photo-recognition@2", Assert.Single(_log.Rows).PromptVersion);
        // A statistic of the whole platform: the patient's own diary is never read for it.
        await _diary.DidNotReceiveWithAnyArgs().ListByPatientIdAsync(default, default, default);
        Assert.DoesNotContain(PatientId.ToString(), request.UserContent);
    }

    [Fact]
    public async Task Ceviche_in_the_catalog_and_in_the_prompt_list_is_reused_when_gemini_answers_ceviche()
    {
        var ceviche = _foods.Seed(new ReferenceFood(new LocalName("Ceviche"), new NutrientsPer100g(110m, 18m, 6m, 1.5m),
            SourceHash.Of("seed", "ceviche")));
        _model.Answers(Answer("Ceviche", kcal: 500m, protein: 10m, carb: 10m, fat: 1m)); // its own nutrients unused

        var view = Success(await Service().Handle(Analyze()));

        var names = JsonDocument.Parse(_model.Requests[0].UserContent).RootElement.GetProperty("catalogDishNames")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Ceviche", names);
        Assert.Equal((ceviche.Id.Value, "Ceviche"), (view.ReferenceFoodId, view.FoodName));
        Assert.DoesNotContain(_foods.Stored, f => f.Source.IsAiEstimated);
        Assert.Equal(3, _foods.Stored.Count);
    }

    [Fact]
    public async Task The_list_takes_the_most_logged_first_and_is_completed_with_verified_foods_up_to_the_count()
    {
        var arroz = _foods.Stored[1];
        var cuy = _foods.Seed(new ReferenceFood(new CreateLocalOverrideCommand(20, "Cuy al horno", 155m, 21m, 0m, 7.8m)));
        _foods.Seed(new ReferenceFood(new LocalName("Ceviche"), new NutrientsPer100g(110m, 18m, 6m, 1.5m),
            SourceHash.Of("seed", "ceviche")));
        _diary.ListMostLoggedReferenceFoodIdsAsync(default, default, default)
            .ReturnsForAnyArgs([arroz.Id.Value]);
        _configuration["FoodCatalog:PromptCatalogNamesCount"] = "3";
        _model.Answers(Answer("Lomo saltado"));

        Success(await Service().Handle(Analyze()));

        // The most logged first; then verified foods, local overrides first, then by name; never more than 3.
        Assert.Equal(["Arroz blanco cocido", "Cuy al horno", "Ceviche"],
            JsonDocument.Parse(_model.Requests[0].UserContent).RootElement.GetProperty("catalogDishNames")
                .EnumerateArray().Select(e => e.GetString()));
        await _diary.Received(1).ListMostLoggedReferenceFoodIdsAsync(MealPhotoAnalysisCommandService.MinimumPatientsPerHint,
            3, Arg.Any<CancellationToken>());
        Assert.Equal(cuy.Id.Value, _foods.Stored.Single(f => f.IsLocalOverride).Id.Value);
    }

    [Fact]
    public async Task The_list_is_cached_for_an_hour_instead_of_reading_the_diary_on_every_analysis()
    {
        _model.Answers(Answer("Lomo saltado")).Answers(Answer("Lomo saltado")).Answers(Answer("Lomo saltado"));

        Success(await Service().Handle(Analyze()));
        Success(await Service().Handle(Analyze())); // another request, same platform-wide cache
        await _diary.ReceivedWithAnyArgs(1).ListMostLoggedReferenceFoodIdsAsync(default, default, default);

        _clock.Now += TimeSpan.FromMinutes(61);
        Success(await Service().Handle(Analyze()));
        await _diary.ReceivedWithAnyArgs(2).ListMostLoggedReferenceFoodIdsAsync(default, default, default);

        // The lifetime is configurable.
        _configuration["FoodCatalog:PromptCatalogNamesCacheMinutes"] = "5";
        _clock.Now += TimeSpan.FromMinutes(61);
        _model.Answers(Answer("Lomo saltado")).Answers(Answer("Lomo saltado"));
        Success(await Service().Handle(Analyze()));
        _clock.Now += TimeSpan.FromMinutes(6);
        Success(await Service().Handle(Analyze()));
        await _diary.ReceivedWithAnyArgs(4).ListMostLoggedReferenceFoodIdsAsync(default, default, default);
    }

    [Fact]
    public async Task Without_consent_the_catalog_hints_are_not_even_read()
    {
        _consent.IsAllowedAsync(PatientId, AiFeature.MealPhotoRecognition, Arg.Any<CancellationToken>()).Returns(false);

        Assert.Equal(AiError.AiConsentRequired, Failure(await Service().Handle(Analyze())).AiError);
        await _diary.DidNotReceiveWithAnyArgs().ListMostLoggedReferenceFoodIdsAsync(default, default, default);
    }

    [Fact]
    public async Task Two_simultaneous_analyses_of_a_new_dish_create_it_once()
    {
        _model.Answers(Answer("Seco de cordero")).Answers(Answer("Seco de cordero"));
        using var barrier = new Barrier(2);
        _foods.BeforeCommit = () => Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(10)));

        var views = await Task.WhenAll(Task.Run(() => Service().Handle(Analyze())),
            Task.Run(() => Service().Handle(Analyze())));

        var created = Assert.Single(_foods.Stored, f => f.Source.IsAiEstimated);
        Assert.All(views, v => Assert.Equal(created.Id.Value, Success(v).ReferenceFoodId));
        Assert.Equal(2, _analyses.Stored.Count);
    }

    [Fact]
    public async Task Incoherent_nutrients_create_nothing_and_answer_photo_not_recognized()
    {
        // 500 kcal declared, 4·10 + 4·10 + 9·1 = 89 by Atwater.
        _model.Answers(Answer("Plato misterioso", kcal: 500m, protein: 10m, carb: 10m, fat: 1m));

        var error = Failure(await Service().Handle(Analyze()));

        Assert.Equal(IntakeError.PhotoNotRecognized, error.Error);
        Assert.Equal(2, _foods.Stored.Count);
        Assert.Empty(_analyses.Stored);
        var problem = (ObjectResult)IntakeActionResultAssembler.ToAiFunctionResult(
            new Result<MealPhotoAnalysisView, IntakeAiFailure>.Failure(error), _ => new object(), Localizer(),
            AiLocalizer());
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Fact]
    public async Task A_photo_without_a_dish_is_not_recognized()
    {
        _model.Answers(Answer(string.Empty, confidence: 0m, grams: 5m, kcal: 0m, protein: 0m, carb: 0m, fat: 0m));

        Assert.Equal(IntakeError.PhotoNotRecognized, Failure(await Service().Handle(Analyze())).Error);
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_log.Rows).Status);
        Assert.Empty(_analyses.Stored);
    }

    [Fact]
    public async Task Without_ai_consent_it_is_403_and_the_ai_is_never_called()
    {
        _consent.IsAllowedAsync(PatientId, AiFeature.MealPhotoRecognition, Arg.Any<CancellationToken>()).Returns(false);

        var error = Failure(await Service().Handle(Analyze()));

        Assert.Equal(AiError.AiConsentRequired, error.AiError);
        Assert.Empty(_model.Requests);
        Assert.Empty(_log.Rows);
        Assert.Empty(_analyses.Stored);
        var problem = (ObjectResult)IntakeActionResultAssembler.ToAiFunctionResult(
            new Result<MealPhotoAnalysisView, IntakeAiFailure>.Failure(error), _ => new object(), Localizer(),
            AiLocalizer());
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Theory]
    [InlineData(true, false, false)] // consent, preference off
    [InlineData(false, true, false)] // preference on, but no consent
    [InlineData(true, true, true)]
    public async Task The_consent_policy_requires_the_consent_and_the_meal_photo_preference(bool consent,
        bool preference, bool called)
    {
        var queries = Substitute.For<IAiPreferencesQueryService>();
        queries.Handle(new GetAiPreferencesByPatientIdQuery(PatientId), Arg.Any<CancellationToken>())
            .Returns(AiPreferencesStatus.Of(PatientId, consent, true, true, true, preference));
        _model.Answers(Answer("Lomo saltado"));

        var result = await Service(new CareRelationshipAiConsentPolicy(queries)).Handle(Analyze());

        Assert.Equal(called, result.IsSuccess);
        Assert.Equal(called ? 1 : 0, _model.Requests.Count);
        if (!called) Assert.Equal(AiError.AiConsentRequired, Failure(result).AiError);
    }

    [Fact]
    public async Task The_photo_leaves_without_its_metadata_and_with_its_pixels()
    {
        _model.Answers(Answer("Lomo saltado")).Answers(Answer("Lomo saltado"));

        Success(await Service().Handle(Analyze(MealPhotos.JpegWithMetadata())));
        Success(await Service().Handle(Analyze(MealPhotos.WebPWithMetadata())));

        var jpeg = Assert.Single(_model.Requests[0].Images!);
        Assert.Equal("image/jpeg", jpeg.MimeType);
        var webp = Assert.Single(_model.Requests[1].Images!);
        Assert.Equal("image/webp", webp.MimeType);
        foreach (var sent in new[] { jpeg.Bytes.ToArray(), webp.Bytes.ToArray() })
        {
            Assert.False(MealPhotos.Contains(sent, MealPhotos.ExifSecret));
            Assert.False(MealPhotos.Contains(sent, MealPhotos.XmpSecret));
            Assert.False(MealPhotos.Contains(sent, MealPhotos.CommentSecret));
            Assert.True(sent.AsSpan().IndexOf(MealPhotos.ScanData) >= 0);
        }

        Assert.True(MealPhotos.Contains(jpeg.Bytes.ToArray(), "JFIF"));
        Assert.Equal([0xFF, 0xD8], jpeg.Bytes[..2].ToArray());
        Assert.Equal([0xFF, 0xD9], jpeg.Bytes[^2..].ToArray());
        // WebP: no EXIF/XMP/ICC flag left in VP8X, and a RIFF size that matches the new length.
        var webpBytes = webp.Bytes.ToArray();
        Assert.Equal(0, webpBytes[20] & (0x20 | 0x08 | 0x04));
        Assert.Equal((uint)(webpBytes.Length - 8), BitConverter.ToUInt32(webpBytes, 4));
        // The input text carries nothing about the patient.
        Assert.DoesNotContain(PatientId.ToString(), _model.Requests[0].UserContent);
    }

    [Fact]
    public async Task The_image_is_never_kept_only_its_hash_is_audited()
    {
        var photo = MealPhotos.JpegWithMetadata();
        _model.Answers(Answer("Ají de gallina"));

        var view = Success(await Service().Handle(Analyze(photo)));

        var sent = _model.Requests[0].Images![0].Bytes.ToArray();
        var row = Assert.Single(_log.Rows);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(sent)).ToLowerInvariant(), row.InputImageHash);
        foreach (var text in new[]
                 {
                     row.OutputJson ?? string.Empty, row.ToString(), JsonSerializer.Serialize(view),
                     JsonSerializer.Serialize(_analyses.Stored[0].Alternatives), string.Join("\n", _serviceLog.Lines),
                     string.Join("\n", _pipelineLog.Lines)
                 })
        {
            Assert.DoesNotContain(Convert.ToBase64String(photo)[..24], text);
            Assert.DoesNotContain(Convert.ToBase64String(sent)[..24], text);
            Assert.DoesNotContain(MealPhotos.ExifSecret, text);
        }

        // There is nowhere to put an image: no byte array on the analysis, the diary entry or any resource.
        foreach (var type in new[] { typeof(MealPhotoAnalysis), typeof(DiaryEntry), typeof(MealPhotoAnalysisResource),
                     typeof(MealPhotoAlternative), typeof(DiaryEntryResource) })
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(byte[]) ||
                                                             p.PropertyType == typeof(ReadOnlyMemory<byte>) ||
                                                             p.Name.Contains("Image") || p.Name.Contains("Photo") &&
                                                             p.PropertyType == typeof(string) &&
                                                             p.Name != "PhotoRef");
        Assert.DoesNotContain("SECRET", new AnalyzeMealPhotoCommand(PatientId, photo).ToString());
    }

    [Fact]
    public async Task The_photo_must_be_present_a_jpeg_or_webp_and_within_the_size_limit()
    {
        Assert.Equal(IntakeError.PhotoRequired, Failure(await Service().Handle(Analyze([]))).Error);
        Assert.Equal(IntakeError.PhotoRequired,
            Failure(await Service().Handle(new AnalyzeMealPhotoCommand(PatientId, null))).Error);
        Assert.Equal(IntakeError.UnsupportedPhotoFormat,
            Failure(await Service().Handle(Analyze(MealPhotos.Png()))).Error);
        var big = MealPhotos.JpegWithMetadata().Concat(new byte[MealPhotoAnalysisCommandService.DefaultMaxBytes])
            .ToArray();
        Assert.Equal(IntakeError.PhotoTooLarge, Failure(await Service().Handle(Analyze(big))).Error);

        _configuration["Intake:MealPhoto:MaxBytes"] = "64";
        Assert.Equal(IntakeError.PhotoTooLarge,
            Failure(await Service().Handle(Analyze(MealPhotos.JpegWithMetadata()))).Error);

        Assert.Empty(_model.Requests);
        var problem = (ObjectResult)IntakeActionResultAssembler.ToAiFunctionResult(
            new Result<MealPhotoAnalysisView, IntakeAiFailure>.Failure(IntakeAiFailure.Of(IntakeError.PhotoTooLarge)),
            _ => new object(), Localizer(), AiLocalizer());
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problem.StatusCode);
    }

    [Fact]
    public async Task The_daily_quota_of_30_photos_answers_429()
    {
        for (var i = 0; i < 30; i++)
            _log.Seed(AiFeature.MealPhotoRecognition, PatientId, AiGenerationStatus.Succeeded,
                _clock.GetUtcNow().AddHours(-1));

        Assert.Equal(AiError.AiRateLimited, Failure(await Service().Handle(Analyze())).AiError);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Turning_meal_photo_recognition_off_or_withdrawing_consent_purges_the_analyses_but_not_the_foods()
    {
        _model.Answers(Answer("Ají de gallina")).Answers(Answer("Lomo saltado"));
        Success(await Service().Handle(Analyze()));
        _analyses.Seed(new MealPhotoAnalysis(PatientId + 1, _lomo.Id.Value, 200m, new Confidence(0.5m), [], 9,
            _clock.GetUtcNow().AddHours(24)));
        var scopes = Fakes.ScopeFactoryWith<IMealPhotoAnalysisCommandService>(Service());

        // Meal ideas off, photos on: the analyses stay.
        await new OnAiPreferencesChangedIntakeHandler(scopes, NullLogger<OnAiPreferencesChangedIntakeHandler>.Instance)
            .Handle(new AiPreferencesChanged(PatientId, true, false, true, _clock.GetUtcNow(), true), default);
        Assert.Equal(2, _analyses.Stored.Count);

        await new OnAiPreferencesChangedIntakeHandler(scopes, NullLogger<OnAiPreferencesChangedIntakeHandler>.Instance)
            .Handle(new AiPreferencesChanged(PatientId, true, true, true, _clock.GetUtcNow(), false), default);
        Assert.Equal(PatientId + 1, Assert.Single(_analyses.Stored).PatientId);

        Success(await Service().Handle(Analyze()));
        await new OnAiProcessingConsentChangedIntakeHandler(scopes,
                NullLogger<OnAiProcessingConsentChangedIntakeHandler>.Instance)
            .Handle(new AiProcessingConsentChanged(PatientId, false, _clock.GetUtcNow()), default);
        Assert.Equal(PatientId + 1, Assert.Single(_analyses.Stored).PatientId);

        // The food the AI created is shared catalog: it stays.
        Assert.Single(_foods.Stored, f => f.Source.IsAiEstimated);
    }

    [Fact]
    public async Task Expired_analyses_are_purged_by_the_worker()
    {
        _analyses.Seed(new MealPhotoAnalysis(PatientId, _lomo.Id.Value, 200m, new Confidence(0.5m), [], 9,
            _clock.GetUtcNow().AddMinutes(-1)));
        _analyses.Seed(new MealPhotoAnalysis(PatientId, _lomo.Id.Value, 200m, new Confidence(0.5m), [], 9,
            _clock.GetUtcNow().AddHours(23)));

        await new MealPhotoAnalysisPurgeHostedService(Fakes.ScopeFactoryWith<IMealPhotoAnalysisCommandService>(Service()),
                new ConfigurationBuilder().Build(), NullLogger<MealPhotoAnalysisPurgeHostedService>.Instance)
            .RunCycleAsync(CancellationToken.None);

        Assert.True(Assert.Single(_analyses.Stored).ExpiresAt > _clock.GetUtcNow());
    }

    [Fact]
    public void An_analysis_keeps_its_invariants()
    {
        Assert.Throws<ArgumentException>(() => new MealPhotoAnalysis(PatientId, 1, 4m, new Confidence(0.5m), [], 9,
            _clock.GetUtcNow()));
        Assert.Throws<ArgumentException>(() => new MealPhotoAnalysis(PatientId, 1, 2001m, new Confidence(0.5m), [], 9,
            _clock.GetUtcNow()));
        Assert.Throws<ArgumentException>(() => new MealPhotoAnalysis(PatientId, 1, 200m, new Confidence(0.5m),
            [new("a", 10m, null), new("b", 10m, null), new("c", 10m, null), new("d", 10m, null)], 9,
            _clock.GetUtcNow()));
        Assert.Throws<ArgumentException>(() => new MealPhotoAnalysis(PatientId, 1, 200m, new Confidence(0.5m), [], 0,
            _clock.GetUtcNow()));
        Assert.Throws<ArgumentException>(() => new MealPhoto(MealPhotos.Png()));

        var analysis = new MealPhotoAnalysis(PatientId, 1, 200m, new Confidence(0.5m), [], 9, _clock.GetUtcNow());
        Assert.True(analysis.IsExpiredAt(_clock.GetUtcNow()));
        Assert.False(analysis.BelongsTo(PatientId + 1));
        Assert.Equal((1, 200m, 0.5m), (analysis.ToProposal(_clock.GetUtcNow()).ReferenceFoodId,
            analysis.ToProposal(_clock.GetUtcNow()).PortionGrams, analysis.ToProposal(_clock.GetUtcNow()).Confidence.Value));
    }

    [Fact]
    public async Task The_endpoint_is_only_for_the_patient_and_answers_201()
    {
        var service = Substitute.For<IMealPhotoAnalysisCommandService>();
        var view = new MealPhotoAnalysisView(Guid.NewGuid(), 5, "Lomo saltado", 320m, 0.8m, [],
            _clock.GetUtcNow().AddHours(24));
        service.Handle(Arg.Any<AnalyzeMealPhotoCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<MealPhotoAnalysisView, IntakeAiFailure>.Success(view));
        var controller = new PatientMealPhotoAnalysesController(service, Localizer(), AiLocalizer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", PatientId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, PatientId.ToString()), new Claim("id", PatientId.ToString())],
                        "test"))
                }
            }
        };
        var bytes = MealPhotos.JpegWithMetadata();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "plato.jpg");

        Assert.IsType<ForbidResult>(await controller.AnalyzeMealPhoto(PatientId + 1, file));
        var created = Assert.IsType<ObjectResult>(await controller.AnalyzeMealPhoto(PatientId, file));

        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var resource = Assert.IsType<MealPhotoAnalysisResource>(created.Value);
        Assert.Equal((view.AnalysisId, "Lomo saltado"), (resource.AnalysisId, resource.FoodName));
        await service.Received(1).Handle(Arg.Is<AnalyzeMealPhotoCommand>(c => c.PatientId == PatientId &&
                                                                              c.Photo!.SequenceEqual(bytes)),
            Arg.Any<CancellationToken>());
    }

    private MealPhotoAnalysisCommandService Service(IAiConsentPolicy? consent = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build();
        var settings = new ConfiguredAiSettings(configuration, NullLogger<ConfiguredAiSettings>.Instance);
        var pipeline = new AiGenerationPipeline(settings, consent ?? _consent,
            PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly), _model, _log, _clock, _pipelineLog);
        var request = _foods.NewRequest();
        var foodCommands = new ReferenceFoodCommandService(request, request, [_provider],
            NullLogger<ReferenceFoodCommandService>.Instance, Substitute.For<IMediator>(), configuration);
        var catalog = new FoodCatalogContextFacade(new ReferenceFoodQueryService(request), foodCommands);
        return new MealPhotoAnalysisCommandService(_analyses, _diary, _analyses, catalog, new PhotoMetadataStripper(),
            pipeline, settings, consent ?? _consent, _hintsCache, configuration, _clock, _serviceLog);
    }

    private static AnalyzeMealPhotoCommand Analyze(byte[]? photo = null)
    {
        return new AnalyzeMealPhotoCommand(PatientId, photo ?? MealPhotos.JpegWithMetadata());
    }

    private static string Answer(string dish, decimal grams = 320m, decimal confidence = 0.82m,
        (string Name, decimal Grams)[]? alternatives = null, decimal kcal = 150m, decimal protein = 10m,
        decimal carb = 12m, decimal fat = 7m)
    {
        return JsonSerializer.Serialize(new
        {
            dishName = dish,
            estimatedGrams = grams,
            confidence,
            alternatives = (alternatives ?? []).Select(a => new { name = a.Name, grams = a.Grams }).ToArray(),
            nutrientsPer100g = new { kcal, protein, carb, fat }
        });
    }

    private static MealPhotoAnalysisView Success(Result<MealPhotoAnalysisView, IntakeAiFailure> result)
    {
        return Assert.IsType<Result<MealPhotoAnalysisView, IntakeAiFailure>.Success>(result).Value;
    }

    private static IntakeAiFailure Failure(Result<MealPhotoAnalysisView, IntakeAiFailure> result)
    {
        return Assert.IsType<Result<MealPhotoAnalysisView, IntakeAiFailure>.Failure>(result).Error;
    }

    private static IStringLocalizer<IntakeMessages> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<IntakeMessages>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>()));
        return localizer;
    }

    private static IStringLocalizer<AiMessages> AiLocalizer()
    {
        var localizer = Substitute.For<IStringLocalizer<AiMessages>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>()));
        return localizer;
    }
}

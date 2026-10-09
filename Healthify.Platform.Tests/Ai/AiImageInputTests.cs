using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;
using Healthify.Platform.Shared.Infrastructure.Ai.Fakes;
using Healthify.Platform.Shared.Infrastructure.Ai.Gemini;
using Healthify.Platform.Shared.Infrastructure.Ai.Persistence;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.Ai;

/// <summary>
///     IN-7, Shared module: images travel to the model as inline parts, the audit keeps only their SHA-256, and
///     neither the audit nor any log line ever holds the bytes (raw, base64 or hexadecimal).
/// </summary>
public class AiImageInputTests
{
    private const int PatientId = 10;

    /// <summary>A recognizable payload: if it leaks anywhere, these bytes (or their base64) show up.</summary>
    private static readonly byte[] Photo = [0xFF, 0xD8, 0xFF, .."SECRET-PIXELS-OF-THE-DISH-0123456789"u8, 0xFF, 0xD9];

    private const string Output =
        """{"dishName":"Lomo saltado","estimatedGrams":320,"confidence":0.8,"alternatives":[],"nutrientsPer100g":{"kcal":150,"protein":10,"carb":12,"fat":7}}""";

    [Fact]
    public void An_image_part_hashes_its_bytes_and_never_prints_them()
    {
        var part = new AiImagePart(Photo, "image/JPEG");

        Assert.Equal(Sha256(Photo), part.Sha256);
        Assert.Equal("image/jpeg", part.MimeType);
        Assert.DoesNotContain("SECRET", part.ToString());
        Assert.DoesNotContain(Convert.ToBase64String(Photo), part.ToString());
        Assert.Throws<ArgumentException>(() => new AiImagePart(Array.Empty<byte>(), "image/jpeg"));
        Assert.Throws<ArgumentException>(() => new AiImagePart(Photo, "application/pdf"));

        var other = new AiImagePart("other"u8.ToArray(), "image/webp");
        Assert.Null(AiImagePart.CombinedHash(null));
        Assert.Equal(part.Sha256, AiImagePart.CombinedHash([part]));
        Assert.Equal(Sha256(Encoding.UTF8.GetBytes(part.Sha256 + ":" + other.Sha256)),
            AiImagePart.CombinedHash([part, other]));
    }

    [Fact]
    public async Task The_pipeline_sends_the_image_and_audits_only_its_hash()
    {
        var model = new FakeLanguageModelClient().Answers(Output);
        var log = new InMemoryAiGenerationLog();
        var logger = new CapturingLogger<AiGenerationPipeline>();

        var result = await Pipeline(model, log, logger).GenerateAsync(Request(), new AcceptAll());

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(Assert.Single(model.Requests).Images!);
        Assert.Equal(Photo, sent.Bytes.ToArray());
        Assert.Equal("image/jpeg", sent.MimeType);
        // The image is not part of the pseudonymized text input.
        Assert.DoesNotContain(Convert.ToBase64String(Photo), model.Requests[0].UserContent);

        var row = Assert.Single(log.Rows);
        Assert.Equal(Sha256(Photo), row.InputImageHash);
        AssertNoTraceOfThePhoto(Describe(row));
        AssertNoTraceOfThePhoto(string.Join("\n", logger.Lines));

        // The EF row has a column for the hash and none for the bytes.
        var entity = new AiGeneration(row);
        Assert.Equal(Sha256(Photo), entity.InputImageHash);
        Assert.DoesNotContain(typeof(AiGeneration).GetProperties(),
            p => p.PropertyType == typeof(byte[]) || p.PropertyType == typeof(ReadOnlyMemory<byte>));
    }

    [Fact]
    public async Task A_failed_or_rejected_generation_with_an_image_is_audited_by_its_hash_too()
    {
        var model = new FakeLanguageModelClient().Fails(LanguageModelFailure.ServerError).Answers("not json");
        var log = new InMemoryAiGenerationLog();
        var logger = new CapturingLogger<AiGenerationPipeline>();
        var pipeline = Pipeline(model, log, logger);

        Assert.Equal(AiError.AiProviderUnavailable, Failure(await pipeline.GenerateAsync(Request(), new AcceptAll())));
        Assert.Equal(AiError.AiOutputRejected, Failure(await pipeline.GenerateAsync(Request(), new AcceptAll())));

        Assert.All(log.Rows, row => Assert.Equal(Sha256(Photo), row.InputImageHash));
        Assert.All(log.Rows, row => AssertNoTraceOfThePhoto(Describe(row)));
        AssertNoTraceOfThePhoto(string.Join("\n", logger.Lines));
    }

    [Fact]
    public async Task A_text_only_generation_keeps_no_image_hash()
    {
        var model = new FakeLanguageModelClient().Answers(Output);
        var log = new InMemoryAiGenerationLog();

        await Pipeline(model, log, new CapturingLogger<AiGenerationPipeline>())
            .GenerateAsync(Request() with { Images = null }, new AcceptAll());

        Assert.Null(Assert.Single(log.Rows).InputImageHash);
        Assert.Null(model.Requests[0].Images);
    }

    [Fact]
    public async Task Gemini_sends_each_image_as_an_inline_part_after_the_text_and_logs_nothing_of_it()
    {
        var handler = new StubHandler();
        var logger = new CapturingLogger<GeminiLanguageModelClient>();
        var client = new GeminiLanguageModelClient(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Ai:Gemini:ApiKey"] = "test-key" }).Build(),
            Substitute.For<IGoogleAccessTokenProvider>(), logger);

        await client.GenerateStructuredAsync(new LanguageModelRequest("Recognize the dish.", """{"task":"x"}""",
            """{"type":"object"}""", 256, TimeSpan.FromSeconds(5), [new AiImagePart(Photo, "image/jpeg")]));

        var parts = JsonNode.Parse(Assert.Single(handler.Bodies))!["contents"]![0]!["parts"]!.AsArray();
        Assert.Equal(2, parts.Count);
        Assert.Equal("""{"task":"x"}""", parts[0]!["text"]!.GetValue<string>());
        Assert.Equal("image/jpeg", parts[1]!["inlineData"]!["mimeType"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(Photo), parts[1]!["inlineData"]!["data"]!.GetValue<string>());
        AssertNoTraceOfThePhoto(string.Join("\n", logger.Lines));
    }

    [Fact]
    public void Meal_photo_recognition_is_a_patient_function_with_a_quota_of_30_by_default()
    {
        Assert.Same(AiFeature.MealPhotoRecognition, AiFeature.FromName("MealPhotoRecognition"));
        Assert.Equal(AiFeatureAudience.Patient, AiFeature.MealPhotoRecognition.Audience);
        var settings = new ConfiguredAiSettings(new ConfigurationBuilder().Build(),
            NullLogger<ConfiguredAiSettings>.Instance);

        Assert.Equal(30, settings.DailyLimit(AiFeature.MealPhotoRecognition));
        Assert.False(settings.IsEnabled(AiFeature.MealPhotoRecognition)); // AI off by default
        Assert.NotNull(PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly)
            .FindLatest(AiFeature.MealPhotoRecognition));
    }

    internal static void AssertNoTraceOfThePhoto(string text)
    {
        Assert.DoesNotContain("SECRET-PIXELS", text);
        Assert.DoesNotContain(Convert.ToBase64String(Photo), text);
        Assert.DoesNotContain(Convert.ToBase64String(Photo)[..16], text);
        Assert.DoesNotContain(Convert.ToHexString(Photo), text, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(AiGenerationRecord row)
    {
        return string.Join("|", row.Feature, row.PromptVersion, row.Model, row.InputHash, row.OutputJson,
            row.ErrorCode, row.ToString());
    }

    private static AiGenerationRequest Request()
    {
        return new AiGenerationRequest(AiFeature.MealPhotoRecognition, PatientId, PatientId, "es",
            new { task = "recognize-main-dish" }, Images: [new AiImagePart(Photo, "image/jpeg")]);
    }

    private static AiGenerationPipeline Pipeline(FakeLanguageModelClient model, InMemoryAiGenerationLog log,
        CapturingLogger<AiGenerationPipeline> logger)
    {
        var consent = Substitute.For<IAiConsentPolicy>();
        consent.IsAllowedAsync(default, default!, default).ReturnsForAnyArgs(true);
        var settings = new ConfiguredAiSettings(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Ai:Enabled"] = "true" }).Build(), NullLogger<ConfiguredAiSettings>.Instance);
        return new AiGenerationPipeline(settings, consent, PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly),
            model, log, TimeProvider.System, logger);
    }

    private static AiError Failure<T>(Result<T, AiError> result)
    {
        return Assert.IsType<Result<T, AiError>.Failure>(result).Error;
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class AcceptAll : IAiOutputValidator<JsonObject>
    {
        public IReadOnlyList<string> Validate(JsonObject output)
        {
            return [];
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"STOP"}]}
                    """, Encoding.UTF8, "application/json")
            };
        }
    }
}

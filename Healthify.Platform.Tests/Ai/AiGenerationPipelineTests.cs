using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.Ai;

/// <summary>
///     IA-0. Every guard of the pipeline, in its order. The kill switch, the feature flag and consent stop before
///     any patient data is processed: no audit row, only an Information log without the patient. From
///     pseudonymization on, every outcome leaves its row, failures included. The language model is the fake: no
///     test reaches Google.
/// </summary>
public class AiGenerationPipelineTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "headline": { "type": "string", "maxLength": 80 },
            "highlights": { "type": "array", "items": { "type": "string" }, "maxItems": 3 }
          },
          "required": ["headline", "highlights"],
          "additionalProperties": false
        }
        """;

    private const string ValidOutput = """{"headline":"Buena semana","highlights":["Registraste casi todo"]}""";

    private readonly Dictionary<string, string?> _configuration = new()
    {
        ["Ai:Enabled"] = "true",
        ["Ai:Features:WeeklySummary:DailyLimit"] = "2"
    };

    private readonly IAiConsentPolicy _consent = Substitute.For<IAiConsentPolicy>();
    private readonly FakeLanguageModelClient _model = new();
    private readonly InMemoryAiGenerationLog _log = new();
    private readonly CapturingLogger _logger = new();

    public AiGenerationPipelineTests()
    {
        _consent.IsAllowedAsync(Arg.Any<int>(), Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task Ai_disabled_answers_feature_disabled_before_asking_for_consent_or_calling_the_model()
    {
        _configuration["Ai:Enabled"] = null; // the default in code: off

        var result = await Generate();

        Assert.Equal(AiError.AiFeatureDisabled, Error(result));
        await _consent.DidNotReceiveWithAnyArgs().IsAllowedAsync(default, null!);
        Assert.Empty(_model.Requests);
        Assert.Empty(_log.Rows); // nothing about the patient was processed: no audit row
        AssertNotAttemptedLogged(AiError.AiFeatureDisabled);
    }

    [Fact]
    public async Task A_feature_flag_off_disables_only_that_feature()
    {
        _configuration["Ai:Features:WeeklySummary:Enabled"] = "false";
        _model.Answers("""{"ideas":[]}""");

        Assert.Equal(AiError.AiFeatureDisabled, Error(await Generate()));
        Assert.Empty(_log.Rows);
        AssertNotAttemptedLogged(AiError.AiFeatureDisabled);
        Assert.True((await Generate(AiFeature.MealIdeas)).IsSuccess);
        Assert.Equal(AiFeature.MealIdeas.Name, Assert.Single(_log.Rows).Feature);
    }

    [Fact]
    public async Task Without_consent_answers_consent_required_and_never_calls_the_model()
    {
        _consent.IsAllowedAsync(PatientId, AiFeature.WeeklySummary, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Generate();

        Assert.Equal(AiError.AiConsentRequired, Error(result));
        Assert.Empty(_model.Requests);
        Assert.Empty(_log.Rows);
        AssertNotAttemptedLogged(AiError.AiConsentRequired);
    }

    [Fact]
    public async Task A_failing_consent_lookup_counts_as_no_consent()
    {
        _consent.IsAllowedAsync(Arg.Any<int>(), Arg.Any<AiFeature>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));

        Assert.Equal(AiError.AiConsentRequired, Error(await Generate()));
        Assert.Empty(_model.Requests);
        Assert.Empty(_log.Rows);
    }

    [Fact]
    public async Task The_daily_quota_answers_rate_limited_and_blocked_rows_do_not_use_it()
    {
        _log.Seed(AiFeature.WeeklySummary, PatientId, AiGenerationStatus.Succeeded, Now.AddHours(-3));
        _log.Seed(AiFeature.WeeklySummary, PatientId, AiGenerationStatus.Blocked, Now.AddHours(-2));
        _log.Seed(AiFeature.WeeklySummary, PatientId, AiGenerationStatus.Succeeded, Now.AddDays(-2)); // outside 24 h
        _log.Seed(AiFeature.WeeklySummary, PatientId + 1, AiGenerationStatus.Succeeded, Now.AddHours(-1));
        _model.Answers(ValidOutput);

        Assert.True((await Generate()).IsSuccess); // 1 of 2 used

        var result = await Generate(); // 2 of 2 used

        Assert.Equal(AiError.AiRateLimited, Error(result));
        Assert.Single(_model.Requests);
        var row = _log.Rows[^1];
        Assert.Equal(AiGenerationStatus.Blocked, row.Status);
        Assert.Equal(nameof(AiError.AiRateLimited), row.ErrorCode);
        Assert.NotNull(row.InputHash); // pseudonymization ran before the quota
    }

    [Fact]
    public async Task A_practitioner_feature_is_counted_per_practitioner()
    {
        _configuration["Ai:Features:DiagnosisSuggestion:DailyLimit"] = "1";
        _log.Seed(AiFeature.DiagnosisSuggestion, PatientId + 5, AiGenerationStatus.Succeeded, Now.AddHours(-1),
            PractitionerId);

        var result = await Generate(AiFeature.DiagnosisSuggestion, PractitionerId);

        Assert.Equal(AiError.AiRateLimited, Error(result));
    }

    [Fact]
    public async Task A_provider_failure_answers_provider_unavailable_and_is_audited_as_failed()
    {
        _model.Fails(LanguageModelFailure.Timeout);

        var result = await Generate();

        Assert.Equal(AiError.AiProviderUnavailable, Error(result));
        var row = Assert.Single(_log.Rows);
        Assert.Equal(AiGenerationStatus.Failed, row.Status);
        Assert.Equal(nameof(LanguageModelFailure.Timeout), row.ErrorCode);
        Assert.Equal("weekly-summary@2", row.PromptVersion);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"headline":"Buena semana"}""")] // required highlights missing
    [InlineData("""{"headline":"Buena semana","highlights":[],"diagnosis":"E66"}""")] // not allowed
    [InlineData("""{"headline":"Buena semana","highlights":["a","b","c","d"]}""")] // too many
    public async Task An_output_that_breaks_the_json_or_the_schema_is_rejected_and_audited(string output)
    {
        _model.Answers(output, 120, 30);

        var result = await Generate();

        Assert.Equal(AiError.AiOutputRejected, Error(result));
        var row = Assert.Single(_log.Rows);
        Assert.Equal(AiGenerationStatus.Rejected, row.Status);
        Assert.Null(row.OutputJson);
        Assert.Equal(120, row.InputTokens);
        Assert.Equal(FakeLanguageModelClient.FakeModel, row.Model);
    }

    [Fact]
    public async Task An_output_that_breaks_a_rule_of_the_function_is_rejected()
    {
        _model.Answers("""{"headline":"Fallaste otra vez","highlights":[]}""");

        var result = await Generate(validator: new NoAccusationValidator());

        Assert.Equal(AiError.AiOutputRejected, Error(result));
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_log.Rows).Status);
    }

    [Fact]
    public async Task A_provider_answer_without_usable_output_is_rejected()
    {
        _model.Fails(LanguageModelFailure.InvalidResponse);

        Assert.Equal(AiError.AiOutputRejected, Error(await Generate()));
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_log.Rows).Status);
    }

    [Fact]
    public async Task A_valid_output_is_returned_with_its_audit_row()
    {
        _model.Answers(ValidOutput, 150, 40);

        var result = await Generate();

        var outcome = Assert.IsType<Result<AiGenerationOutcome<WeeklySummaryOutput>, AiError>.Success>(result).Value;
        Assert.Equal("Buena semana", outcome.Output.Headline);
        Assert.Equal("weekly-summary@2", outcome.PromptVersion);
        var row = Assert.Single(_log.Rows);
        Assert.Equal(1, outcome.GenerationId);
        Assert.Equal(AiGenerationStatus.Succeeded, row.Status);
        Assert.Equal(ValidOutput, row.OutputJson);
        Assert.Equal(PatientId, row.SubjectPatientId);
        Assert.Equal(AiInputPseudonymizer.Sha256(_model.Requests[0].UserContent), row.InputHash);
        Assert.Equal((150, 40), (row.InputTokens, row.OutputTokens));
        Assert.Equal(Now, row.CreatedAt);
        Assert.Equal(Now.AddDays(180), row.ExpiresAt);
    }

    [Fact]
    public async Task The_model_receives_the_prompt_in_the_language_the_schema_and_only_the_pseudonymized_input()
    {
        _model.Answers(ValidOutput);

        await Generate();

        var request = Assert.Single(_model.Requests);
        Assert.Contains("Answer in es.", request.SystemInstruction);
        Assert.DoesNotContain("Output schema", request.SystemInstruction);
        Assert.Contains("\"headline\"", request.OutputSchemaJson);
        Assert.Equal(TimeSpan.FromSeconds(20), request.Timeout);
        Assert.DoesNotContain("Ana", request.UserContent);
        Assert.DoesNotContain("ana@correo.com", request.UserContent);
        Assert.DoesNotContain(PatientId.ToString(), request.UserContent);
        Assert.Contains("1850", request.UserContent); // the aggregates stay
    }

    [Fact]
    public async Task A_success_that_cannot_be_audited_is_not_returned()
    {
        _model.Answers(ValidOutput);
        _log.FailOnRecord = true;

        Assert.Equal(AiError.UnexpectedError, Error(await Generate()));
    }

    [Fact]
    public async Task A_feature_without_a_prompt_is_treated_as_disabled()
    {
        var result = await Generate(AiFeature.PlanAdjustmentProposal, PractitionerId);

        Assert.Equal(AiError.AiFeatureDisabled, Error(result));
        Assert.Empty(_model.Requests);
        // Past pseudonymization: audited, unlike the kill switch.
        var row = Assert.Single(_log.Rows);
        Assert.Equal(AiGenerationStatus.Blocked, row.Status);
        Assert.NotNull(row.InputHash);
    }

    [Fact]
    public async Task An_unexpected_failure_before_any_data_is_processed_leaves_no_row()
    {
        _consent.IsAllowedAsync(Arg.Any<int>(), Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(true);
        var pipeline = new AiGenerationPipeline(new ThrowingSettings(), _consent, new PromptCatalog([]), _model, _log,
            new FixedTimeProvider(Now), _logger);

        var result = await pipeline.GenerateAsync(Request(AiFeature.WeeklySummary, null),
            SchemaOnlyAiOutputValidator<WeeklySummaryOutput>.Instance);

        Assert.Equal(AiError.UnexpectedError, Error(result));
        Assert.Empty(_log.Rows);
    }

    [Fact]
    public void Each_context_maps_the_shared_ai_errors_to_the_same_statuses()
    {
        var localizer = Substitute.For<IStringLocalizer<AiMessages>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>()));
        Func<AiError, IActionResult>[] assemblers =
        [
            e => CareRelationshipActionResultAssembler.ToAiFailureResult(e, localizer),
            e => MonitoringActionResultAssembler.ToAiFailureResult(e, localizer),
            e => IntakeActionResultAssembler.ToAiFailureResult(e, localizer),
            e => NutritionalCareActionResultAssembler.ToAiFailureResult(e, localizer)
        ];
        var expected = new Dictionary<AiError, int>
        {
            [AiError.AiFeatureDisabled] = StatusCodes.Status503ServiceUnavailable,
            [AiError.AiConsentRequired] = StatusCodes.Status403Forbidden,
            [AiError.AiRateLimited] = StatusCodes.Status429TooManyRequests,
            [AiError.AiProviderUnavailable] = StatusCodes.Status503ServiceUnavailable,
            [AiError.AiOutputRejected] = StatusCodes.Status502BadGateway,
            [AiError.UnexpectedError] = StatusCodes.Status500InternalServerError
        };

        foreach (var assembler in assemblers)
        foreach (var (error, status) in expected)
        {
            var result = Assert.IsType<ObjectResult>(assembler(error));
            Assert.Equal(status, result.StatusCode);
            // The localizer answers with the key: each error uses the text of its own name.
            Assert.Equal(error.ToString(), Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(result.Value).Detail);
        }
    }

    private Task<Result<AiGenerationOutcome<WeeklySummaryOutput>, AiError>> Generate(
        IAiOutputValidator<WeeklySummaryOutput>? validator = null)
    {
        return Pipeline().GenerateAsync(Request(AiFeature.WeeklySummary, null),
            validator ?? SchemaOnlyAiOutputValidator<WeeklySummaryOutput>.Instance);
    }

    private Task<Result<AiGenerationOutcome<object>, AiError>> Generate(AiFeature feature, int? requestedBy = null)
    {
        return Pipeline().GenerateAsync(Request(feature, requestedBy), SchemaOnlyAiOutputValidator<object>.Instance);
    }

    private static AiGenerationRequest Request(AiFeature feature, int? requestedBy)
    {
        return new AiGenerationRequest(feature, PatientId, requestedBy ?? PatientId, "es",
            new
            {
                PatientId,
                Name = "Ana Pérez",
                Email = "ana@correo.com",
                Note = "Ana registró casi todo",
                KcalPerDay = new[] { 1850, 1920 }
            },
            ["Ana", "Pérez"]);
    }

    private AiGenerationPipeline Pipeline()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build();
        var catalog = new PromptCatalog([
            Prompt("weekly-summary@1.md", "old"),
            Prompt("weekly-summary@2.md", "current"),
            Prompt("meal-ideas@1.md", "ideas", """{"type":"object"}"""),
            Prompt("diagnosis-suggestion@1.md", "diagnosis", """{"type":"object"}""")
        ]);
        return new AiGenerationPipeline(
            new ConfiguredAiSettings(configuration, NullLogger<ConfiguredAiSettings>.Instance),
            _consent, catalog, _model, _log, new FixedTimeProvider(Now), _logger);
    }

    private static KeyValuePair<string, string> Prompt(string file, string role, string schema = Schema)
    {
        return new KeyValuePair<string, string>(file,
            $"# Role\n\nYou write the {role} summary. Answer in {{{{language}}}}.\n\n## Output schema\n\n```json\n{schema}\n```\n");
    }

    private static AiError? Error<T>(Result<T, AiError> result)
    {
        return result is Result<T, AiError>.Failure f ? f.Error : null;
    }

    private void AssertNotAttemptedLogged(AiError reason)
    {
        var entry = Assert.Single(_logger.Entries, e => e.Message.Contains("not attempted"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(reason.ToString(), entry.Message);
        Assert.Contains(AiFeature.WeeklySummary.Name, entry.Message);
        Assert.DoesNotContain(PatientId.ToString(), entry.Message);
        Assert.DoesNotContain("Ana", entry.Message);
    }

    public sealed record WeeklySummaryOutput(string Headline, IReadOnlyList<string> Highlights);

    private sealed class NoAccusationValidator : IAiOutputValidator<WeeklySummaryOutput>
    {
        public IReadOnlyList<string> Validate(WeeklySummaryOutput output)
        {
            return output.Headline.Contains("Fallaste", StringComparison.OrdinalIgnoreCase)
                ? ["Accusatory wording."]
                : [];
        }
    }

    private sealed class ThrowingSettings : IAiSettings
    {
        public bool IsEnabled(AiFeature feature) => throw new InvalidOperationException("Broken configuration.");
        public int DailyLimit(AiFeature feature) => 0;
        public int MaxOutputTokens(AiFeature feature) => 0;
        public TimeSpan Timeout => TimeSpan.Zero;
        public TimeSpan Retention => TimeSpan.Zero;
    }

    private sealed class CapturingLogger : ILogger<AiGenerationPipeline>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

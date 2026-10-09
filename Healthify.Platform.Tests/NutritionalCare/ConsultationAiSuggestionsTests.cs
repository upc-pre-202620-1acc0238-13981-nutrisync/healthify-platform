using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;
using Healthify.Platform.Shared.Infrastructure.Ai.Fakes;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     IA-6 and IA-7. The AI suggestions of the guided consultation: through the shared pipeline (real settings,
///     prompts and audit, fake model), validated by this context, and always with their deterministic fallback.
///     The patient: woman, 31, 168 cm, 74.2 kg, waist 88 cm (IMC 26.3, OverweightGradeI).
/// </summary>
public class ConsultationAiSuggestionsTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int ConsultationId = 40;
    private const int AssessmentId = 100;
    private const int DiagnosisId = 7;
    private static readonly DateOnly Today = new(2026, 3, 10);

    private readonly IConsultationRepository _consultations = Substitute.For<IConsultationRepository>();
    private readonly INutritionalAssessmentRepository _assessments = Substitute.For<INutritionalAssessmentRepository>();
    private readonly INutritionalDiagnosisRepository _diagnoses = Substitute.For<INutritionalDiagnosisRepository>();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly ICareRelationshipContextFacade _care = Substitute.For<ICareRelationshipContextFacade>();
    private readonly IAiConsentPolicy _consent = Substitute.For<IAiConsentPolicy>();
    private readonly FakeLanguageModelClient _model = new();
    private readonly InMemoryAiGenerationLog _log = new();
    private readonly Dictionary<string, string?> _configuration = new() { ["Ai:Enabled"] = "true" };
    private readonly Consultation _consultation;

    public ConsultationAiSuggestionsTests()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _iam.GetPreferredLanguage(PractitionerId, Arg.Any<CancellationToken>()).Returns("es");
        _care.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _consent.IsAllowedAsync(PatientId, Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(true);

        var baseline = ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female",
            168m, Today);
        var assessment = ConsultationScenario.MeasuredAssessment(AssessmentId, ConsultationId, PractitionerId,
            baseline, Today, 74.2m);
        _assessments.FindByIdAsync(AssessmentId, Arg.Any<CancellationToken>()).Returns(assessment);
        _diagnoses.FindByIdAsync(DiagnosisId, Arg.Any<CancellationToken>()).Returns(
            ConsultationScenario.PendingDiagnosis(DiagnosisId, assessment, DiagnosisCode.OverweightGradeI,
                ConsultationId));

        _consultation = ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId);
        _consultation.AttachAssessment(AssessmentId);
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(_consultation);
    }

    // ---- IA-6 ----

    [Fact]
    public async Task An_ai_suggestion_within_one_grade_of_the_index_is_returned_with_its_generation()
    {
        _model.Answers("""{ "code": "ObesityGradeI", "rationale": "IMC 26.3 kg/m² con cintura de 88 cm e hipotiroidismo." }""");

        var suggestion = Success(await Service().Handle(Diagnosis()));

        Assert.Equal(DiagnosisCode.ObesityGradeI, suggestion.Code);
        Assert.Equal(DiagnosisSuggestion.AiSource, suggestion.Source);
        Assert.Equal("IMC 26.3 kg/m² con cintura de 88 cm e hipotiroidismo.", suggestion.Rationale);
        var row = Assert.Single(_log.Rows);
        Assert.Equal(AiGenerationStatus.Succeeded, row.Status);
        Assert.Equal("diagnosis-suggestion@1", row.PromptVersion);
        Assert.Equal(PractitionerId, row.RequestedByUserId);
        Assert.Equal(1L, suggestion.AiGenerationId);
    }

    [Fact]
    public async Task An_ai_diagnosis_more_than_one_grade_from_the_index_falls_back_to_the_rule()
    {
        // IA-6: IMC 26.3 → ObesityGradeII contradicts the index by two grades.
        _model.Answers("""{ "code": "ObesityGradeII", "rationale": "Riesgo cardiometabólico elevado por la cintura." }""");

        var suggestion = Success(await Service().Handle(Diagnosis()));

        Assert.Equal(DiagnosisCode.OverweightGradeI, suggestion.Code);
        Assert.Equal(DiagnosisSuggestion.RuleSource, suggestion.Source);
        Assert.Equal("IMC 26.3 kg/m²; cintura 88 cm", suggestion.Rationale);
        Assert.Null(suggestion.AiGenerationId);
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_log.Rows).Status);
    }

    [Theory]
    [InlineData("Underweight", false)]
    [InlineData("NormalWeight", true)]
    [InlineData("OverweightGradeI", true)]
    [InlineData("ObesityGradeI", true)]
    [InlineData("ObesityGradeII", false)]
    [InlineData("ObesityGradeIII", false)]
    public void The_validator_accepts_only_codes_within_one_grade_of_the_index(string code, bool accepted)
    {
        var validator = new DiagnosisSuggestionOutputValidator(new DiagnosisCode(DiagnosisCode.OverweightGradeI));

        Assert.Equal(accepted, validator.Validate(new DiagnosisSuggestionOutput(code, "Fundamento clínico.")).Count == 0);
    }

    [Fact]
    public void The_validator_rejects_a_code_outside_the_list_and_a_rationale_too_long()
    {
        var validator = new DiagnosisSuggestionOutputValidator(new DiagnosisCode(DiagnosisCode.OverweightGradeI));

        Assert.NotEmpty(validator.Validate(new DiagnosisSuggestionOutput("Other", "Fundamento clínico.")));
        Assert.NotEmpty(validator.Validate(new DiagnosisSuggestionOutput(DiagnosisCode.OverweightGradeI,
            new string('x', 601))));
    }

    [Fact]
    public async Task With_the_ai_off_the_rule_answers_and_nothing_is_generated_or_audited()
    {
        _configuration["Ai:Enabled"] = "false";

        var suggestion = Success(await Service().Handle(Diagnosis()));

        Assert.Equal(DiagnosisSuggestion.RuleSource, suggestion.Source);
        Assert.Empty(_model.Requests);
        Assert.Empty(_log.Rows);
    }

    [Fact]
    public async Task Without_the_patients_ai_consent_the_rule_answers_and_the_model_is_never_called()
    {
        // §12-#5: practitioner functions process the patient's data, so they need the patient's AI consent.
        _consent.IsAllowedAsync(PatientId, AiFeature.DiagnosisSuggestion, Arg.Any<CancellationToken>())
            .Returns(false);

        var suggestion = Success(await Service().Handle(Diagnosis()));

        Assert.Equal(DiagnosisSuggestion.RuleSource, suggestion.Source);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task When_the_provider_fails_the_rule_answers()
    {
        _model.Fails(LanguageModelFailure.ServerError);

        var suggestion = Success(await Service().Handle(Diagnosis()));

        Assert.Equal(DiagnosisSuggestion.RuleSource, suggestion.Source);
        Assert.Equal(AiGenerationStatus.Failed, Assert.Single(_log.Rows).Status);
    }

    [Fact]
    public async Task The_input_is_the_clinical_snapshot_without_free_text_or_identifiers()
    {
        _model.Answers("""{ "code": "OverweightGradeI", "rationale": "IMC 26.3 kg/m² y cintura de 88 cm." }""");

        await Service().Handle(Diagnosis());

        var input = Assert.Single(_model.Requests).UserContent;
        Assert.Contains("\"bmiCategory\":\"OverweightGradeI\"", input);
        Assert.Contains("Hypothyroidism", input);
        Assert.DoesNotContain("patientId", input, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("consultationId", input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Guards_run_before_the_ai_in_order_practitioner_consultation_link_measurement()
    {
        _iam.IsPractitioner(99, Arg.Any<CancellationToken>()).Returns(false);
        AssertFailure(await Service().Handle(new SuggestConsultationDiagnosisCommand(ConsultationId, 99)),
            NutritionalCareError.PractitionerOnly);
        await _consultations.DidNotReceive().FindByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

        AssertFailure(await Service().Handle(new SuggestConsultationDiagnosisCommand(41, PractitionerId)),
            NutritionalCareError.ConsultationNotFound);

        _iam.IsPractitioner(21, Arg.Any<CancellationToken>()).Returns(true);
        AssertFailure(await Service().Handle(new SuggestConsultationDiagnosisCommand(ConsultationId, 21)),
            NutritionalCareError.PractitionerOnly);

        _care.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(false);
        AssertFailure(await Service().Handle(Diagnosis()), NutritionalCareError.ActiveCareLinkRequired);

        _care.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>())
            .Returns(ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId));
        AssertFailure(await Service().Handle(Diagnosis()), NutritionalCareError.ConsultationStepOutOfOrder);

        Assert.Empty(_model.Requests);
    }

    // ---- IA-7 ----

    [Fact]
    public async Task Ai_guidelines_are_catalog_codes_returned_in_the_order_of_ev5()
    {
        _consultation.AttachDiagnosis(DiagnosisId);
        _model.Answers("""{ "suggested": ["ReduceSalt", "PrioritizeVegetables", "Drink2LWater"] }""");

        var suggestions = Success(await Service().Handle(Guidelines()));

        Assert.Equal([Guideline.PrioritizeVegetables, Guideline.Drink2LWater, Guideline.ReduceSalt],
            suggestions.Suggested);
        Assert.Equal(DiagnosisSuggestion.AiSource, suggestions.Source);
        Assert.NotNull(suggestions.AiGenerationId);
        Assert.Contains("\"diagnosisCode\":\"OverweightGradeI\"", Assert.Single(_model.Requests).UserContent);
    }

    [Fact]
    public async Task An_ai_answer_outside_the_catalog_falls_back_to_the_fixed_table()
    {
        _consultation.AttachDiagnosis(DiagnosisId);
        _model.Answers("""{ "suggested": ["PrioritizeVegetables", "Camina 30 minutos"] }""");

        var suggestions = Success(await Service().Handle(Guidelines()));

        Assert.Equal(DiagnosisSuggestion.RuleSource, suggestions.Source);
        Assert.Equal(ConfiguredDefaultGuidelinesProvider.Defaults[DiagnosisCode.OverweightGradeI],
            suggestions.Suggested);
        Assert.Null(suggestions.AiGenerationId);
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_log.Rows).Status);
    }

    [Fact]
    public async Task Without_ai_the_guidelines_are_the_fixed_table_of_nc6()
    {
        _configuration["Ai:Enabled"] = "false";
        _consultation.AttachDiagnosis(DiagnosisId);

        var suggestions = Success(await Service().Handle(Guidelines()));

        Assert.Equal(DiagnosisSuggestion.RuleSource, suggestions.Source);
        Assert.Equal([Guideline.PrioritizeVegetables, Guideline.AvoidSugaryDrinks, Guideline.ReduceSalt],
            suggestions.Suggested);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task There_are_no_guideline_suggestions_before_the_diagnosis_of_step_two()
    {
        AssertFailure(await Service().Handle(Guidelines()), NutritionalCareError.ConsultationStepOutOfOrder);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public void The_guideline_validator_rejects_duplicates_and_codes_outside_the_catalog()
    {
        var validator = GuidelineSuggestionsOutputValidator.Instance;

        Assert.Empty(validator.Validate(new GuidelineSuggestionsOutput([Guideline.ReduceSalt])));
        Assert.NotEmpty(validator.Validate(new GuidelineSuggestionsOutput([Guideline.ReduceSalt, Guideline.ReduceSalt])));
        Assert.NotEmpty(validator.Validate(new GuidelineSuggestionsOutput(["DrinkMoreCoffee"])));
        Assert.NotEmpty(validator.Validate(new GuidelineSuggestionsOutput([])));
    }

    private ConsultationAiCommandService Service()
    {
        var settings = new ConfiguredAiSettings(new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build(),
            NullLogger<ConfiguredAiSettings>.Instance);
        var pipeline = new AiGenerationPipeline(settings, _consent,
            PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly), _model, _log, TimeProvider.System,
            NullLogger<AiGenerationPipeline>.Instance);
        return new ConsultationAiCommandService(_consultations, _assessments, _diagnoses,
            new ConfiguredDefaultGuidelinesProvider(new ConfigurationBuilder().Build(),
                NullLogger<ConfiguredDefaultGuidelinesProvider>.Instance),
            _iam, _care, pipeline, NullLogger<ConsultationAiCommandService>.Instance);
    }

    private static SuggestConsultationDiagnosisCommand Diagnosis()
    {
        return new SuggestConsultationDiagnosisCommand(ConsultationId, PractitionerId);
    }

    private static SuggestConsultationGuidelinesCommand Guidelines()
    {
        return new SuggestConsultationGuidelinesCommand(ConsultationId, PractitionerId);
    }

    private static T Success<T>(Result<T, NutritionalCareError> result)
    {
        return Assert.IsType<Result<T, NutritionalCareError>.Success>(result).Value;
    }

    private static void AssertFailure<T>(Result<T, NutritionalCareError> result, NutritionalCareError expected)
    {
        Assert.Equal(expected, Assert.IsType<Result<T, NutritionalCareError>.Failure>(result).Error);
    }
}

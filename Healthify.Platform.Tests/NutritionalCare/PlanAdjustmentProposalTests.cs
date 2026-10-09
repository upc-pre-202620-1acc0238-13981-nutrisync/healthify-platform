using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Infrastructure.Ai.Lexicon;
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
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     IA-8. The plan adjustment proposal, engine of NC-10: through the shared pipeline (real settings, prompts and
///     audit, fake model) and the hard validation of the server: energy within ±25 % of the version in force, the
///     calorie floor by sex, macros coherent within 2 %, no new restriction, catalog guidelines only, and a message for
///     the patient without accusation or diagnosis. The version in force: 1 796 kcal, 90 g protein, 225 g carbohydrate,
///     60 g fat (PR14.IA "Energía diaria 1 796 → 1 650 kcal").
/// </summary>
public class PlanAdjustmentProposalTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    private const string ValidJson = """
        {
          "title": "Ajustar la energía y reforzar las cenas",
          "energyKcal": 1650, "proteinG": 95, "carbG": 190, "fatG": 60,
          "addedGuidelines": ["ProteinAndVegetablesAtDinner"], "removedGuidelines": [],
          "patientMessage": "Notamos que tus cenas son más ligeras. Probemos con estas ideas.",
          "recheckAfterDays": 7,
          "rationale": "Registró 40 % menos de su meta en 5 de 9 días; las cenas aparecen en 2 de esos 5 días.",
          "practitionerLanguage": "es", "patientLanguage": "es"
        }
        """;

    private readonly IAiConsentPolicy _consent = Substitute.For<IAiConsentPolicy>();
    private readonly FakeLanguageModelClient _model = new();
    private readonly InMemoryAiGenerationLog _log = new();
    private readonly Dictionary<string, string?> _configuration = new() { ["Ai:Enabled"] = "true" };

    public PlanAdjustmentProposalTests()
    {
        _consent.IsAllowedAsync(PatientId, Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task A_valid_proposal_comes_back_with_its_generation_and_is_audited_for_the_practitioner()
    {
        _model.Answers(ValidJson);

        var outcome = Success(await Proposer().ProposePlanAdjustment(Input()));

        Assert.Equal(1650m, outcome.Output.EnergyKcal);
        Assert.Equal(["ProteinAndVegetablesAtDinner"], outcome.Output.AddedGuidelines);
        // X-2: @2 asks for each part in the language of its reader.
        Assert.Equal("plan-adjustment-proposal@2", outcome.PromptVersion);
        var row = Assert.Single(_log.Rows);
        Assert.Equal(AiGenerationStatus.Succeeded, row.Status);
        Assert.Equal(PractitionerId, row.RequestedByUserId);
        Assert.Equal(PatientId, row.SubjectPatientId);
    }

    [Fact]
    public async Task What_leaves_for_the_model_is_numbers_and_codes_with_the_safety_bounds()
    {
        _model.Answers(ValidJson);

        await Proposer().ProposePlanAdjustment(Input());

        var content = Assert.Single(_model.Requests).UserContent;
        Assert.Contains("\"minEnergyKcal\":1347", content);
        Assert.Contains("\"maxEnergyKcal\":2245", content);
        Assert.Contains("ObesityGradeI", content);
        Assert.Contains("\"averagePercentFromTarget\":-40", content);
        Assert.DoesNotContain("\"patientId\"", content);
        Assert.DoesNotContain($"\"{PatientId}\"", content);
    }

    [Fact]
    public async Task A_proposal_outside_the_safety_bounds_is_rejected_and_audited_as_rejected()
    {
        // 1 300 kcal is more than 25 % below 1 796.
        _model.Answers(ValidJson.Replace("\"energyKcal\": 1650, \"proteinG\": 95, \"carbG\": 190, \"fatG\": 60",
            "\"energyKcal\": 1300, \"proteinG\": 80, \"carbG\": 150, \"fatG\": 40"));

        var result = await Proposer().ProposePlanAdjustment(Input());

        Assert.Equal(AiError.AiOutputRejected, Failure(result));
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_log.Rows).Status);
    }

    [Fact]
    public async Task Without_the_patients_AI_consent_there_is_no_proposal_and_nothing_is_sent()
    {
        _consent.IsAllowedAsync(PatientId, Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Proposer().ProposePlanAdjustment(Input());

        Assert.Equal(AiError.AiConsentRequired, Failure(result));
        Assert.Empty(_model.Requests);
        Assert.Empty(_log.Rows);
    }

    [Fact]
    public async Task With_the_AI_off_there_is_no_proposal()
    {
        _configuration["Ai:Enabled"] = "false";

        Assert.Equal(AiError.AiFeatureDisabled, Failure(await Proposer().ProposePlanAdjustment(Input())));
        Assert.Empty(_model.Requests);
    }

    // ---- The hard validation (NC-10) ----

    [Theory]
    [InlineData(1347, true)] // exactly 25 % below, rounded up
    [InlineData(1340, false)]
    [InlineData(2245, true)]
    [InlineData(2250, false)]
    public void The_energy_stays_within_25_percent_of_the_version_in_force(decimal energy, bool accepted)
    {
        Assert.Equal(accepted, PlanAdjustmentSafety.IsWithinAdjustmentBand(1796m, energy));
    }

    [Fact]
    public void The_energy_never_goes_below_the_floor_of_the_patients_sex()
    {
        // 1 450 is within 25 % of 1 800 but below the 1 500 floor of a man.
        var validator = Validator(Input(1800m, floor: 1500m));

        var violations = validator.Validate(Output(energy: 1450m, protein: 90m, carb: 160m, fat: 50m));

        Assert.Contains(violations, v => v.Contains("below the floor"));
        Assert.Empty(Validator(Input(1800m, floor: 1200m)).Validate(Output(1450m, 90m, 160m, 50m)));
    }

    [Theory]
    [InlineData(95, 190, 60, true)] // 1 680 kcal for 1 650: +1.8 %
    [InlineData(95, 190, 64, false)] // 1 716 kcal: +4 %
    [InlineData(80, 180, 60, false)] // 1 580 kcal: −4.2 %
    public void The_macros_add_up_to_the_energy_within_2_percent(decimal protein, decimal carb, decimal fat,
        bool coherent)
    {
        Assert.Equal(coherent, PlanAdjustmentSafety.AreMacrosCoherent(1650m, protein, carb, fat));
    }

    [Fact]
    public void Guidelines_come_from_the_catalog_added_only_if_new_and_removed_only_if_present()
    {
        var validator = Validator(Input());

        Assert.Contains(validator.Validate(Output() with { AddedGuidelines = ["EatLessCarbs"] }),
            v => v.Contains("not a code of the guideline catalog"));
        Assert.Contains(validator.Validate(Output() with { AddedGuidelines = ["ReduceSalt"] }),
            v => v.Contains("already in the version in force"));
        Assert.Contains(validator.Validate(Output() with { RemovedGuidelines = ["Drink2LWater"] }),
            v => v.Contains("not in the version in force"));
        // A restriction is not a guideline: the AI cannot add one by any field.
        Assert.Contains(validator.Validate(Output() with { AddedGuidelines = [DietaryRestriction.GlutenFree] }),
            v => v.Contains("not a code of the guideline catalog"));
        Assert.Empty(validator.Validate(Output() with { RemovedGuidelines = ["ReduceSalt"] }));
    }

    [Theory]
    [InlineData("Fallaste otra vez con las cenas; probemos de nuevo.", "accusatory")]
    [InlineData("You failed your dinners this week.", "accusatory")]
    [InlineData("Por tu obesidad, ajustamos las cenas.", "diagnosis")]
    [InlineData("Tu IMC nos pide reforzar las cenas.", "diagnosis")]
    [InlineData("Tu diagnóstico ObesityGradeI pide cambios.", "diagnosis")]
    public void The_message_for_the_patient_invites_and_never_names_the_diagnosis(string message, string kind)
    {
        Assert.Contains(Validator(Input()).Validate(Output() with { PatientMessage = message }),
            v => v.Contains(kind));
    }

    [Fact]
    public void The_message_is_at_most_300_characters_and_the_recheck_between_3_and_30_days()
    {
        var validator = Validator(Input());

        Assert.NotEmpty(validator.Validate(Output() with { PatientMessage = new string('a', 301) }));
        Assert.NotEmpty(validator.Validate(Output() with { RecheckAfterDays = 2 }));
        Assert.NotEmpty(validator.Validate(Output() with { RecheckAfterDays = 31 }));
        Assert.NotEmpty(validator.Validate(Output() with { Rationale = new string('a', 601) }));
        Assert.Empty(validator.Validate(Output() with { RecheckAfterDays = 30 }));
    }

    [Fact]
    public void A_normal_message_is_accepted()
    {
        Assert.Empty(Validator(Input()).Validate(Output()));
    }

    [Theory]
    [InlineData("Female", 1200)]
    [InlineData("male", 1500)]
    [InlineData(null, 1500)]
    public void The_calorie_floor_is_1200_for_a_woman_and_1500_for_a_man_by_default(string? sex, decimal floor)
    {
        Assert.Equal(floor, new ConfiguredCalorieFloorPolicy(new ConfigurationBuilder().Build()).FloorFor(sex));
    }

    [Fact]
    public void The_calorie_floor_is_configurable()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["NutritionalCare:CalorieFloorKcal:Female"] = "1300" }).Build();

        Assert.Equal(1300m, new ConfiguredCalorieFloorPolicy(configuration).FloorFor(BiologicalSex.Female));
    }

    [Fact]
    public void The_embedded_lexicon_loads_both_sections()
    {
        Assert.Contains("fallaste", EmbeddedPatientMessageLexicon.Instance.AccusatoryTerms);
        Assert.Contains("obesidad", EmbeddedPatientMessageLexicon.Instance.DiagnosisTerms);
    }

    internal static PlanAdjustmentInput Input(decimal currentEnergy = 1796m, decimal floor = 1200m)
    {
        return new PlanAdjustmentInput(PatientId, PractitionerId, "es",
            new PlanAdjustmentEvidence(-40m, 5, 9, "Below"),
            new PlanAdjustmentMealSlots(5, 5, 5, 2, 1),
            new PlanAdjustmentWeightTrend(-0.2m, -0.8m, 12),
            new PlanAdjustmentCurrentPlan(3, currentEnergy, 90m, 225m, 60m, [Guideline.ReduceSalt],
                [DietaryRestriction.LactoseFree]),
            DiagnosisCode.ObesityGradeI, floor);
    }

    private static PlanAdjustmentProposalOutput Output(decimal energy = 1650m, decimal protein = 95m,
        decimal carb = 190m, decimal fat = 60m)
    {
        return new PlanAdjustmentProposalOutput("Ajustar la energía y reforzar las cenas", energy, protein, carb, fat,
            [Guideline.ProteinAndVegetablesAtDinner], [],
            "Notamos que tus cenas son más ligeras. Probemos con estas ideas.", 7, "Fundamento.", "es", "es");
    }

    private static PlanAdjustmentProposalOutputValidator Validator(PlanAdjustmentInput input)
    {
        return new PlanAdjustmentProposalOutputValidator(input, EmbeddedPatientMessageLexicon.Instance);
    }

    private PlanAdjustmentProposer Proposer()
    {
        var settings = new ConfiguredAiSettings(new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build(),
            NullLogger<ConfiguredAiSettings>.Instance);
        var pipeline = new AiGenerationPipeline(settings, _consent,
            PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly), _model, _log, TimeProvider.System,
            NullLogger<AiGenerationPipeline>.Instance);
        return new PlanAdjustmentProposer(pipeline, EmbeddedPatientMessageLexicon.Instance);
    }

    private static AiGenerationOutcome<PlanAdjustmentProposalOutput> Success(
        Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError> result)
    {
        return Assert.IsType<Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError>.Success>(result).Value;
    }

    private static AiError Failure(Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError> result)
    {
        return Assert.IsType<Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError>.Failure>(result).Error;
    }
}

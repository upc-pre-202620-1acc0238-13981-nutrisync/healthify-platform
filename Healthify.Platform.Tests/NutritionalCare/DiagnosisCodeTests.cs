using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-4. The closed diagnosis list of EV-3, who chose it, the deterministic rationale and the
///     invariants of a coded diagnosis.
/// </summary>
public class DiagnosisCodeTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    [Fact]
    public void The_list_is_closed_to_the_six_codes_of_EV_3()
    {
        Assert.Equal(
        [
            "Underweight", "NormalWeight", "OverweightGradeI", "ObesityGradeI", "ObesityGradeII", "ObesityGradeIII"
        ], DiagnosisCode.All);
    }

    [Theory]
    [InlineData("Other")]
    [InlineData("Sobrepeso grado I")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_code_is_rejected(string? code)
    {
        Assert.Throws<ArgumentException>(() => new DiagnosisCode(code!));
    }

    [Fact]
    public void A_code_is_normalised_ignoring_case_and_spaces()
    {
        Assert.Equal(DiagnosisCode.OverweightGradeI, new DiagnosisCode(" overweightgradei ").Value);
    }

    [Theory]
    [InlineData(18.4, "Underweight")]
    [InlineData(18.5, "NormalWeight")]
    [InlineData(24.9, "NormalWeight")]
    [InlineData(26.3, "OverweightGradeI")]
    [InlineData(30.0, "ObesityGradeI")]
    [InlineData(35.0, "ObesityGradeII")]
    [InlineData(40.0, "ObesityGradeIII")]
    public void The_deterministic_suggestion_is_the_WHO_category_of_the_index(decimal bmi, string expected)
    {
        Assert.Equal(expected, DiagnosisCode.FromBodyMassIndex(bmi).Value);
        Assert.Equal(BodyMassIndex.CategoryFor(bmi), DiagnosisCode.FromBodyMassIndex(bmi).Value);
    }

    [Fact]
    public void The_source_is_closed_to_accepted_suggestion_or_practitioner_choice()
    {
        Assert.Equal(["AiSuggestionAccepted", "PractitionerSelected"], DiagnosisSource.All);
        Assert.Throws<ArgumentException>(() => new DiagnosisSource("Rule"));
    }

    [Fact]
    public void The_deterministic_rationale_is_written_from_the_measurement()
    {
        Assert.Equal("IMC 26.3 kg/m²; cintura 88 cm", ClinicalRationale.FromMeasurement(26.3m, 88.00m, null).Value);
        Assert.Equal("IMC 26.0 kg/m²; cintura 88.5 cm; grasa corporal 31 %",
            ClinicalRationale.FromMeasurement(26m, 88.5m, 31m).Value);
        Assert.Equal("IMC 22.1 kg/m²", ClinicalRationale.FromMeasurement(22.1m, null, null).Value);
    }

    [Fact]
    public void Choosing_a_code_without_a_rationale_writes_it_from_the_measurement()
    {
        var assessment = Assessment();

        var diagnosis = NutritionalDiagnosis.FromConsultation(10, 20, assessment.Id.Value,
            new DiagnosisCode("OverweightGradeI"), new DiagnosisSource("PractitionerSelected"), null, null,
            assessment.LatestMeasurement!);

        Assert.Equal("OverweightGradeI", diagnosis.Code!.Value);
        Assert.Equal("OverweightGradeI", diagnosis.Statement);
        Assert.Equal("PractitionerSelected", diagnosis.Source!.Value);
        Assert.Equal("IMC 26.3 kg/m²; cintura 88 cm", diagnosis.Rationale.Value);
        Assert.Equal(26.3m, diagnosis.BmiAtIssue);
        Assert.Null(diagnosis.AiGenerationId);
        Assert.True(diagnosis.IsActive);
    }

    [Fact]
    public void A_written_rationale_is_kept_as_written()
    {
        var assessment = Assessment();

        var diagnosis = NutritionalDiagnosis.FromConsultation(10, 20, assessment.Id.Value,
            new DiagnosisCode("ObesityGradeI"), new DiagnosisSource("PractitionerSelected"), null,
            "Cintura y antecedentes justifican el grado.", assessment.LatestMeasurement!);

        Assert.Equal("Cintura y antecedentes justifican el grado.", diagnosis.Rationale.Value);
    }

    [Theory]
    [InlineData(null, "IMC 26.3")]
    [InlineData(7L, null)]
    [InlineData(0L, "IMC 26.3")]
    public void An_accepted_AI_suggestion_keeps_its_generation_and_its_rationale(long? generationId,
        string? rationale)
    {
        var assessment = Assessment();

        Assert.Throws<ArgumentException>(() => NutritionalDiagnosis.FromConsultation(10, 20, assessment.Id.Value,
            new DiagnosisCode("OverweightGradeI"), new DiagnosisSource("AiSuggestionAccepted"), generationId,
            rationale, assessment.LatestMeasurement!));
    }

    [Fact]
    public void The_original_free_text_diagnosis_still_requires_its_rationale()
    {
        Assert.Throws<ArgumentException>(() =>
            new NutritionalDiagnosis(new IssueDiagnosisCommand(10, 20, 30, "Overweight grade I", null)));

        var legacy = new NutritionalDiagnosis(new IssueDiagnosisCommand(10, 20, 30, "Overweight grade I",
            "BMI 26.3 kg/m2"));
        Assert.Null(legacy.Code);
        Assert.Null(legacy.Source);
        Assert.Equal("Overweight grade I", legacy.Statement);
    }

    private static NutritionalAssessment Assessment()
    {
        var baseline = ConsultationScenario.Baseline(10, 20, new DateOnly(1995, 3, 10), "Female", 168m, Today);
        return ConsultationScenario.MeasuredAssessment(100, 40, 20, baseline, Today, 74.2m);
    }
}

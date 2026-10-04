using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-6. The fixed table DiagnosisCode → guideline codes (NutritionalCare:DefaultGuidelinesByDiagnosis),
///     the deterministic base of IA-7: only catalog codes ever come out of it.
/// </summary>
public class DefaultGuidelinesProviderTests
{
    [Fact]
    public void Every_diagnosis_has_defaults_and_they_are_all_catalog_codes()
    {
        var provider = Provider();

        Assert.All(DiagnosisCode.All, code =>
        {
            var guidelines = provider.For(new DiagnosisCode(code));
            Assert.NotEmpty(guidelines);
            Assert.All(guidelines, g => Assert.Contains(g, Guideline.Codes));
        });
        Assert.Equal(["PrioritizeVegetables", "AvoidSugaryDrinks", "ReduceSalt"],
            provider.For(new DiagnosisCode(DiagnosisCode.OverweightGradeI)));
    }

    [Fact]
    public void Configuration_replaces_the_row_and_unknown_codes_are_ignored()
    {
        var provider = Provider(new Dictionary<string, string?>
        {
            ["NutritionalCare:DefaultGuidelinesByDiagnosis:OverweightGradeI:0"] = "Drink2LWater",
            ["NutritionalCare:DefaultGuidelinesByDiagnosis:OverweightGradeI:1"] = "Camina más",
            ["NutritionalCare:DefaultGuidelinesByDiagnosis:NormalWeight:0"] = "Nada válido"
        });

        Assert.Equal(["Drink2LWater"], provider.For(new DiagnosisCode(DiagnosisCode.OverweightGradeI)));
        Assert.Equal(ConfiguredDefaultGuidelinesProvider.Defaults[DiagnosisCode.NormalWeight],
            provider.For(new DiagnosisCode(DiagnosisCode.NormalWeight)));
    }

    [Fact]
    public async Task The_consultation_gets_the_guidelines_of_its_diagnosis()
    {
        var today = new DateOnly(2026, 3, 10);
        var baseline = ConsultationScenario.Baseline(10, 20, new DateOnly(1995, 3, 10), "Female", 168m, today);
        var assessment = ConsultationScenario.MeasuredAssessment(100, 40, 20, baseline, today, 74.2m);
        var consultation = ConsultationScenario.Started(40, 10, 20);
        consultation.AttachAssessment(100);
        consultation.AttachDiagnosis(2);
        var consultations = Substitute.For<IConsultationRepository>();
        consultations.FindByIdAsync(40, Arg.Any<CancellationToken>()).Returns(consultation);
        var diagnoses = Substitute.For<INutritionalDiagnosisRepository>();
        diagnoses.FindByIdAsync(2, Arg.Any<CancellationToken>())
            .Returns(ConsultationScenario.Diagnosis(2, assessment, DiagnosisCode.ObesityGradeII));
        var service = new ConsultationQueryService(consultations, Substitute.For<INutritionalAssessmentRepository>(),
            diagnoses, Provider(), Substitute.For<INutritionPlanRepository>(), Substitute.For<IMonitoringContextFacade>());

        var suggested = await service.Handle(new GetConsultationGuidelineSuggestionsQuery(40));

        Assert.Equal(ConfiguredDefaultGuidelinesProvider.Defaults[DiagnosisCode.ObesityGradeII], suggested);
        Assert.Empty(await service.Handle(new GetConsultationGuidelineSuggestionsQuery(41)));
    }

    private static ConfiguredDefaultGuidelinesProvider Provider(Dictionary<string, string?>? values = null)
    {
        return new ConfiguredDefaultGuidelinesProvider(
            new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build(),
            NullLogger<ConfiguredDefaultGuidelinesProvider>.Instance);
    }
}

using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     RM-3/RM-4. What Nutritional Care publishes about measurements: the evaluations in force (completed
///     consultations, oldest first, the first one flagged) and the active diagnosis for the practitioner's record,
///     and the latest clinical measurement without any BMI for everyone else.
/// </summary>
public class ClinicalEvaluationsFacadeTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly InMemoryNutritionalCare _care = new(Today);

    public ClinicalEvaluationsFacadeTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    [Fact]
    public async Task Only_completed_consultations_are_evaluations_and_the_first_is_flagged()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first");
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second");
        // A third one, still at step 1: not an evaluation yet.
        var third = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, third, PractitionerId, 70m);

        var facade = _care.Facade(Today);
        var evaluations = await facade.GetClinicalEvaluations(PatientId);
        var latest = await facade.GetLatestClinicalMeasurement(PatientId);
        var diagnosis = await facade.GetActiveDiagnosis(PatientId);

        Assert.Equal([80m, 74.2m], evaluations.Select(e => e.WeightKg));
        Assert.Equal([true, false], evaluations.Select(e => e.IsFirst));
        Assert.All(evaluations, e => Assert.False(string.IsNullOrEmpty(e.BmiCategory)));
        Assert.Equal(74.2m, latest!.WeightKg);
        Assert.Equal(DiagnosisCode.OverweightGradeI, diagnosis!.Code);
    }

    [Fact]
    public async Task A_pending_diagnosis_is_not_the_active_one()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first");
        var second = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, second, PractitionerId, 74.2m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, second, PractitionerId,
            DiagnosisCode.OverweightGradeI);

        var diagnosis = await _care.Facade(Today).GetActiveDiagnosis(PatientId);

        Assert.Equal(DiagnosisCode.ObesityGradeI, diagnosis!.Code);
    }

    [Fact]
    public async Task Without_measurements_there_is_nothing_and_nothing_throws()
    {
        var facade = _care.Facade(Today);

        Assert.Empty(await facade.GetClinicalEvaluations(PatientId));
        Assert.Null(await facade.GetLatestClinicalMeasurement(PatientId));
        Assert.Null(await facade.GetActiveDiagnosis(PatientId));
    }

    [Fact]
    public void The_measurement_everyone_may_read_has_no_bmi()
    {
        Assert.DoesNotContain(typeof(ClinicalMeasurementItem).GetProperties(),
            p => p.Name.Contains("Bmi", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Contains("Category", StringComparison.OrdinalIgnoreCase));
    }
}

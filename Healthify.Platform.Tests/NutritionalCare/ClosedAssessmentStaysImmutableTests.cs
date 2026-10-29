using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     Business rule Closed Assessment Is Immutable (Subflow 3.1) after the structured assessment: it holds for assessments
///     recorded through the stand-alone endpoint and for those recorded in a guided consultation, and
///     editing the baseline afterwards does not reach a closed assessment.
/// </summary>
public class ClosedAssessmentStaysImmutableTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int AssessmentId = 30;
    private static readonly DateOnly ConsultationDate = new(2026, 10, 5);

    [Fact]
    public void A_closed_consultation_assessment_accepts_no_measurement_of_either_kind()
    {
        var assessment = ConsultationAssessment(Baseline());
        assessment.TakeStructuredMeasurement(74.2m, new HeightCm(168m), Checks(), null, 88m);
        assessment.Close();

        Assert.Throws<InvalidOperationException>(() =>
            assessment.TakeStructuredMeasurement(75m, new HeightCm(168m), Checks(), null, null));
        Assert.Throws<InvalidOperationException>(() =>
            assessment.TakeClinicalMeasurement(new TakeClinicalMeasurementCommand(AssessmentId, PractitionerId, 75m,
                168m, "Fasting", null, null)));
        Assert.Single(assessment.Measurements);
    }

    [Fact]
    public void An_assessment_cannot_be_closed_twice()
    {
        var assessment = ConsultationAssessment(Baseline());
        assessment.Close();
        var closedAt = assessment.ClosedAt;

        Assert.Throws<InvalidOperationException>(assessment.Close);
        Assert.Equal(closedAt, assessment.ClosedAt);
    }

    [Fact]
    public void Editing_the_baseline_does_not_rewrite_a_closed_assessment()
    {
        var baseline = Baseline();
        var assessment = ConsultationAssessment(baseline);
        var measurement = assessment.TakeStructuredMeasurement(74.2m, baseline.Height, Checks(), null, null);
        assessment.Close();

        baseline.Update(new UpdatePatientBaselineCommand(PatientId, PractitionerId, new DateOnly(1980, 1, 1), "Male",
            180m, ["Gout"]), ConsultationDate);

        Assert.Equal(31, assessment.AgeYears);
        Assert.Equal("Female", assessment.BiologicalSex.Value);
        Assert.Equal(["Hypothyroidism"], assessment.ConditionsSnapshot);
        Assert.Equal(168m, measurement.HeightCm);
        Assert.Equal(26.3m, measurement.BmiKgM2);
    }

    private static PatientBaseline Baseline()
    {
        return new PatientBaseline(new RecordPatientBaselineCommand(PatientId, PractitionerId,
            new DateOnly(1995, 3, 10), "Female", 168m, ["Hypothyroidism"]), ConsultationDate);
    }

    private static NutritionalAssessment ConsultationAssessment(PatientBaseline baseline)
    {
        return NutritionalAssessment.ForConsultation(1, PractitionerId, baseline, ConsultationDate,
            new ActivityLevel(ActivityLevel.Moderate), null, null, null);
    }

    private static MeasurementProtocolChecklist Checks()
    {
        return new MeasurementProtocolChecklist([MeasurementProtocolChecklist.Fasting]);
    }
}

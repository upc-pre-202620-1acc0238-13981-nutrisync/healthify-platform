using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     Builds the aggregates of a guided consultation already past its first steps, as the database would
///     hold them, so the tests of a later step do not have to run the earlier ones.
/// </summary>
public static class ConsultationScenario
{
    public static PatientBaseline Baseline(int patientId, int practitionerId, DateOnly birthDate, string sex,
        decimal heightCm, DateOnly today)
    {
        return new PatientBaseline(new RecordPatientBaselineCommand(patientId, practitionerId, birthDate, sex,
            heightCm, ["Hypothyroidism"]), today);
    }

    public static Consultation Started(int consultationId, int patientId, int practitionerId)
    {
        return Identity.Assign(new Consultation(new StartConsultationCommand(patientId, practitionerId), false),
            new ConsultationId(consultationId));
    }

    /// <summary>The closed assessment of step 1, with one structured measurement.</summary>
    public static NutritionalAssessment MeasuredAssessment(int assessmentId, int consultationId, int practitionerId,
        PatientBaseline baseline, DateOnly consultationDate, decimal weightKg, decimal? waistCm = 88m,
        string activityLevel = ActivityLevel.Moderate, decimal? bodyFat = null)
    {
        var assessment = NutritionalAssessment.ForConsultation(consultationId, practitionerId, baseline,
            consultationDate, new ActivityLevel(activityLevel), null, null, null);
        assessment.TakeStructuredMeasurement(weightKg, baseline.Height, new MeasurementProtocolChecklist(["Fasting"]),
            bodyFat, waistCm);
        assessment.Close();
        return Identity.Assign(assessment, new AssessmentId(assessmentId));
    }

    /// <summary>An active coded diagnosis, as issued by an earlier consultation.</summary>
    public static NutritionalDiagnosis Diagnosis(int diagnosisId, NutritionalAssessment assessment, string code)
    {
        var diagnosis = NutritionalDiagnosis.FromConsultation(assessment.PatientId, assessment.PractitionerId,
            assessment.Id.Value, new DiagnosisCode(code), new DiagnosisSource(DiagnosisSource.PractitionerSelected),
            null, null, assessment.LatestMeasurement!);
        return Identity.Assign(diagnosis, new DiagnosisId(diagnosisId));
    }

    /// <summary>NC-7. The diagnosis of step 2, pending until the consultation publishes its plan.</summary>
    public static NutritionalDiagnosis PendingDiagnosis(int diagnosisId, NutritionalAssessment assessment,
        string code, int consultationId)
    {
        var diagnosis = NutritionalDiagnosis.FromConsultation(assessment.PatientId, assessment.PractitionerId,
            assessment.Id.Value, new DiagnosisCode(code), new DiagnosisSource(DiagnosisSource.PractitionerSelected),
            null, null, assessment.LatestMeasurement!, consultationId);
        return Identity.Assign(diagnosis, new DiagnosisId(diagnosisId));
    }

    /// <summary>NC-7. A consultation with steps 1 to 3 saved: the next step is the publication.</summary>
    public static Consultation AtPublication(int consultationId, int patientId, int practitionerId, int assessmentId,
        int diagnosisId, int planId)
    {
        var consultation = Started(consultationId, patientId, practitionerId);
        consultation.AttachAssessment(assessmentId);
        consultation.AttachDiagnosis(diagnosisId);
        consultation.AttachPlanDraft(planId);
        return consultation;
    }
}

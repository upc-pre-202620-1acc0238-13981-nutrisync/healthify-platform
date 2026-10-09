using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST.Resources;

namespace Healthify.Platform.ReadModels.Interfaces.REST.Transform;

/// <summary>RM-4. PAC-3: the record plus what only the practitioner reads.</summary>
public static class PractitionerPatientRecordResourceAssembler
{
    public static PractitionerPatientRecordResource ToResource(PatientRecordComposition composition,
        PractitionerRecordSection section)
    {
        var evaluations = section.Evaluations;
        var first = evaluations.Count > 0 ? evaluations[0] : null;
        var latest = evaluations.Count > 0 ? evaluations[^1] : null;

        return new PractitionerPatientRecordResource(
            PatientRecordResourceAssembler.ToResource(composition),
            section.ActiveDiagnosis is null
                ? null
                : new RecordActiveDiagnosisResource(section.ActiveDiagnosis.DiagnosisId, section.ActiveDiagnosis.Code,
                    section.ActiveDiagnosis.Statement, section.ActiveDiagnosis.IssuedAt),
            evaluations
                .Select(e => new RecordEvaluationResource(e.AssessmentId, e.TakenAt, e.WeightKg, e.BmiKgM2,
                    e.BmiCategory, e.WaistCircumferenceCm, e.IsFirst))
                .Reverse()
                .ToList(),
            latest is null || first is null
                ? null
                : new RecordClinicalWeightResource(latest.WeightKg, latest.TakenAt, latest.WeightKg - first.WeightKg,
                    first.TakenAt),
            latest is null ? null : new RecordBmiResource(latest.BmiKgM2, latest.BmiCategory),
            ToCompliance(section.ComplianceFrom, section.Compliance));
    }

    internal static RecordComplianceResource? ToCompliance(DateOnly from, ComplianceSummaryItem? summary)
    {
        return summary is null
            ? null
            : new RecordComplianceResource(from, from.AddDays(summary.TotalDays - 1), summary.Met, summary.TotalDays);
    }
}

/// <summary>
///     RM-4. PT20: the record plus what the patient reads about themselves. It receives nothing that could carry a
///     diagnosis, a BMI or a BMI category.
/// </summary>
public static class PatientOwnRecordResourceAssembler
{
    public static PatientOwnRecordResource ToResource(PatientRecordComposition composition,
        PatientOwnRecordSection section)
    {
        var care = composition.Care;
        var targets = composition.Intervention;

        return new PatientOwnRecordResource(
            PatientRecordResourceAssembler.ToResource(composition),
            care is null || section.Practitioner is null
                ? null
                : new RecordPractitionerResource(care.PractitionerId, section.Practitioner.FullName,
                    care.EstablishedAt, care.IsActive ? "Active" : "PendingConsent"),
            section.NextFollowUp is null
                ? null
                : new RecordNextFollowUpResource(section.NextFollowUp.FollowUpId, section.NextFollowUp.ScheduledFor,
                    section.NextFollowUp.Modality, section.NextFollowUp.Preparation),
            new RecordMyNumbersResource(
                targets?.EnergyKcal,
                targets?.PlanVersion,
                PractitionerPatientRecordResourceAssembler.ToCompliance(section.ComplianceFrom, section.Compliance),
                section.ClinicalWeight is null
                    ? null
                    : new RecordPatientClinicalWeightResource(section.ClinicalWeight.WeightKg,
                        section.ClinicalWeight.TakenAt),
                section.WeightTrend?.SlopeKgPerWeek),
            targets is null
                ? null
                : new RecordPlanResource(
                    (targets.GuidelineItems ?? targets.Guidelines.Select(g =>
                        new ActiveGuidelineItem(null, g)).ToList())
                    .Select(g => new RecordGuidelineResource(g.Code, g.Custom)).ToList(),
                    targets.Restrictions,
                    targets.LegacyRestrictions ?? []),
            composition.FollowUp.Referrals.Select(PatientRecordResourceAssembler.ToResource).ToList());
    }
}

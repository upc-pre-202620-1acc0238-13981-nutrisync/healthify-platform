using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

namespace Healthify.Platform.ReadModels.Application;

/// <summary>
///     The unified record of one patient, assembled from five contexts.
/// </summary>
/// <remarks>
///     The sections are named after the four phases of the process the expert dictated: assessment,
///     diagnosis, intervention and follow-up. Three of them are here.
///     The second one is not, and its absence is the design rather than a gap. A nutritional
///     diagnosis and the calculation basis behind a target never leave Nutritional Care, so no ACL
///     contract publishes them, so no composer can reach them. What crossed the boundary from the
///     assessment phase is the clinical anthropometry, which crossed as an event and is read back
///     here from the context that stores it.
///     Every section is nullable or a list, and a facade that cannot answer yields an empty section
///     rather than a failed record: a page assembled from five contexts should not disappear because
///     one of them is quiet.
/// </remarks>
/// <param name="PatientId">Whose record.</param>
/// <param name="Identity">Who they are, or null when the account cannot be read.</param>
/// <param name="Care">The care link that currently grants access, or null when there is none.</param>
/// <param name="Assessment">Phase one: the clinical weight series.</param>
/// <param name="Intervention">Phase three: the published contract, or null when nothing is published.</param>
/// <param name="FollowUp">Phase four: how it has been going.</param>
public record PatientRecordComposition(
    int PatientId,
    UserIdentityItem? Identity,
    CareLinkStatusItem? Care,
    PatientRecordAssessmentSection Assessment,
    ActiveTargetsItem? Intervention,
    PatientRecordFollowUpSection FollowUp);

/// <summary>
///     Phase one of the record.
/// </summary>
/// <remarks>
///     Clinical measurements only. The readings the patient takes at home live in the follow-up
///     section as a smoothed trend, and the two series are never merged: a bathroom scale does not
///     acquire clinical authority by being displayed next to something that has it.
/// </remarks>
/// <param name="ClinicalAnthropometrySeries">Weight taken by the practitioner, oldest first.</param>
public record PatientRecordAssessmentSection(
    IReadOnlyList<AnthropometryPointItem> ClinicalAnthropometrySeries);

/// <summary>
///     Phase four of the record.
/// </summary>
/// <remarks>
///     The consistency state carries the two dates that make its order readable: the patient is
///     asked first, and only after three weeks in alert does a practitioner hear about it. A record
///     that showed the escalation without the prompt would be showing surveillance.
/// </remarks>
/// <param name="DailyCompliance">Day by day, oldest first. Unlogged days included as such.</param>
/// <param name="SelfWeighInTrend">The smoothed home series. Never a single day as a headline.</param>
/// <param name="Consistency">Where the consistency index stands, or null when there is none.</param>
/// <param name="Referrals">Where the patient was sent, most recent first.</param>
public record PatientRecordFollowUpSection(
    IReadOnlyList<DailyComplianceItem> DailyCompliance,
    IReadOnlyList<WeightTrendPointItem> SelfWeighInTrend,
    ConsistencyStateItem? Consistency,
    IReadOnlyList<ReferralItem> Referrals);

/// <summary>
///     RM-4. What only the practitioner reads in PAC-3: the active diagnosis, the evaluations with their BMI and
///     the compliance of the last seven days.
/// </summary>
/// <param name="ActiveDiagnosis">"Diagnóstico activo · solo profesional", or null.</param>
/// <param name="Evaluations">"EVALUACIONES", oldest first.</param>
/// <param name="ComplianceFrom">First of the last seven days.</param>
/// <param name="Compliance">"Cumplimiento 5 de 7 días", or null.</param>
public record PractitionerRecordSection(
    ActiveDiagnosisItem? ActiveDiagnosis,
    IReadOnlyList<ClinicalEvaluationItem> Evaluations,
    DateOnly ComplianceFrom,
    ComplianceSummaryItem? Compliance);

/// <summary>
///     RM-4. What the patient reads in PT20 about themselves. Built without any call that returns a diagnosis, a
///     BMI or a BMI category (§12-#9): the patient record cannot carry them because it never receives them.
/// </summary>
/// <param name="Practitioner">"Tu nutricionista", or null when the account cannot be read.</param>
/// <param name="NextFollowUp">"Mis consultas · Próxima", or null.</param>
/// <param name="ComplianceFrom">First of the last seven days.</param>
/// <param name="Compliance">"Mi cumplimiento 5 de 7 días", or null.</param>
/// <param name="ClinicalWeight">"Peso clínico 74.2 kg · Evaluación del 3 mar.", or null.</param>
/// <param name="WeightTrend">"Mi tendencia −0,3 kg/sem", or null.</param>
public record PatientOwnRecordSection(
    UserIdentityItem? Practitioner,
    NextFollowUpItem? NextFollowUp,
    DateOnly ComplianceFrom,
    ComplianceSummaryItem? Compliance,
    ClinicalMeasurementItem? ClinicalWeight,
    WeightTrendSummaryItem? WeightTrend);

/// <summary>
///     Composes the Patient Record read model.
/// </summary>
/// <remarks>
///     This class is the whole of the composition layer for this view, and what it can reach is
///     exactly what the five ACL contracts publish. It injects no repository, no query service and
///     no DbContext, which is what stops a composite view from becoming a sixth bounded context with
///     opinions of its own.
///     Read models are not a bounded context. There is no aggregate here, no command, no event, no
///     table and no error enum.
/// </remarks>
public class PatientRecordComposer(
    IIamContextFacade iamContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IIntakeContextFacade intakeContextFacade,
    IMonitoringContextFacade monitoringContextFacade,
    TimeProvider timeProvider)
{
    /// <summary>RM-4. "Cumplimiento 5 de 7 días · Últimos 7 días".</summary>
    public const int ComplianceDays = 7;

    /// <summary>RM-4. "Mi tendencia" (PT20), as PAC-2: four weeks.</summary>
    public const int WeightTrendWeeks = 4;

    /// <summary>How much history the record carries when the caller does not say.</summary>
    /// <remarks>
    ///     NOTE: technical constant, not part of the domain model. It bounds the two series so that a
    ///     long relationship does not return an unbounded page; the caller can ask for more.
    /// </remarks>
    public const int DefaultDays = 30;

    /// <summary>
    ///     Assembles the record. Never throws: every facade degrades on its own, and a section that
    ///     could not be read comes back empty.
    /// </summary>
    /// <param name="patientId">Whose record.</param>
    /// <param name="days">How much of the two series to carry.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<PatientRecordComposition> Compose(int patientId, int days = DefaultDays,
        CancellationToken ct = default)
    {
        var window = days <= 0 ? DefaultDays : days;

        var identity = await iamContextFacade.GetUserById(patientId, ct);
        var care = await careRelationshipContextFacade.GetActiveCareLinkByPatientId(patientId, ct);

        // Phase one. Only what crossed the boundary as a clinical measurement.
        var clinicalSeries = await monitoringContextFacade.GetAnthropometrySeriesPoints(patientId, ct);

        // Phase three. Targets, guidelines and restrictions, which is all the published contract is.
        // There is no diagnosis and no calculation basis on it, here or on the contract itself.
        var intervention = await nutritionalCareContextFacade.GetActiveTargetsByPatientId(patientId, ct);

        // Phase four.
        var dailyCompliance = await monitoringContextFacade.GetDailyComplianceSeries(patientId, window, ct);
        var selfWeighInTrend = await intakeContextFacade.GetWeightTrendPoints(patientId, window, ct);
        var consistency = await monitoringContextFacade.GetConsistencyState(patientId, ct);
        var referrals = await monitoringContextFacade.GetReferrals(patientId, ct);

        return new PatientRecordComposition(
            patientId,
            identity,
            care,
            new PatientRecordAssessmentSection(clinicalSeries),
            intervention,
            new PatientRecordFollowUpSection(dailyCompliance, selfWeighInTrend, consistency, referrals));
    }

    /// <summary>
    ///     RM-4 - PAC-3. The record plus what only the practitioner reads. Never throws.
    /// </summary>
    public async Task<(PatientRecordComposition Record, PractitionerRecordSection Practitioner)>
        ComposeForPractitioner(int patientId, int days = DefaultDays, CancellationToken ct = default)
    {
        var record = await Compose(patientId, days, ct);

        var diagnosis = await nutritionalCareContextFacade.GetActiveDiagnosis(patientId, ct);
        var evaluations = await nutritionalCareContextFacade.GetClinicalEvaluations(patientId, ct);
        var (from, today) = LastDays();
        var compliance = await monitoringContextFacade.GetComplianceSummary(patientId, from, today, ct);

        return (record, new PractitionerRecordSection(diagnosis, evaluations, from, compliance));
    }

    /// <summary>
    ///     RM-4 - PT20. The record plus what the patient reads about themselves. Never throws. It does not call
    ///     GetActiveDiagnosis or GetClinicalEvaluations: a diagnosis, a BMI or a BMI category never reaches it.
    /// </summary>
    public async Task<(PatientRecordComposition Record, PatientOwnRecordSection Own)> ComposeForPatient(
        int patientId, int days = DefaultDays, CancellationToken ct = default)
    {
        var record = await Compose(patientId, days, ct);

        var practitioner = record.Care is null
            ? null
            : await iamContextFacade.GetUserById(record.Care.PractitionerId, ct);
        var nextFollowUps = await monitoringContextFacade.GetNextFollowUpsByPatientIds([patientId], ct);
        var (from, today) = LastDays();
        var compliance = await monitoringContextFacade.GetComplianceSummary(patientId, from, today, ct);
        var clinicalWeight = await nutritionalCareContextFacade.GetLatestClinicalMeasurement(patientId, ct);
        var weightTrend = await intakeContextFacade.GetWeightTrendSummary(patientId, WeightTrendWeeks, ct);

        return (record, new PatientOwnRecordSection(practitioner, nextFollowUps.GetValueOrDefault(patientId), from,
            compliance, clinicalWeight, weightTrend));
    }

    /// <summary>The last seven calendar days up to today (DECISIÓN §12-#7: calendar days).</summary>
    private (DateOnly From, DateOnly To) LastDays()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        return (today.AddDays(-(ComplianceDays - 1)), today);
    }
}

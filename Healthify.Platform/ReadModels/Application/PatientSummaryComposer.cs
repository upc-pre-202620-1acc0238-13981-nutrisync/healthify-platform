using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

namespace Healthify.Platform.ReadModels.Application;

/// <summary>
///     RM-2. The summary of one patient the practitioner opens from the roster (PAC-1), or the empty state of a
///     patient without baseline (PAC-0).
/// </summary>
/// <remarks>
///     Every section is nullable and degrades on its own: a facade that cannot answer leaves its section empty
///     rather than failing the page. There is no diagnosis, rationale or calculation basis here, because no
///     contract this composer can reach publishes them.
/// </remarks>
/// <param name="PatientId">Whose summary.</param>
/// <param name="Identity">"Ana Flores", or null when Iam cannot answer.</param>
/// <param name="Care">The link that grants access ("vinculada desde 12 mar. 2026"), or null.</param>
/// <param name="ActivePlanVersion">"Plan versión 3", or null when nothing is published ("sin plan").</param>
/// <param name="Baseline">"Mujer · 31 años · 168 cm · hipotiroidismo"; null means PAC-0.</param>
/// <param name="NextFollowUp">"Próxima consulta", or null.</param>
/// <param name="ConsultationInProgress">PAC-1.C "Consulta en curso · Paso 1 de 4", or null.</param>
/// <param name="SinceLastConsultation">"DESDE LA ÚLTIMA CONSULTA".</param>
public record PatientSummaryComposition(
    int PatientId,
    UserIdentityItem? Identity,
    CareLinkStatusItem? Care,
    int? ActivePlanVersion,
    PatientBaselineSummaryItem? Baseline,
    NextFollowUpItem? NextFollowUp,
    ConsultationInProgressItem? ConsultationInProgress,
    SinceLastConsultationSection SinceLastConsultation);

/// <summary>
///     RM-2. PAC-1 "DESDE LA ÚLTIMA CONSULTA: Peso (autopesaje) −0,3 kg/sem · Tendencia · 4 semanas; Cumplimiento
///     5 de 7 días".
/// </summary>
/// <param name="FromDate">The first day the section covers.</param>
/// <param name="LastConsultationAt">When the last consultation was published, or null when there is none.</param>
/// <param name="TrendWeeks">How many weeks the weight trend covers.</param>
/// <param name="WeightTrend">The smoothed home series over those weeks, or null when there is none.</param>
/// <param name="Compliance">The days from <paramref name="FromDate" /> to today, counted, or null.</param>
public record SinceLastConsultationSection(
    DateOnly FromDate,
    DateTimeOffset? LastConsultationAt,
    int TrendWeeks,
    WeightTrendSummaryItem? WeightTrend,
    ComplianceSummaryItem? Compliance);

/// <summary>
///     Composes the Patient Summary read model (RM-2) from the ACL contracts of the five contexts, and nothing
///     else.
/// </summary>
/// <remarks>
///     "Desde la última consulta" starts on the day the last consultation was published. Before the first one,
///     the compliance covers the last 7 days and the weight trend the last 4 weeks. Days are UTC calendar days,
///     as everywhere else in Monitoring.
/// </remarks>
public class PatientSummaryComposer(
    IIamContextFacade iamContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IIntakeContextFacade intakeContextFacade,
    IMonitoringContextFacade monitoringContextFacade,
    TimeProvider timeProvider)
{
    /// <summary>Compliance window before the first consultation ("5 de 7 días · Últimos 7 días").</summary>
    public const int FirstConsultationComplianceDays = 7;

    /// <summary>Weight trend window before the first consultation ("Tendencia · 4 semanas").</summary>
    public const int FirstConsultationTrendWeeks = 4;

    /// <summary>Assembles the summary. Never throws: every facade degrades on its own.</summary>
    public async Task<PatientSummaryComposition> Compose(int patientId, CancellationToken ct = default)
    {
        var identity = await iamContextFacade.GetUserById(patientId, ct);
        var care = await careRelationshipContextFacade.GetActiveCareLinkByPatientId(patientId, ct);
        var activeTargets = await nutritionalCareContextFacade.GetActiveTargetsByPatientId(patientId, ct);
        var baseline = await nutritionalCareContextFacade.GetBaselineSummary(patientId, ct);
        var inProgress = await nutritionalCareContextFacade.GetConsultationInProgress(patientId, ct);
        var nextFollowUps = await monitoringContextFacade.GetNextFollowUpsByPatientIds([patientId], ct);

        var since = await SinceLastConsultation(patientId, ct);

        return new PatientSummaryComposition(
            patientId,
            identity,
            care,
            activeTargets?.PlanVersion,
            baseline,
            nextFollowUps.GetValueOrDefault(patientId),
            inProgress,
            since);
    }

    private async Task<SinceLastConsultationSection> SinceLastConsultation(int patientId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var lastConsultationAt = await nutritionalCareContextFacade.GetLastCompletedConsultationAt(patientId, ct);

        DateOnly from;
        int trendWeeks;
        if (lastConsultationAt is { } completedAt)
        {
            from = DateOnly.FromDateTime(completedAt.UtcDateTime);
            if (from > today) from = today;
            trendWeeks = Math.Max(1, (int)Math.Ceiling((today.DayNumber - from.DayNumber + 1) / 7m));
        }
        else
        {
            from = today.AddDays(-(FirstConsultationComplianceDays - 1));
            trendWeeks = FirstConsultationTrendWeeks;
        }

        var weightTrend = await intakeContextFacade.GetWeightTrendSummary(patientId, trendWeeks, ct);
        var compliance = await monitoringContextFacade.GetComplianceSummary(patientId, from, today, ct);

        return new SinceLastConsultationSection(from, lastConsultationAt, trendWeeks, weightTrend, compliance);
    }
}

using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

namespace Healthify.Platform.ReadModels.Application;

/// <summary>
///     What the practitioner reads about one patient between visits.
/// </summary>
/// <remarks>
///     This is the only path from the practitioner to the diary, and it is a read. There is no
///     command anywhere that lets a practitioner write, correct, validate or annotate a diary entry,
///     a self weigh-in or a weight trend, and the absence is the enforcement.
///     The diary arrives with confidence and provenance attached to every entry, always. A portion a
///     model guessed from a photo and a portion the patient typed are different kinds of evidence,
///     and a panel that showed them as the same number would be inviting the reader to mistake a
///     guess for a measurement. The panel is read qualitatively for that reason.
///     The two weight series are carried side by side and never merged. The clinical one is what the
///     practitioner measured; the smoothed one is what the patient's scale said, and it is only ever
///     shown as a trend.
///     What is not here: the nutritional diagnosis and the calculation basis. Neither leaves
///     Nutritional Care, and neither is on any contract this composer can reach.
///     RM-3: nor the consistency index. Patient First Always: the practitioner hears about it only as a
///     ConsistencyEscalation item in the review inbox, after the patient saw it, never on the panel.
/// </remarks>
/// <param name="PatientId">Whose panel.</param>
/// <param name="Date">The local calendar day the diary section covers.</param>
/// <param name="ActiveTargets">The published contract in force, or null when nothing is published.</param>
/// <param name="DailyIntakeSummary">What that day's confirmed entries added up to, or null.</param>
/// <param name="Diary">That day's entries, oldest first, with confidence and provenance.</param>
/// <param name="DailyCompliance">Day by day, oldest first. Unlogged days included as such.</param>
/// <param name="ClinicalAnthropometrySeries">Weight taken by the practitioner, oldest first.</param>
/// <param name="SelfWeighInTrend">The smoothed home series, kept separate from the clinical one.</param>
/// <param name="WeekFrom">RM-3. Monday of the week of <paramref name="Date" />.</param>
/// <param name="WeekTo">RM-3. Sunday of that week.</param>
/// <param name="Week">RM-3 (MA-6). That week day by day and counted, or null when it cannot be read.</param>
/// <param name="LoggedDaysFrom">RM-3. First of the last seven calendar days.</param>
/// <param name="LoggedDays">RM-3. The last seven calendar days counted ("Registro 6 de 7 días"), or null.</param>
/// <param name="WeightTrendSummary">RM-3 (IN-5). "−0,3 kg/sem · Autopesaje · 4 semanas", or null.</param>
/// <param name="LastClinicalMeasurement">RM-3 (NC-3). "Última medición clínica", or null.</param>
public record PatientMonitoringPanelComposition(
    int PatientId,
    DateOnly Date,
    ActiveTargetsItem? ActiveTargets,
    DailyIntakeSummaryItem? DailyIntakeSummary,
    IReadOnlyList<DiaryEntryItem> Diary,
    IReadOnlyList<DailyComplianceItem> DailyCompliance,
    IReadOnlyList<AnthropometryPointItem> ClinicalAnthropometrySeries,
    IReadOnlyList<WeightTrendPointItem> SelfWeighInTrend,
    DateOnly WeekFrom,
    DateOnly WeekTo,
    ComplianceRangeItem? Week,
    DateOnly LoggedDaysFrom,
    ComplianceSummaryItem? LoggedDays,
    WeightTrendSummaryItem? WeightTrendSummary,
    ClinicalMeasurementItem? LastClinicalMeasurement);

/// <summary>
///     Composes the Patient Monitoring Panel read model.
/// </summary>
/// <remarks>
///     Three contexts, three ACL contracts, no repositories. Notice what this composer cannot do:
///     there is no deviation on the Monitoring contract and no evidence string, because a sustained
///     deviation crosses the boundary once, as an event, and lands in a human inbox. A panel that
///     could query deviations on demand would be a second, quieter path from a signal to a clinical
///     decision, and the whole point of the review inbox is that there is only one and a person
///     stands in it.
///     RM-3: the consistency index is no longer read here (Patient First Always); the AI summary of IA-5 is not
///     either, so the panel never waits on a model.
///     Read models are not a bounded context. There is no aggregate here, no command, no event, no
///     table and no error enum.
/// </remarks>
public class PatientMonitoringPanelComposer(
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IIntakeContextFacade intakeContextFacade,
    IMonitoringContextFacade monitoringContextFacade,
    TimeProvider timeProvider)
{
    /// <summary>How many days of series the panel carries when the caller does not say.</summary>
    /// <remarks>
    ///     NOTE: technical constant, not part of the domain model. It is the shortest window a
    ///     deviation is ever read over, so a panel showing less than this would be showing something
    ///     no rule of this platform is willing to interpret.
    /// </remarks>
    public const int DefaultDays = 7;

    /// <summary>RM-3. "Tendencia · 4 semanas".</summary>
    public const int WeightTrendWeeks = 4;

    /// <summary>RM-3. "Registro 6 de 7 días".</summary>
    public const int LoggedDaysWindow = 7;

    /// <summary>
    ///     Assembles the panel. Never throws: every facade degrades on its own, and a section that
    ///     could not be read comes back empty.
    /// </summary>
    /// <param name="patientId">Whose panel.</param>
    /// <param name="date">The local calendar day the diary section covers. Defaults to today.</param>
    /// <param name="days">How much of the series to carry.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<PatientMonitoringPanelComposition> Compose(int patientId, DateOnly? date = null,
        int days = DefaultDays, CancellationToken ct = default)
    {
        var window = days <= 0 ? DefaultDays : days;

        // The patient's calendar day, not the server's. Which day an entry belongs to is decided by
        // the wall clock the patient was reading, and the diary is filed that way.
        var day = date ?? DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        var activeTargets = await nutritionalCareContextFacade.GetActiveTargetsByPatientId(patientId, ct);
        var summary = await intakeContextFacade.GetDailyIntakeSummary(patientId, day, ct);
        var diary = await intakeContextFacade.GetDiaryEntries(patientId, day, ct);

        var dailyCompliance = await monitoringContextFacade.GetDailyComplianceSeries(patientId, window, ct);
        var clinicalSeries = await monitoringContextFacade.GetAnthropometrySeriesPoints(patientId, ct);
        var selfWeighInTrend = await intakeContextFacade.GetWeightTrendPoints(patientId, window, ct);

        // RM-3 (MA-6): PAC-2 "Esta semana · L M M J V S D", Monday to Sunday of the panel day. Days still ahead
        // are Unlogged ("Sin registro"), never "Por debajo" (DECISIÓN §12-#7).
        var weekFrom = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var weekTo = weekFrom.AddDays(6);
        var week = await monitoringContextFacade.GetComplianceRange(patientId, weekFrom, weekTo, ct);

        // DECISIÓN RM-3: "Registro 6 de 7 días" counts the last seven calendar days up to the panel day, so it
        // does not shrink at the start of a week.
        var loggedFrom = day.AddDays(-(LoggedDaysWindow - 1));
        var loggedDays = await monitoringContextFacade.GetComplianceSummary(patientId, loggedFrom, day, ct);

        var weightTrend = await intakeContextFacade.GetWeightTrendSummary(patientId, WeightTrendWeeks, ct);
        var lastMeasurement = await nutritionalCareContextFacade.GetLatestClinicalMeasurement(patientId, ct);

        return new PatientMonitoringPanelComposition(
            patientId,
            day,
            activeTargets,
            summary,
            diary,
            dailyCompliance,
            clinicalSeries,
            selfWeighInTrend,
            weekFrom,
            weekTo,
            week,
            loggedFrom,
            loggedDays,
            weightTrend,
            lastMeasurement);
    }
}

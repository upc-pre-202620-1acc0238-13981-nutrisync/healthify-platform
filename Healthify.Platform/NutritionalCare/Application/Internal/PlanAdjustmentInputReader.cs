using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     IA-8. Reads the input of the plan adjustment proposal for a sustained deviation (MD IA-8 "Input"): the evidence
///     of the item, the slots logged on the deviated days (Monitoring and Intake, by facade), the weight trend (IN-5),
///     the version in force, the active diagnosis and the calorie floor for the patient's sex.
/// </summary>
/// <remarks>
///     Read only. It loads the plan to describe it and never writes it: it has no unit of work and the plan
///     repository is used for reading alone.
///     DECISIÓN IA-8: "comidas por franja de los días desviados" is read as the days of the last 14 (the evaluation
///     horizon of Monitoring is shorter) whose outcome went in the direction of the deviation, Short or Exceeded,
///     and, for each, which slots had a counted entry. Slots as in IA-2: Breakfast 05:00–10:59, Lunch 11:00–15:59,
///     Dinner 18:00–22:59, Other the rest.
/// </remarks>
public class PlanAdjustmentInputReader(
    INutritionPlanRepository planRepository,
    INutritionalDiagnosisRepository diagnosisRepository,
    IPatientBaselineRepository baselineRepository,
    INutritionalAssessmentRepository assessmentRepository,
    IMonitoringContextFacade monitoringContextFacade,
    IIntakeContextFacade intakeContextFacade,
    IIamContextFacade iamContextFacade,
    ICalorieFloorPolicy calorieFloorPolicy,
    IClinicalDateProvider clinicalDate)
{
    /// <summary>Days read back from today for the deviated days.</summary>
    public const int DeviatedDaysHorizon = 14;

    /// <summary>Weeks of the weight trend.</summary>
    public const int WeightTrendWeeks = 4;

    /// <summary>The input, or the error that leaves the item without a proposal.</summary>
    public async Task<(PlanAdjustmentInput? Input, NutritionalCareError Error)> ReadAsync(ReviewItem reviewItem,
        CancellationToken cancellationToken)
    {
        var patientId = reviewItem.PatientId;

        // The version in force: the proposal adjusts it.
        var plan = await planRepository.FindActiveByPatientIdAsync(patientId, cancellationToken);
        var targets = plan?.PrescribedTargets;
        if (plan is null || targets is null) return (null, NutritionalCareError.PlanNotFound);

        var diagnosis = await diagnosisRepository.FindActiveByPatientIdAsync(patientId, cancellationToken);
        var floor = calorieFloorPolicy.FloorFor(await BiologicalSexOfAsync(patientId, diagnosis, cancellationToken));

        var evidence = reviewItem.EvidenceData;
        var today = clinicalDate.Today();
        var from = today.AddDays(-(DeviatedDaysHorizon - 1));
        var slots = await MealSlotsOnDeviatedDaysAsync(patientId, evidence?.Direction, from, today,
            cancellationToken);

        var trend = await intakeContextFacade.GetWeightTrendSummary(patientId, WeightTrendWeeks, cancellationToken);
        // X-2: each part in the language of whoever reads it.
        var patientLanguage = await iamContextFacade.GetPreferredLanguage(patientId, cancellationToken);
        var practitionerLanguage =
            await iamContextFacade.GetPreferredLanguage(reviewItem.PractitionerId, cancellationToken);

        var input = new PlanAdjustmentInput(
            patientId,
            reviewItem.PractitionerId,
            LanguageOf(patientLanguage),
            new PlanAdjustmentEvidence(evidence?.AveragePercentFromTarget, evidence?.DeviatedDays,
                evidence?.LoggedDaysConsidered, evidence?.Direction),
            slots,
            trend is null ? null : new PlanAdjustmentWeightTrend(trend.SlopeKgPerWeek, trend.ChangeKg, trend.PointCount),
            new PlanAdjustmentCurrentPlan(plan.Version, targets.EnergyKcal, targets.ProteinG, targets.CarbG,
                targets.FatG,
                plan.Guidelines.Where(g => !g.IsCustom).Select(g => g.Code!).ToList(),
                plan.Restrictions.ToList()),
            diagnosis?.Code?.Value,
            floor,
            LanguageOf(practitionerLanguage));
        return (input, NutritionalCareError.UnexpectedError);
    }

    /// <summary><c>en</c> or <c>es</c>; anything else, or nothing, is the default <c>es</c>.</summary>
    private static string LanguageOf(string? preferred)
    {
        return string.Equals(preferred?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
    }

    /// <summary>The recorded sex: the baseline (NC-1), else the assessment behind the active diagnosis.</summary>
    public async Task<string?> BiologicalSexOfAsync(int patientId, NutritionalDiagnosis? diagnosis,
        CancellationToken cancellationToken)
    {
        var baseline = await baselineRepository.FindByPatientIdAsync(patientId, cancellationToken);
        if (baseline is not null) return baseline.BiologicalSex.Value;
        if (diagnosis is null) return null;
        var assessment = await assessmentRepository.FindByIdAsync(diagnosis.AssessmentId, cancellationToken);
        return assessment?.BiologicalSex.Value;
    }

    private async Task<PlanAdjustmentMealSlots> MealSlotsOnDeviatedDaysAsync(int patientId, string? direction,
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var range = await monitoringContextFacade.GetComplianceRange(patientId, from, to, cancellationToken);
        var outcomes = direction switch
        {
            ReviewItemEvidence.Below => new[] { "Short" },
            ReviewItemEvidence.Above => new[] { "Exceeded" },
            _ => new[] { "Short", "Exceeded" }
        };
        var deviatedDays = (range?.Days ?? [])
            .Where(d => outcomes.Contains(d.Outcome, StringComparer.OrdinalIgnoreCase))
            .Select(d => d.Date)
            .ToHashSet();
        if (deviatedDays.Count == 0) return new PlanAdjustmentMealSlots(0, 0, 0, 0, 0);

        var moments = (await intakeContextFacade.GetDiaryEntryMoments(patientId, from, to, cancellationToken))
            .Where(m => m.IsCountedTowardsTargets && deviatedDays.Contains(m.Date))
            .ToList();

        int DaysWith(string slot)
        {
            return moments.Where(m => SlotOf(m.LocalTimestamp) == slot).Select(m => m.Date).Distinct().Count();
        }

        return new PlanAdjustmentMealSlots(deviatedDays.Count, DaysWith("Breakfast"), DaysWith("Lunch"),
            DaysWith("Dinner"), DaysWith("Other"));
    }

    private static string SlotOf(DateTimeOffset localTimestamp)
    {
        return localTimestamp.Hour switch
        {
            >= 5 and < 11 => "Breakfast",
            >= 11 and < 16 => "Lunch",
            >= 18 and < 23 => "Dinner",
            _ => "Other"
        };
    }
}

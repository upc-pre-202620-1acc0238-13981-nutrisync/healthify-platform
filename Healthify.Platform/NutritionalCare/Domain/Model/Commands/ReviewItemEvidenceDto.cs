namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>The structured evidence of a signal, as the policy that opens the item received it.</summary>
/// <param name="AveragePercentFromTarget">Signed mean distance from the energy target (−40 = 40 % below).</param>
/// <param name="DeviatedDays">Logged days that deviated in the direction.</param>
/// <param name="LoggedDaysConsidered">Logged days read. Unlogged days are not counted.</param>
/// <param name="Direction">Above or Below.</param>
/// <param name="AdjustedOn">Scheduled recheck: the day the plan was adjusted.</param>
/// <param name="AdjustedPlanVersion">Scheduled recheck: the version assigned that day.</param>
/// <param name="AverageEnergyKcalFromTarget">Sustained deviation: signed kcal per day from the target.</param>
/// <param name="ConsistencyKgPerWeek">Consistency escalation: the index, kg per week.</param>
/// <param name="ConsistencyState">Consistency escalation: the state (Alert).</param>
/// <param name="AlertSinceOn">Consistency escalation: the day the alert began.</param>
/// <param name="WeeksInAlert">Consistency escalation: whole weeks in alert.</param>
/// <param name="ShownToPatientOn">Consistency escalation: the day the patient acknowledged the prompt.</param>
public record ReviewItemEvidenceDto(
    decimal? AveragePercentFromTarget,
    int? DeviatedDays,
    int? LoggedDaysConsidered,
    string? Direction,
    DateOnly? AdjustedOn = null,
    int? AdjustedPlanVersion = null,
    decimal? AverageEnergyKcalFromTarget = null,
    decimal? ConsistencyKgPerWeek = null,
    string? ConsistencyState = null,
    DateOnly? AlertSinceOn = null,
    int? WeeksInAlert = null,
    DateOnly? ShownToPatientOn = null);

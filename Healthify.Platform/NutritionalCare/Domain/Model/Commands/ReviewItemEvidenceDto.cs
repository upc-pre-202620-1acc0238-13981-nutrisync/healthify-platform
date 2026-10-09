namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>NC-11. The structured evidence of a signal, as the policy that opens the item received it.</summary>
/// <param name="AveragePercentFromTarget">Signed mean distance from the energy target (−40 = 40 % below).</param>
/// <param name="DeviatedDays">Logged days that deviated in the direction.</param>
/// <param name="LoggedDaysConsidered">Logged days read. Unlogged days are not counted.</param>
/// <param name="Direction">Above or Below.</param>
/// <param name="AdjustedOn">NC-10, scheduled recheck: the day the plan was adjusted.</param>
/// <param name="AdjustedPlanVersion">NC-10, scheduled recheck: the version assigned that day.</param>
/// <param name="AverageEnergyKcalFromTarget">X-2, sustained deviation: signed kcal per day from the target.</param>
/// <param name="ConsistencyKgPerWeek">X-2, consistency escalation: the index, kg per week.</param>
/// <param name="ConsistencyState">X-2, consistency escalation: the state (Alert).</param>
/// <param name="AlertSinceOn">X-2, consistency escalation: the day the alert began.</param>
/// <param name="WeeksInAlert">X-2, consistency escalation: whole weeks in alert.</param>
/// <param name="ShownToPatientOn">X-2, consistency escalation: the day the patient acknowledged the prompt.</param>
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

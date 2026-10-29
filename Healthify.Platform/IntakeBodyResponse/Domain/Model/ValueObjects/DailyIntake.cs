namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     IN-9. What the patient logged on one day of their own, next to the published targets (PT3 «Medidor del día»):
///     energy and macronutrients consumed and what is left of each.
/// </summary>
/// <remarks>
///     Descriptive only: it never rates the day (no outcome, no flags). Rating a day is MonitoringAdherence's job
///     (daily-compliance). Only confirmed entries count, as everywhere else (RM-3).
/// </remarks>
/// <param name="Date">The patient's day.</param>
/// <param name="EnergyKcal">Energy logged that day.</param>
/// <param name="ProteinG">Protein logged that day, in grams.</param>
/// <param name="CarbG">Carbohydrate logged that day, in grams.</param>
/// <param name="FatG">Fat logged that day, in grams.</param>
/// <param name="EntryCount">How many entries count towards the targets.</param>
/// <param name="PlanVersion">The version of the plan the targets come from.</param>
/// <param name="TargetEnergyKcal">The published daily energy target.</param>
/// <param name="TargetProteinG">The published daily protein target, in grams.</param>
/// <param name="TargetCarbG">The published daily carbohydrate target, in grams.</param>
/// <param name="TargetFatG">The published daily fat target, in grams.</param>
/// <param name="Remaining">The targets minus what was logged, never below zero.</param>
public sealed record DailyIntake(
    DateOnly Date,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    int EntryCount,
    int PlanVersion,
    decimal TargetEnergyKcal,
    decimal TargetProteinG,
    decimal TargetCarbG,
    decimal TargetFatG,
    RemainingTargets Remaining);

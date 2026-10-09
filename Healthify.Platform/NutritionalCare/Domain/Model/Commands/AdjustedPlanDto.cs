namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>NC-10 - PR14.IA-A "Asignar plan ajustado": the proposal as the practitioner edited it.</summary>
/// <param name="EnergyKcal">Daily energy.</param>
/// <param name="ProteinG">Daily protein.</param>
/// <param name="CarbG">Daily carbohydrate.</param>
/// <param name="FatG">Daily fat.</param>
/// <param name="Guidelines">
///     The guideline catalog codes of the new version, the whole list. Custom guidelines of the version in force are
///     kept.
/// </param>
/// <param name="PatientMessage">The message for the patient (NC-9), or null for none.</param>
public record AdjustedPlanDto(
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> Guidelines,
    string? PatientMessage);

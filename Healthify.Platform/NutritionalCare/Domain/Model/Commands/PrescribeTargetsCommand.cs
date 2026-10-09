namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Subflow 3.4 - Prescribe Targets. The practitioner accepts the proposal as it stands, or
///     replaces its numbers and says why. One command, one of two possible events.
/// </summary>
public record PrescribeTargetsCommand(
    int PlanId,
    int PractitionerId,
    string Outcome,
    decimal? EnergyKcal,
    decimal? ProteinG,
    decimal? CarbG,
    decimal? FatG,
    string? OverrideReason);

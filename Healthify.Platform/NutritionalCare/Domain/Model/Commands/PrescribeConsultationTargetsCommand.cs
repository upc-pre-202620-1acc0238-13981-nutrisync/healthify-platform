namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-5 - Step 3 of the consultation (EV-4): "Aceptar metas" or "Escribir mis propios valores" on the
///     draft plan version of the consultation.
/// </summary>
/// <param name="ConsultationId">The consultation in progress.</param>
/// <param name="PractitionerId">The practitioner leading it, from the token.</param>
/// <param name="Outcome">AcceptedAsProposed or Overridden.</param>
/// <param name="EnergyKcal">Overridden only; a missing value keeps the proposed one.</param>
/// <param name="ProteinG">Overridden only; a missing value keeps the proposed one.</param>
/// <param name="CarbG">Overridden only; a missing value keeps the proposed one.</param>
/// <param name="FatG">Overridden only; a missing value keeps the proposed one.</param>
/// <param name="OverrideReason">Required with Overridden (Override Requires Reason, Subflow 3.4).</param>
public record PrescribeConsultationTargetsCommand(
    int ConsultationId,
    int PractitionerId,
    string Outcome,
    decimal? EnergyKcal = null,
    decimal? ProteinG = null,
    decimal? CarbG = null,
    decimal? FatG = null,
    string? OverrideReason = null);

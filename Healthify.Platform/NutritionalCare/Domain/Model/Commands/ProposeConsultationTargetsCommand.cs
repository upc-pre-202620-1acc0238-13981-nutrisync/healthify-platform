namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-5 - Step 3 of the consultation (EV-4), ProposeTargetsForConsultation. Calculates the draft plan
///     version on the assessment of step 1 and the diagnosis of step 2, with the default parameters or the
///     ones the practitioner changed.
/// </summary>
/// <param name="ConsultationId">The consultation in progress.</param>
/// <param name="PractitionerId">The practitioner leading it, from the token.</param>
/// <param name="Parameters">Null to use every default ("Se calculan solas"); "Cambiar parámetros" otherwise.</param>
public record ProposeConsultationTargetsCommand(
    int ConsultationId,
    int PractitionerId,
    TargetParametersDto? Parameters = null);

namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     IA-8 - Purge the AI plan proposals of a patient that no practitioner accepted (Proposed or Dismissed). Issued by
///     the policy "When AI Processing Consent Withdrawn" (CR-2, §12-#14), which also covers the end of the link and
///     the discharge. Never by a user.
/// </summary>
/// <param name="PatientId">The patient whose AI processing ended.</param>
public record PurgeUnacceptedPlanProposalsCommand(int PatientId);

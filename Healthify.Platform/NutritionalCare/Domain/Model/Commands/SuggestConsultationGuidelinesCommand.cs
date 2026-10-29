namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Suggest the guidelines of the plan for the diagnosis of step 2.
///     It never changes the consultation: the chips are only pre-selected.
/// </summary>
/// <param name="ConsultationId">The consultation.</param>
/// <param name="PractitionerId">From the token: the practitioner leading it.</param>
public record SuggestConsultationGuidelinesCommand(int ConsultationId, int PractitionerId);

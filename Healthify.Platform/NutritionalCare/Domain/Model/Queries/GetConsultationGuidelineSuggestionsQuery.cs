namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>NC-6 - The guideline codes EV-5 pre-selects for the diagnosis of the consultation (IA-7 fallback).</summary>
public record GetConsultationGuidelineSuggestionsQuery(int ConsultationId);

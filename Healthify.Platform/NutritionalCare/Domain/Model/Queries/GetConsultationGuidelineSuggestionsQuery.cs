namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>The guideline codes step 4 pre-selects for the diagnosis of the consultation (the AI fallback).</summary>
public record GetConsultationGuidelineSuggestionsQuery(int ConsultationId);

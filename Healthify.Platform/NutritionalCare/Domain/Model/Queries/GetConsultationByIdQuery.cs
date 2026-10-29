namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>A consultation with what each saved step produced, to rehydrate steps 1 to 4.</summary>
public record GetConsultationByIdQuery(int ConsultationId);

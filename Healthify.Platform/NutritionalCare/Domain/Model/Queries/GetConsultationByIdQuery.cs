namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>NC-2 - A consultation with what each saved step produced, to rehydrate EV-2 to EV-5.</summary>
public record GetConsultationByIdQuery(int ConsultationId);

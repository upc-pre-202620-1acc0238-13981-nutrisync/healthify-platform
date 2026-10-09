namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>
///     NC-11. One item of the inbox with the patient's name: what <c>POST /resolution</c> and
///     <c>POST /plan-proposal/acceptance</c> answer, the same shape as <c>GET /review-items</c>.
/// </summary>
/// <param name="ReviewItemId">The item.</param>
public record GetReviewInboxEntryByIdQuery(int ReviewItemId);

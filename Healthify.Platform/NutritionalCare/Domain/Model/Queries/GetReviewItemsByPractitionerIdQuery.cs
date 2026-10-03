using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>
///     NC-11. Read model Practitioner Review Inbox, filtered by state (PR13.1 takes the resolved item out of the open
///     list), with the name of each patient.
/// </summary>
/// <param name="PractitionerId">Whose inbox.</param>
/// <param name="State">Open or Resolved.</param>
public record GetReviewItemsByPractitionerIdQuery(int PractitionerId, ReviewItemState State);

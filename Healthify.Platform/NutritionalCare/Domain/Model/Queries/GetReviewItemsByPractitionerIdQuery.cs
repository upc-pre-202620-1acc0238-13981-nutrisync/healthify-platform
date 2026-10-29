using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>
///     Read model Practitioner Review Inbox, filtered by state (a resolved item leaves the open
///     list), with the name of each patient.
/// </summary>
/// <param name="PractitionerId">Whose inbox.</param>
/// <param name="State">Open or Resolved.</param>
public record GetReviewItemsByPractitionerIdQuery(int PractitionerId, ReviewItemState State);

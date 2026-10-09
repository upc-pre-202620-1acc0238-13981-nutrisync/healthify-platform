using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>NC-11. One line of the review inbox: the item and the name of the patient (IAM-1).</summary>
/// <param name="ReviewItem">The item.</param>
/// <param name="PatientFullName">"Ana Flores", or null when Iam does not answer.</param>
public record ReviewInboxEntry(ReviewItem ReviewItem, string? PatientFullName);

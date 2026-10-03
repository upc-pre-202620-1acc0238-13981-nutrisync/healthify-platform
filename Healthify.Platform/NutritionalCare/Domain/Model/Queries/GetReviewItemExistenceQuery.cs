namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>
///     IA-5. Whether a review item of this signal type was ever opened for the patient, open or resolved. For the
///     rule Patient Shown First of Monitoring: the consistency index reaches a practitioner text only once it reached
///     the practitioner's inbox.
/// </summary>
/// <param name="PatientId">Whose items.</param>
/// <param name="SignalType">SustainedDeviation or ConsistencyEscalation.</param>
public record GetReviewItemExistenceQuery(int PatientId, string SignalType);

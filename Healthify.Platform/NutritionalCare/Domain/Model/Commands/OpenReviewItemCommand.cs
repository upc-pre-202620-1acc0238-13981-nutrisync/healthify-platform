namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Subflow 3.7 - Open Review Item. Issued by the policies that react to signals from Monitoring,
///     never by a user. It has no REST endpoint, and it can never modify a plan.
/// </summary>
/// <param name="PatientId">Whose signal.</param>
/// <param name="SignalType">SustainedDeviation, ConsistencyEscalation or (NC-10) ScheduledRecheck.</param>
/// <param name="Evidence">What was observed, in one sentence. Kept as it always was.</param>
/// <param name="EvidenceData">NC-11. The same evidence as numbers, when the signal carries them.</param>
public record OpenReviewItemCommand(
    int PatientId,
    string SignalType,
    string Evidence,
    ReviewItemEvidenceDto? EvidenceData = null);

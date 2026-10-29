namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     The patient turns AI processing on or off on their care link. Only the linked patient
///     may issue it. Turning it off changes neither the plan nor the records.
/// </summary>
public record ChangeAiProcessingConsentCommand(int CareLinkId, int PatientId, bool Granted);

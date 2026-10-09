namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     CR-2. The patient turns AI processing on or off on their care link (PT2, PT21.IA). Only the linked patient
///     may issue it. Turning it off changes neither the plan nor the records.
/// </summary>
public record ChangeAiProcessingConsentCommand(int CareLinkId, int PatientId, bool Granted);

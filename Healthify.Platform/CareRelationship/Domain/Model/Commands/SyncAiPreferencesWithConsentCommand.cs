namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     IA-1. Keeps the preferences coherent with the consent switch (PT21.IA): all on when AI processing is granted,
///     all off when it is withdrawn. Issued by the policy that reacts to AI Processing Consent Changed; no endpoint.
/// </summary>
public record SyncAiPreferencesWithConsentCommand(int PatientId, bool AiProcessingGranted);

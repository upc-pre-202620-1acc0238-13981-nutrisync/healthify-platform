namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>Payload of CR-2 - Change AI Processing Consent.</summary>
public record ChangeAiProcessingConsentResource(bool Granted)
{
    /// <summary>
    ///     True to let the AI functions process the patient's data, false to stop it. Turning it off changes neither
    ///     the plan nor the records.
    /// </summary>
    public bool Granted { get; init; } = Granted;
}

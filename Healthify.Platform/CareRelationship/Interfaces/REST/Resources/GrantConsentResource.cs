namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>Payload of Subflow 2.3 - Consent.</summary>
public record GrantConsentResource(string Scope, bool AiProcessingGranted = false)
{
    /// <summary>
    ///     What the patient is agreeing to share, in their own terms. Required: a consent whose
    ///     scope was never recorded is an assumption, not a consent.
    /// </summary>
    public string Scope { get; init; } = Scope;

    /// <summary>
    ///     CR-2. The separate consent to AI processing (PT2 "Usar funciones con IA"). Optional; false when absent, so
    ///     a client that does not know the switch never turns AI on.
    /// </summary>
    public bool AiProcessingGranted { get; init; } = AiProcessingGranted;
}

namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.3 - Consent. Only the linked patient may grant it. CR-2 appends <c>AiProcessingGranted</c>: the
///     separate consent to AI processing given in the same screen (PT2), off unless the patient turns it on.
/// </summary>
public record GrantConsentCommand(int CareLinkId, int PatientId, string Scope, bool AiProcessingGranted = false);

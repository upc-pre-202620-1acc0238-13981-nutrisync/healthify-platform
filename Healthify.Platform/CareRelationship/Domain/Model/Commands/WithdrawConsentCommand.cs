namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.5 - Withdraw Consent. Carries no justification field on purpose: the patient never
///     explains why they are leaving.
/// </summary>
public record WithdrawConsentCommand(int CareLinkId, int PatientId);

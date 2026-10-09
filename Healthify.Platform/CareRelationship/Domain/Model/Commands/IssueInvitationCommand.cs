namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>Subflow 2.1 - Invitation Issuing. Only a practitioner may issue one.</summary>
public record IssueInvitationCommand(int IssuedBy, DateTimeOffset ExpiresAt);

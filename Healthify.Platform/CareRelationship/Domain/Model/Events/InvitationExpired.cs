using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>Subflow 2.1. Stays inside Care Relationship.</summary>
public record InvitationExpired(int InvitationId, int IssuedBy, DateTimeOffset ExpiredAt) : DomainEventBase;

using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.Iam.Domain.Model.Events;

/// <summary>Subflow 1.2. Stays inside Iam. No other bounded context may declare a handler for it.</summary>
public record SessionStarted(int SessionId, int UserId, DateTimeOffset StartedAt) : DomainEventBase;

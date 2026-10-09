namespace Healthify.Platform.Shared.Domain.Model.Events;

/// <summary>
///     Base record for every domain event. Events are published with
///     <c>mediator.PublishAsync(...)</c> only after the unit of work has committed.
/// </summary>
public abstract record DomainEventBase : IEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

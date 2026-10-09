using Cortex.Mediator.Notifications;
using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.Shared.Application.Internal.EventHandlers;

/// <summary>
///     Typed alias over Cortex.Mediator's notification handler, constrained to domain events.
///     Every handler in the platform implements this interface and never
///     <see cref="INotificationHandler{TNotification}"/> directly.
/// </summary>
public interface IEventHandler<in TEvent> : INotificationHandler<TEvent>
    where TEvent : IEvent
{
}

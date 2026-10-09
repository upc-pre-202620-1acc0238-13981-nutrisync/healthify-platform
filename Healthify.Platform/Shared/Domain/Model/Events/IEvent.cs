using Cortex.Mediator.Notifications;

namespace Healthify.Platform.Shared.Domain.Model.Events;

/// <summary>Marker for anything that already happened inside the domain.</summary>
public interface IEvent : INotification
{
}

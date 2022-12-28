using EventBus.Events;

namespace EventBus.Bus;

/// <summary>
/// Contract for the event bus. The event bus uses a message broker to send and subscribe to events.
/// </summary>
public interface IEventBus
{
    void Publish<TEvent>(string exchangeName, TEvent @event)
        where TEvent : Event;

    void Subscribe<TEvent, TEventHandler>(string exchangeName,string queueName)
        where TEvent : Event
        where TEventHandler : IEventHandler<TEvent>;

    void Unsubscribe<TEvent, TEventHandler>()
        where TEvent : Event
        where TEventHandler : IEventHandler<TEvent>;
}

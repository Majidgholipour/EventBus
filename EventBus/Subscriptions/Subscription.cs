﻿namespace EventBus.Subscriptions;
/// <summary>
/// Represents an event subscription. Subscriptions control when we listen to events.
/// </summary>
public class Subscription
{
	public Type EventType { get; private set; }
	public Type HandlerType { get; private set; }

	public Subscription(Type eventType, Type handlerType)
	{
		EventType = eventType;
		HandlerType = handlerType;
	}
}

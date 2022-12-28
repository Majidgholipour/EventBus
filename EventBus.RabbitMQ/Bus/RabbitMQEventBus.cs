
namespace EventBus.RabbitMQ.Bus;
public class RabbitMQEventBus : IEventBus
{
	private readonly int _publishRetryCount = 5;
	private readonly TimeSpan _subscribeRetryTime = TimeSpan.FromSeconds(5);

	private readonly IPersistentConnection _persistentConnection;
	private readonly IEventBusSubscriptionManager _subscriptionsManager;
	private readonly IServiceProvider _serviceProvider;

	private readonly ILogger<RabbitMQEventBus> _logger;

	private IModel _consumerChannel;

	public RabbitMQEventBus(
		IPersistentConnection persistentConnection,
		IEventBusSubscriptionManager subscriptionsManager,
		IServiceProvider serviceProvider,
		ILogger<RabbitMQEventBus> logger)
	{
		_persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
		_subscriptionsManager = subscriptionsManager ?? throw new ArgumentNullException(nameof(subscriptionsManager));
		_serviceProvider = serviceProvider;
		_logger = logger;
	}

	public void Publish<TEvent>(string exchangeName, TEvent @event)
		where TEvent : Event
	{
		if (!_persistentConnection.IsConnected)
		{
			_persistentConnection.TryConnect();
		}

		var policy = Policy
			.Handle<BrokerUnreachableException>()
			.Or<SocketException>()
			.WaitAndRetry(_publishRetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (exception, timeSpan) =>
			{
				_logger.LogWarning(exception, "Could not publish event #{EventId} after {Timeout} seconds: {ExceptionMessage}.", @event.Id, $"{timeSpan.TotalSeconds:n1}", exception.Message);
			});

		var eventName = @event.GetType().Name;

		_logger.LogTrace("Creating RabbitMQ channel to publish event #{EventId} ({EventName})...", @event.Id, eventName);

		using (var channel = _persistentConnection.CreateModel())
		{
			_logger.LogTrace("Declaring RabbitMQ exchange to publish event #{EventId}...", @event.Id);

			channel.ExchangeDeclare(exchange: exchangeName, type: "direct");

			var message = JsonSerializer.Serialize(@event);
			var body = Encoding.UTF8.GetBytes(message);

			policy.Execute(() =>
			{
				var properties = channel.CreateBasicProperties();
				properties.DeliveryMode = (byte)DeliveryMode.Persistent;

				_logger.LogTrace("Publishing event to RabbitMQ with ID #{EventId}...", @event.Id);

				channel.BasicPublish(
					exchange: exchangeName,
					routingKey: eventName,
					mandatory: true,
					basicProperties: properties,
					body: body);

				_logger.LogTrace("Published event with ID #{EventId}.", @event.Id);
			});
		}
	}
	public void Subscribe<TEvent, TEventHandler>(string exchangeName,string queueName)
		where TEvent : Event
		where TEventHandler : IEventHandler<TEvent>
	{

		ConfigureMessageBroker(exchangeName, queueName);

		var eventName = _subscriptionsManager.GetEventIdentifier<TEvent>();
		var eventHandlerName = typeof(TEventHandler).Name;

		AddQueueBindForEventSubscription(eventName, exchangeName, queueName);

		_logger.LogInformation("Subscribing to event {EventName} with {EventHandler}...", eventName, eventHandlerName);

		_subscriptionsManager.AddSubscription<TEvent, TEventHandler>();
		StartBasicConsume(queueName);

		_logger.LogInformation("Subscribed to event {EventName} with {EvenHandler}.", eventName, eventHandlerName);
	}

	public void Unsubscribe<TEvent, TEventHandler>()
		where TEvent : Event
		where TEventHandler : IEventHandler<TEvent>
	{
		var eventName = _subscriptionsManager.GetEventIdentifier<TEvent>();

		_logger.LogInformation("Unsubscribing from event {EventName}...", eventName);

		_subscriptionsManager.RemoveSubscription<TEvent, TEventHandler>();

		_logger.LogInformation("Unsubscribed from event {EventName}.", eventName);
	}

	private void ConfigureMessageBroker(string exchangeName, string queueName)
	{
		_consumerChannel = CreateConsumerChannel(exchangeName, queueName);
		_subscriptionsManager.OnEventRemoved += (sender, e) => SubscriptionManager_OnEventRemoved(sender,e,exchangeName,queueName);
		_persistentConnection.OnReconnectedAfterConnectionFailure += (sender, e) => PersistentConnection_OnReconnectedAfterConnectionFailure(sender, e, exchangeName, queueName);
	}

	private IModel CreateConsumerChannel(string exchangeName,string queueName)
	{
		if (!_persistentConnection.IsConnected)
		{
			_persistentConnection.TryConnect();
		}

		_logger.LogTrace("Creating RabbitMQ consumer channel...");

		var channel = _persistentConnection.CreateModel();

		channel.ExchangeDeclare(exchange: exchangeName, type: "direct");
		channel.QueueDeclare
		(
			queue: queueName,
			durable: true,
			exclusive: false,
			autoDelete: false,
			arguments: null
		);

		channel.CallbackException += (sender, ea) =>
		{
			_logger.LogWarning(ea.Exception, "Recreating RabbitMQ consumer channel...");
			DoCreateConsumerChannel(exchangeName,queueName);
		};

		_logger.LogTrace("Created RabbitMQ consumer channel.");


		return channel;
	}

	private void StartBasicConsume(string queueName)
	{
		_logger.LogTrace("Starting RabbitMQ basic consume...");

		if (_consumerChannel == null)
		{
			_logger.LogError("Could not start basic consume because consumer channel is null.");
			return;
		}

		var consumer = new AsyncEventingBasicConsumer(_consumerChannel);
		consumer.Received += Consumer_Received;

		_consumerChannel.BasicConsume
		(
			queue: queueName,
			autoAck: false,
			consumer: consumer
		);

		_logger.LogTrace("Started RabbitMQ basic consume.");
	}

	private async Task Consumer_Received(object sender, BasicDeliverEventArgs eventArgs)
	{
		var eventName = eventArgs.RoutingKey;
		var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

		bool isAcknowledged = false;

		try
		{
			await ProcessEvent(eventName, message);

			_consumerChannel.BasicAck(eventArgs.DeliveryTag, multiple: false);
			isAcknowledged = true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error processing the following message: {Message}.", message);
		}
		finally
		{
			if (!isAcknowledged)
			{
				await TryEnqueueMessageAgainAsync(eventArgs);
			}
		}
	}

	private async Task TryEnqueueMessageAgainAsync(BasicDeliverEventArgs eventArgs)
	{
		try
		{
			_logger.LogWarning("Adding message to queue again with {Time} seconds delay...", $"{_subscribeRetryTime.TotalSeconds:n1}");

			await Task.Delay(_subscribeRetryTime);
			_consumerChannel.BasicNack(eventArgs.DeliveryTag, false, true);

			_logger.LogTrace("Message added to queue again.");
		}
		catch (Exception ex)
		{
			_logger.LogError("Could not enqueue message again: {Error}.", ex.Message);
		}
	}

	private async Task ProcessEvent(string eventName, string message)
	{
		_logger.LogTrace("Processing RabbitMQ event: {EventName}...", eventName);

		if (!_subscriptionsManager.HasSubscriptionsForEvent(eventName))
		{
			_logger.LogTrace("There are no subscriptions for this event.");
			return;
		}

		var subscriptions = _subscriptionsManager.GetHandlersForEvent(eventName);
		foreach (var subscription in subscriptions)
		{
			var handler = _serviceProvider.GetService(subscription.HandlerType);
			if (handler == null)
			{
				_logger.LogWarning("There are no handlers for the following event: {EventName}", eventName);
				continue;
			}

			var eventType = _subscriptionsManager.GetEventTypeByName(eventName);

			var @event = JsonSerializer.Deserialize(message, eventType);
			var eventHandlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
			await Task.Yield();
			await (Task)eventHandlerType.GetMethod(nameof(IEventHandler<Event>.HandleAsync)).Invoke(handler, new object[] { @event });
		}

		_logger.LogTrace("Processed event {EventName}.", eventName);
	}

	private void SubscriptionManager_OnEventRemoved(object sender, string eventName,string exchangeName,string queueName)
	{
		if (!_persistentConnection.IsConnected)
		{
			_persistentConnection.TryConnect();
		}

		using (var channel = _persistentConnection.CreateModel())
		{
			channel.QueueUnbind(queue: queueName, exchange: exchangeName, routingKey: eventName);

			if (_subscriptionsManager.IsEmpty)
			{
				_consumerChannel.Close();
			}
		}
	}

	private void AddQueueBindForEventSubscription(string eventName, string exchangeName,string queueName)
	{
		var containsKey = _subscriptionsManager.HasSubscriptionsForEvent(eventName);
		if (containsKey)
		{
			return;
		}

		if (!_persistentConnection.IsConnected)
		{
			_persistentConnection.TryConnect();
		}

		using (var channel = _persistentConnection.CreateModel())
		{
			channel.QueueBind(queue: queueName, exchange: exchangeName, routingKey: eventName);
		}
	}

	private void PersistentConnection_OnReconnectedAfterConnectionFailure(object sender, EventArgs e, string exchangeName, string queueName)
	{
		DoCreateConsumerChannel(exchangeName,queueName);
		RecreateSubscriptions();
	}

	private void DoCreateConsumerChannel(string exchangeName,string queueName)
	{
		_consumerChannel.Dispose();
		_consumerChannel = CreateConsumerChannel(exchangeName,queueName);
		StartBasicConsume(queueName);
	}

	private void RecreateSubscriptions()
	{
		var subscriptions = _subscriptionsManager.GetAllSubscriptions();
		_subscriptionsManager.Clear();

		Type eventBusType = this.GetType();
		MethodInfo genericSubscribe;

		foreach (var entry in subscriptions)
		{
			foreach (var subscription in entry.Value)
			{
				genericSubscribe = eventBusType.GetMethod("Subscribe").MakeGenericMethod(subscription.EventType, subscription.HandlerType);
				genericSubscribe.Invoke(this, null);
			}
		}
	}
}
# EventBus
Simple lightweight .NET Event Bus Library using RabbitMQ

Use and Configuration
=====================
## Publisher

1. Create an Event in EventBus.InegrationEvent project:
```csharp
public class MessageEvent : Event
{
    public string Message{ get; set; }
}
```

2. Add RabbitMQ configuration in appsetting.json:
```csharp
  "RabbitMQ": {
    "ConnectionUrl": "amqp://guest:guest@127.0.0.1:5672/"
  },
  ```
  3. Add RabbitMQ.Client Package from nuget 
  
  .Net CLI
  ```console
    dotnet add package RabbitMQ.Client --version 6.4.0
  ```
  Package Manager Console
  ```console
    dotnet add package RabbitMQ.Client --version 6.4.0
  ```
  4. Add reference EventBus, EventBus.RabbitMQ and EventBus.InegrationEvent in your project
  5. In Program (or StartUp) class add below code to register AddRabbitMQEventBus service
  ```csharp
    // In ConfigureServices section
    var rabbitMQSection = builder.Configuration.GetSection("RabbitMQ");
    builder.Services.AddRabbitMQEventBus
    (
        connectionUrl: rabbitMQSection["ConnectionUrl"],
        timeoutBeforeReconnecting: 15
    );
  ```
  6. Then in controller or any place that you want to send message use this code snip
  
  ```csharp
    private readonly IEventBus _eventBus;
    
    public HomeController(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(string message)
    {
      _eventBus.Publish("your exhange name",
      new MessageEvent()
      {
        Message = message ?? string.Empty
      });
      return Ok();
    }
  ```
  So far we configure publisher (client) and a message sent to specific Exchange. Remember that the create Queue and binding Queue to Exchange done in subscriber (server) part.
  
  ## Subscriber
  
  1. Add RabbitMQ configuration in appsetting.json like below
  ```csharp
  "RabbitMQ": {
    "ConnectionUrl": "amqp://guest:guest@127.0.0.1:5672/"
  },
  ```
  2. Add RabbitMQ.Client Package from nuget 
  
  .Net CLI
  ```console
    dotnet add package RabbitMQ.Client --version 6.4.0
  ```
Package Manager Console
```console
    dotnet add package RabbitMQ.Client --version 6.4.0
  ```
  3. Add referece EventBus, EventBus.RabbitMQ and EventBus.InegrationEvent in your project
  4. Create a handler class to get sent message from publisher
  ```csharp
  public class MessageEventHandler : IEventHandler<MessageEvent>
  {
    private readonly ILogger<MessageEventHandler> _logger;

    public MessageEventHandler(ILogger<MessageEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(MessageEvent @event)
    {
        // Here you handle what happens when you receive an event of this type from the event bus.

        _logger.LogInformation(@event.ToString());
        return Task.CompletedTask;
    }
  }
  ```
  
  5. In Program (or StartUp) class add below code to register AddRabbitMQEventBus service and setup subscriber
  ```csharp
    // In ConfigureServices section
    var rabbitMQSection = builder.Configuration.GetSection("RabbitMQ");
    builder.Services.AddRabbitMQEventBus
    (
        connectionUrl: rabbitMQSection["ConnectionUrl"],
        timeoutBeforeReconnecting: 15
    );
    builder.Services.AddTransient<MessageEventHandler>();
    
    // In Configure section
    var eventBus = app.Services.GetRequiredService<IEventBus>();

    eventBus.Subscribe<MessageEvent, MessageEventHandler>(
        your exhange name,
        your Queue name);
  ```
  
  According to above snip code we configure MessageEventHandler subscriber to subcribing message from MessageEvent that send before from publisher. Also remember, in my example in this repository the Publisher and Subscriber are in the same project

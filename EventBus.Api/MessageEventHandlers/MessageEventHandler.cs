
namespace FasterGate.Message.Application.V1.IntegrationEvents;

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

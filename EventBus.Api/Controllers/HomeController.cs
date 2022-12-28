
namespace EventBus.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class HomeController : ControllerBase
{

    private readonly IEventBus _eventBus;

    public HomeController(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(string message)
    {
        try
        {
            _eventBus.Publish(EnmExchanges.Message_Bus.GetEnumTitle(),
            new MessageEvent()
            {
                Message = message ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            return BadRequest();
        }
        return Ok();
    }


}

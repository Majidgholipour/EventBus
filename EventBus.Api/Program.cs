
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

 var rabbitMQSection = builder.Configuration.GetSection("RabbitMQ");
builder.Services.AddRabbitMQEventBus
(
    connectionUrl: rabbitMQSection["ConnectionUrl"],
    timeoutBeforeReconnecting: 15
);

builder.Services.AddTransient<MessageEventHandler>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

var eventBus = app.Services.GetRequiredService<IEventBus>();

//Add EventBus Subscriber
eventBus.Subscribe<MessageEvent, MessageEventHandler>(
        EnmExchanges.Message_Bus.GetEnumTitle(),
        EnmQueues.Message_Queue.GetEnumTitle());

app.Run();

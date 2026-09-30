using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace OrderService.Services;

public class MessageBus
{
    private readonly ConnectionFactory _factory;

    public MessageBus()
    {
        _factory = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@localhost:5672") };
    }

    public async Task PublishAsync(OrderCreatedEvent orderEvent)
    {
        await using IConnection conn = await _factory.CreateConnectionAsync();
        await using IChannel channel = await conn.CreateChannelAsync();

        string queue = "OrderQueue";
        string exchange = "orders";
        string routingKey = "order-exchange-routing";

        await channel.ExchangeDeclareAsync(
            exchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null
        );
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );
        await channel.QueueBindAsync(queue, exchange, routingKey, arguments: null);

        var json = JsonSerializer.Serialize(orderEvent);
        var body = Encoding.UTF8.GetBytes(json);

        await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, body: body);

        Console.WriteLine($" [x] Sent {json}");
    }
}

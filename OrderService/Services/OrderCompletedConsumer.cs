using System.Text;
using System.Text.Json;
using OrderService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Services;

public class OrderCompletedConsumer : BackgroundService
{
    private readonly ConnectionFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection? _connection;
    private IChannel? _channel;

    public OrderCompletedConsumer(IServiceScopeFactory scopeFactory)
    {
        _factory = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@localhost:5672") };
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _connection = await _factory.CreateConnectionAsync(stopToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stopToken);

        string queue = "OrderCompletedQueue";
        string exchange = "order-completed";
        string routingKey = "order-completed-routing";

        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true, autoDelete: false, arguments: null);
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: null);
        await _channel.QueueBindAsync(queue, exchange, routingKey, arguments: null);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;

        await _channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer);

        await Task.Delay(Timeout.Infinite, stopToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken: cancellationToken);
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken: cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task HandleMessageAsync(object model, BasicDeliverEventArgs ea)
    {
        // one DbContext per message
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDb>();

        try
        {
            // decode + deserialize the completion event
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            OrderCompletedEvent? completedEvent = JsonSerializer.Deserialize<OrderCompletedEvent>(json);

            if (completedEvent is null)
            {
                // garbage message — discard it
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            // find the order
            var order = await db.Orders.FindAsync(completedEvent.OrderId);

            if (order is null)
            {
                Console.WriteLine($"Order {completedEvent.OrderId} not found — discarding completion event.");
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            // mark the order complete
            order.Status = OrderStatus.Complete;
            await db.SaveChangesAsync();

            Console.WriteLine($"Order {completedEvent.OrderId} completed.");

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to process completion event: {ex.Message}");

            // transient failure — requeue and try again later
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }
}

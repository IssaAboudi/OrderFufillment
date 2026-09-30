using System.Text;
using System.Text.Json;
using OrderService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Services;

public class OrderStatusConsumer : BackgroundService
{
    private readonly ConnectionFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection? _connection;
    private IChannel? _channel;

    public OrderStatusConsumer(IServiceScopeFactory scopeFactory)
    {
        _factory = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@localhost:5672") };
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _connection = await _factory.CreateConnectionAsync(stopToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stopToken);

        string queue = "OrderStatusQueue";
        string exchange = "order-status";
        string routingKey = "order-status-routing";

        await _channel.ExchangeDeclareAsync(
            exchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null
        );
        await _channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );
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
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            OrderFailedEvent? failedEvent = JsonSerializer.Deserialize<OrderFailedEvent>(json);

            if (failedEvent is null)
            {
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            var order = await db.Orders.FindAsync(failedEvent.OrderId);

            if (order is null)
            {
                Console.WriteLine(
                    $"Order {failedEvent.OrderId} not found — discarding failure event."
                );
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            order.Status = OrderStatus.Failed;
            order.FailureReason = failedEvent.Reason;
            await db.SaveChangesAsync();

            Console.WriteLine($"Order {failedEvent.OrderId} failed: {failedEvent.Reason}");

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to process status event: {ex.Message}");

            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }
}

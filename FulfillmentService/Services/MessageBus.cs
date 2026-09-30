using System.Text;
using System.Text.Json;
using FulfillmentService.Models;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FulfillmentService.Services;

public class MessageBus : BackgroundService
{
    private readonly ConnectionFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection? _connection;
    private IChannel? _channel;

    public MessageBus(IServiceScopeFactory scopeFactory)
    {
        _factory = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@localhost:5672") };
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _connection = await _factory.CreateConnectionAsync(stopToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stopToken);

        string queue = "ProcessedOrderQueue";
        string exchange = "order-processed";
        string routingKey = "order-processed-routing";

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
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDb>();

        string json = Encoding.UTF8.GetString(ea.Body.Span);
        OrderProcessedEvent? orderEvent = JsonSerializer.Deserialize<OrderProcessedEvent>(json);

        if (orderEvent is null)
        {
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            bool alreadyExists = await db.Receipts.AnyAsync(r => r.OrderId == orderEvent.OrderId);
            if (alreadyExists)
            {
                Console.WriteLine(
                    $"Receipt for order {orderEvent.OrderId} already exists — skipping."
                );
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            decimal total = orderEvent.Items.Sum(i => i.Quantity * i.UnitPrice);

            db.Receipts.Add(
                new Receipt
                {
                    OrderId = orderEvent.OrderId,
                    CustomerName = orderEvent.CustomerName,
                    TotalAmount = total,
                }
            );

            foreach (var item in orderEvent.Items)
            {
                db.ReceiptItems.Add(
                    new ReceiptItem
                    {
                        OrderId = orderEvent.OrderId,
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                    }
                );
            }

            await db.SaveChangesAsync();

            Console.WriteLine($"Receipt for order {orderEvent.OrderId} created — total {total}.");

            await PublishOrderCompletedAsync(orderEvent.OrderId);

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create receipt: {ex.Message}");

            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task PublishOrderCompletedAsync(int orderId)
    {
        await using IConnection conn = await _factory.CreateConnectionAsync();
        await using IChannel channel = await conn.CreateChannelAsync();

        string queue = "OrderCompletedQueue";
        string exchange = "order-completed";
        string routingKey = "order-completed-routing";

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

        var completedEvent = new OrderCompletedEvent(orderId);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(completedEvent));

        await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, body: body);

        Console.WriteLine($" [x] Published completed event for order {orderId}.");
    }
}

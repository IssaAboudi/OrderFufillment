using System.Text;
using System.Text.Json;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryService.Services;

public class MessageBus : BackgroundService
{
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IServiceScopeFactory _scopeFactory;

    public MessageBus(IServiceScopeFactory scopeFactory)
    {
        _factory = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@localhost:5672") };
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _connection = await _factory.CreateConnectionAsync(stopToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stopToken);

        string queue = "OrderQueue";
        string exchange = "orders";
        string routingKey = "order-exchange-routing";

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
        using IServiceScope scope = _scopeFactory.CreateScope();
        InventoryDb db = scope.ServiceProvider.GetRequiredService<InventoryDb>();

        string json = Encoding.UTF8.GetString(ea.Body.Span);
        OrderCreatedEvent? orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

        if (orderEvent is null)
        {
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            bool alreadyProcessed = await db.ProcessedOrders.AnyAsync(o =>
                o.OrderId == orderEvent.OrderId
            );
            if (alreadyProcessed)
            {
                Console.WriteLine($"Order {orderEvent.OrderId} already processed — skipping.");
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            foreach (var item in orderEvent.Items)
            {
                InventoryItem? inventory = await db.InventoryItems.FindAsync(item.ProductId);

                if (inventory is null)
                {
                    throw new Exception($"Product {item.ProductId} not found.");
                }

                if (inventory.QuantityOnHand < item.Quantity)
                {
                    throw new Exception($"Not enough stock for product {item.ProductId}.");
                }

                inventory.QuantityOnHand -= item.Quantity;
            }

            db.ProcessedOrders.Add(new ProcessedOrder { OrderId = orderEvent.OrderId });

            foreach (var item in orderEvent.Items)
            {
                db.ProcessedOrderItems.Add(new ProcessedOrderItem
                {
                    OrderId = orderEvent.OrderId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                });
            }

            await db.SaveChangesAsync();

            Console.WriteLine($"Order {orderEvent.OrderId} processed — stock updated.");

            // tell FulfillmentService the order is processed
            await PublishOrderProcessedAsync(orderEvent.OrderId, orderEvent.CustomerName, orderEvent.Items);

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Order {orderEvent.OrderId} failed: {ex.Message}");

            await PublishOrderFailedAsync(orderEvent.OrderId, ex.Message);

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
    }

    private async Task PublishOrderFailedAsync(int orderId, string reason)
    {
        await using IConnection conn = await _factory.CreateConnectionAsync();
        await using IChannel channel = await conn.CreateChannelAsync();

        string statusQueue = "OrderStatusQueue";
        string statusExchange = "order-status";
        string statusRoutingKey = "order-status-routing";

        await channel.ExchangeDeclareAsync(
            statusExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null
        );
        await channel.QueueDeclareAsync(
            statusQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );
        await channel.QueueBindAsync(
            statusQueue,
            statusExchange,
            statusRoutingKey,
            arguments: null
        );

        var failedEvent = new OrderFailedEvent(orderId, reason);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(failedEvent));

        await channel.BasicPublishAsync(
            statusExchange,
            statusRoutingKey,
            mandatory: false,
            body: body
        );

        Console.WriteLine($" [x] Published failure event for order {orderId}.");
    }

    private async Task PublishOrderProcessedAsync(
        int orderId,
        string customerName,
        List<OrderItemContract> items
    )
    {
        await using IConnection conn = await _factory.CreateConnectionAsync();
        await using IChannel channel = await conn.CreateChannelAsync();

        string queue = "ProcessedOrderQueue";
        string exchange = "order-processed";
        string routingKey = "order-processed-routing";

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

        var processedEvent = new OrderProcessedEvent(orderId, customerName, items);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(processedEvent));

        await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, body: body);

        Console.WriteLine($" [x] Published processed event for order {orderId}.");
    }
}

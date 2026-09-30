using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderService.DTO;
using OrderService.Models;
using OrderService.Services;

var builder = WebApplication.CreateBuilder(args);

// connects to postgresql
builder.Services.AddDbContext<OrderDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// converts enums to strings
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);

// add MessageBus (producer) + status consumers
builder.Services.AddSingleton<MessageBus>();
builder.Services.AddHostedService<OrderStatusConsumer>();
builder.Services.AddHostedService<OrderCompletedConsumer>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// apply any pending EF migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDb>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// make a new order
app.MapPost(
    "/orders",
    async (OrderDb db, MessageBus bus, CreateOrderRequest request) =>
    {
        var order = new Order
        {
            CustomerName = request.CustomerName,
            Status = OrderStatus.Pending,
            OrderDate = DateTime.UtcNow,

            Items = new List<OrderItem>(),
        };

        var productNames = new Dictionary<int, string>();

        foreach (var item in request.Items)
        {
            var product = await db.Products.FindAsync(item.ProductId);

            if (product is null)
            {
                return Results.NotFound($"Product {item.ProductId} was not found.");
            }

            if (!product.IsAvailable)
            {
                return Results.BadRequest($"Product {item.ProductId} is not available.");
            }

            productNames[item.ProductId] = product.Name;

            order.Items.Add(
                new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price,
                }
            );
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        OrderCreatedEvent orderEvent = new OrderCreatedEvent(
            order.Id,
            order.CustomerName,
            order
                .Items.Select(i => new OrderItemContract(i.ProductId, i.Quantity, i.UnitPrice))
                .ToList()
        );

        await bus.PublishAsync(orderEvent);

        var response = new
        {
            order.Id,
            order.CustomerName,
            Items = order.Items.Select(i => new
            {
                i.Id,
                i.OrderId,
                Product = productNames[i.ProductId],
                i.Quantity,
                i.UnitPrice,
            }),
            order.OrderDate,
            order.Status,
        };

        return Results.Created($"/orders/{order.Id}", response);
    }
);

// get a list of orders
app.MapGet(
    "/orders",
    async (OrderDb db) =>
        await db
            .Orders.OrderBy(o => o.Id)
            .Select(o => new
            {
                o.Id,
                o.CustomerName,
                o.OrderDate,
                o.Status,
                o.FailureReason,
            })
            .ToListAsync()
);

// get a particular order and it's information
app.MapGet(
    "/orders/{id}",
    async (int id, OrderDb db) =>
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            return Results.NotFound();
        }

        // fetch product names for the line items
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var productNames = await db
            .Products.Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var response = new
        {
            order.Id,
            order.CustomerName,
            Items = order.Items.Select(i => new
            {
                i.Id,
                i.OrderId,
                Product = productNames[i.ProductId],
                i.Quantity,
                i.UnitPrice,
            }),
            order.OrderDate,
            order.Status,
            order.FailureReason,
        };

        return Results.Ok(response);
    }
);

// update an order
app.MapPatch(
    "/orders/{id}",
    async (int id, OrderDb db, UpdateOrderRequest request) =>
    {
        var order = await db.Orders.FindAsync(id);

        if (order is null)
        {
            return Results.NotFound();
        }

        // update existing order
        if (request.CustomerName is not null)
        {
            order.CustomerName = request.CustomerName;
        }

        if (request.Items is not null)
        {
            var items = new List<OrderItem>();

            foreach (var item in request.Items)
            {
                var product = await db.Products.FindAsync(item.ProductId);

                if (product is null)
                {
                    return Results.NotFound($"Product {item.ProductId} was not found.");
                }

                if (!product.IsAvailable)
                {
                    return Results.BadRequest($"Product {item.ProductId} is not available.");
                }

                items.Add(
                    new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = product.Price,
                    }
                );
            }

            order.Items = items;
        }

        await db.SaveChangesAsync();
        return Results.Ok(order);
    }
);

// delete an order
app.MapDelete(
    "/orders/{id}",
    async (int id, OrderDb db) =>
    {
        var order = await db.Orders.FindAsync(id);

        if (order is null)
        {
            return Results.NotFound();
        }

        db.Orders.Remove(order);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }
);

// list the product catalog (menu)
app.MapGet(
    "/products",
    async (OrderDb db) =>
        await db.Products.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync()
);

// get a single menu item
app.MapGet(
    "/products/{id}",
    async (int id, OrderDb db) =>
    {
        var product = await db.Products.FindAsync(id);

        if (product is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(product);
    }
);

app.Run();

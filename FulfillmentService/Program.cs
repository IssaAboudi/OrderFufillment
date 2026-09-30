using FulfillmentService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// connects to postgresql
builder.Services.AddDbContext<FulfillmentDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// add MessageBus (consumer + publisher)
builder.Services.AddHostedService<MessageBus>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// apply any pending EF migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FulfillmentDb>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok("healthy"));

// get a receipt for a specific order (with line items + total)
app.MapGet(
    "/receipts/{orderId}",
    async (int orderId, FulfillmentDb db) =>
    {
        var receipt = await db
            .Receipts.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.OrderId == orderId);

        if (receipt is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(
            new
            {
                receipt.OrderId,
                receipt.CustomerName,
                receipt.TotalAmount,
                Items = receipt.Items.Select(i => new
                {
                    i.ProductId,
                    i.Quantity,
                    i.UnitPrice,
                }),
            }
        );
    }
);

// list all receipts
app.MapGet(
    "/receipts",
    async (FulfillmentDb db) =>
        await db
            .Receipts.OrderBy(r => r.OrderId)
            .Select(r => new
            {
                r.OrderId,
                r.CustomerName,
                r.TotalAmount,
            })
            .ToListAsync()
);

app.Run();

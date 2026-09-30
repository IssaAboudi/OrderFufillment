using System.Text.Json.Serialization;
using InventoryService.Models;
using InventoryService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// connects to postgresql
builder.Services.AddDbContext<InventoryDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// converts enums to strings
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);

// add MessageBus
builder.Services.AddHostedService<MessageBus>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// apply any pending EF migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDb>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok("healthy"));

app.MapGet(
    "/inventory",
    async (InventoryDb db) =>
    {
        var items = await db.InventoryItems.OrderBy(i => i.ProductId).ToListAsync();

        return items.Select(i => new
        {
            Product = (Product)i.ProductId,
            QuantityOnHand = i.QuantityOnHand,
        });
    }
);

app.Run();

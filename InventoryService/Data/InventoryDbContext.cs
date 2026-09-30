using InventoryService.Models;
using Microsoft.EntityFrameworkCore;

public class InventoryDb : DbContext
{
    public InventoryDb(DbContextOptions<InventoryDb> options)
        : base(options) { }

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<ProcessedOrder> ProcessedOrders => Set<ProcessedOrder>();

    public DbSet<ProcessedOrderItem> ProcessedOrderItems => Set<ProcessedOrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.ProductId).ValueGeneratedNever();

            // seed stock levels matching the OrderService product catalog (IDs 1-10)
            entity.HasData(
                new InventoryItem { ProductId = 1, QuantityOnHand = 100 },
                new InventoryItem { ProductId = 2, QuantityOnHand = 80 },
                new InventoryItem { ProductId = 3, QuantityOnHand = 60 },
                new InventoryItem { ProductId = 4, QuantityOnHand = 40 },
                new InventoryItem { ProductId = 5, QuantityOnHand = 120 },
                new InventoryItem { ProductId = 6, QuantityOnHand = 200 },
                new InventoryItem { ProductId = 7, QuantityOnHand = 30 },
                new InventoryItem { ProductId = 8, QuantityOnHand = 25 },
                new InventoryItem { ProductId = 9, QuantityOnHand = 90 },
                new InventoryItem { ProductId = 10, QuantityOnHand = 70 }
            );
        });

        modelBuilder.Entity<ProcessedOrder>(entity =>
        {
            entity.HasKey(o => o.OrderId);
            entity.Property(o => o.OrderId).ValueGeneratedNever();
        });

        modelBuilder.Entity<ProcessedOrderItem>(entity =>
        {
            entity.HasOne<ProcessedOrder>()
                .WithMany()
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

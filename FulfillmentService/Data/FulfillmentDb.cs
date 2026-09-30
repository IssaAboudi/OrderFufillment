using FulfillmentService.Models;
using Microsoft.EntityFrameworkCore;

public class FulfillmentDb : DbContext
{
    public FulfillmentDb(DbContextOptions<FulfillmentDb> options)
        : base(options) { }

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.HasKey(r => r.OrderId);
            entity.Property(r => r.OrderId).ValueGeneratedNever();
        });

        modelBuilder.Entity<ReceiptItem>(entity =>
        {
            entity.HasOne<Receipt>()
                .WithMany(r => r.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using OrderService.Models;

public class OrderDb : DbContext
{
    public OrderDb(DbContextOptions<OrderDb> options)
        : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<OrderItem>()
            .HasOne(item => item.Order)
            .WithMany(order => order.Items)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder
            .Entity<OrderItem>()
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // pre-populate a product catalog (assuming restaurant)
        modelBuilder
            .Entity<Product>()
            .HasData(
                new Product
                {
                    Id = 1,
                    Name = "Classic Cheeseburger",
                    Description = "Angus beef patty, cheddar, lettuce, tomato, house sauce",
                    Price = 9.99m,
                    Category = "Main",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 2,
                    Name = "Margherita Pizza",
                    Description = "San Marzano tomato, fresh mozzarella, basil",
                    Price = 12.50m,
                    Category = "Main",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 3,
                    Name = "Grilled Chicken Wrap",
                    Description = "Grilled chicken, garlic sauce, pickles, lettuce",
                    Price = 8.75m,
                    Category = "Main",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 4,
                    Name = "Caesar Salad",
                    Description = "Romaine, parmesan, croutons, Caesar dressing",
                    Price = 7.25m,
                    Category = "Starter",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 5,
                    Name = "Garlic Bread",
                    Description = "Toasted baguette with garlic butter and herbs",
                    Price = 4.50m,
                    Category = "Starter",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 6,
                    Name = "French Fries",
                    Description = "Crispy golden fries with sea salt",
                    Price = 3.50m,
                    Category = "Side",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 7,
                    Name = "Chocolate Lava Cake",
                    Description = "Warm chocolate cake with a molten center",
                    Price = 6.00m,
                    Category = "Dessert",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 8,
                    Name = "New York Cheesecake",
                    Description = "Classic baked cheesecake with berry coulis",
                    Price = 5.50m,
                    Category = "Dessert",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 9,
                    Name = "Fresh Orange Juice",
                    Description = "Freshly squeezed oranges",
                    Price = 3.25m,
                    Category = "Drink",
                    IsAvailable = true,
                },
                new Product
                {
                    Id = 10,
                    Name = "Iced Latte",
                    Description = "Espresso, cold milk, ice",
                    Price = 4.00m,
                    Category = "Drink",
                    IsAvailable = true,
                }
            );
    }
}

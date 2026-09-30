using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OrderService.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Category", "Description", "IsAvailable", "Name", "Price" },
                values: new object[,]
                {
                    { 1, "Main", "Angus beef patty, cheddar, lettuce, tomato, house sauce", true, "Classic Cheeseburger", 9.99m },
                    { 2, "Main", "San Marzano tomato, fresh mozzarella, basil", true, "Margherita Pizza", 12.50m },
                    { 3, "Main", "Grilled chicken, garlic sauce, pickles, lettuce", true, "Grilled Chicken Wrap", 8.75m },
                    { 4, "Starter", "Romaine, parmesan, croutons, Caesar dressing", true, "Caesar Salad", 7.25m },
                    { 5, "Starter", "Toasted baguette with garlic butter and herbs", true, "Garlic Bread", 4.50m },
                    { 6, "Side", "Crispy golden fries with sea salt", true, "French Fries", 3.50m },
                    { 7, "Dessert", "Warm chocolate cake with a molten center", true, "Chocolate Lava Cake", 6.00m },
                    { 8, "Dessert", "Classic baked cheesecake with berry coulis", true, "New York Cheesecake", 5.50m },
                    { 9, "Drink", "Freshly squeezed oranges", true, "Fresh Orange Juice", 3.25m },
                    { 10, "Drink", "Espresso, cold milk, ice", true, "Iced Latte", 4.00m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems");
        }
    }
}

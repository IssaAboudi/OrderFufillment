using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace InventoryService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    QuantityOnHand = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.ProductId);
                });

            migrationBuilder.InsertData(
                table: "InventoryItems",
                columns: new[] { "ProductId", "QuantityOnHand" },
                values: new object[,]
                {
                    { 1, 100 },
                    { 2, 80 },
                    { 3, 60 },
                    { 4, 40 },
                    { 5, 120 },
                    { 6, 200 },
                    { 7, 30 },
                    { 8, 25 },
                    { 9, 90 },
                    { 10, 70 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryItems");
        }
    }
}

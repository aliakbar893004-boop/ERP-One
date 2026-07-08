using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropVariantStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Stock",
                table: "ProductVariants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Stock",
                table: "ProductVariants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Re-backfill the legacy Stock column from ProductStocks (sum across warehouses)
            // so a rollback restores per-variant totals.
            migrationBuilder.Sql(@"
                UPDATE v SET v.Stock = ISNULL(t.Qty, 0)
                FROM ProductVariants v
                LEFT JOIN (SELECT ProductVariantId, SUM(Quantity) AS Qty
                           FROM ProductStocks GROUP BY ProductVariantId) t
                  ON t.ProductVariantId = v.Id;");
        }
    }
}

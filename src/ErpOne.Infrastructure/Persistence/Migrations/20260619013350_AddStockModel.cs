using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductStocks_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductStocks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MovementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RefType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RefId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_ProductVariantId_WarehouseId",
                table: "ProductStocks",
                columns: new[] { "ProductVariantId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_WarehouseId",
                table: "ProductStocks",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductVariantId_WarehouseId",
                table: "StockMovements",
                columns: new[] { "ProductVariantId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseId",
                table: "StockMovements",
                column: "WarehouseId");

            // Backfill: seed opening balances from the temporary ProductVariants.Stock into the
            // default warehouse. Idempotent: only inserts when no Opening movement / stock row exists.
            // Assumes exactly one Warehouse with IsDefault = 1 (seeded in F0).
            migrationBuilder.Sql(@"
                DECLARE @wh INT = (SELECT TOP 1 Id FROM Warehouses WHERE IsDefault = 1 ORDER BY Id);
                IF @wh IS NULL
                    THROW 50000, 'No default warehouse (IsDefault=1) found; cannot backfill stock.', 1;

                DECLARE @now DATETIME2 = SYSUTCDATETIME();

                INSERT INTO StockMovements
                    (ProductVariantId, WarehouseId, Type, Quantity, UnitCost, MovementDate, RefType, RefId, Note, CreatedAt, CreatedBy)
                SELECT v.Id, @wh, 'Adjustment', v.Stock, v.CostPrice, @now, 'Opening', NULL,
                       N'Saldo awal migrasi F2', @now, 'migration'
                FROM ProductVariants v
                WHERE v.Stock > 0
                  AND NOT EXISTS (SELECT 1 FROM StockMovements m
                                  WHERE m.ProductVariantId = v.Id AND m.WarehouseId = @wh AND m.RefType = 'Opening');

                INSERT INTO ProductStocks
                    (ProductVariantId, WarehouseId, Quantity, CreatedAt, CreatedBy)
                SELECT v.Id, @wh, v.Stock, @now, 'migration'
                FROM ProductVariants v
                WHERE v.Stock > 0
                  AND NOT EXISTS (SELECT 1 FROM ProductStocks s
                                  WHERE s.ProductVariantId = v.Id AND s.WarehouseId = @wh);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductStocks");

            migrationBuilder.DropTable(
                name: "StockMovements");
        }
    }
}

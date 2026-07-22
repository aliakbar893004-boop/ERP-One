using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditedAmount",
                table: "T_SupplierInvoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "T_PurchaseReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GoodsReceiptId = table.Column<int>(type: "int", nullable: true),
                    SupplierInvoiceId = table.Column<int>(type: "int", nullable: true),
                    ReturnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RejectionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InventoryTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_PurchaseReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_PurchaseReturns_M_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "M_Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_PurchaseReturnLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseReturnId = table.Column<int>(type: "int", nullable: false),
                    GoodsReceiptLineId = table.Column<int>(type: "int", nullable: false),
                    SupplierInvoiceLineId = table.Column<int>(type: "int", nullable: true),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    VariantSku = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxRateSnapshot = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_PurchaseReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_PurchaseReturnLines_M_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "M_ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_PurchaseReturnLines_T_PurchaseReturns_PurchaseReturnId",
                        column: x => x.PurchaseReturnId,
                        principalTable: "T_PurchaseReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DateFormat", "ModifiedAt", "ModifiedBy", "Padding", "Prefix", "ResetPeriod", "Separator" },
                values: new object[] { 16, "PurchaseReturn", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "DN", "Monthly", "-" });

            migrationBuilder.CreateIndex(
                name: "IX_T_PurchaseReturnLines_GoodsReceiptLineId",
                table: "T_PurchaseReturnLines",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PurchaseReturnLines_ProductVariantId",
                table: "T_PurchaseReturnLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PurchaseReturnLines_PurchaseReturnId",
                table: "T_PurchaseReturnLines",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PurchaseReturns_ReturnNumber",
                table: "T_PurchaseReturns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_PurchaseReturns_SupplierId",
                table: "T_PurchaseReturns",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_PurchaseReturnLines");

            migrationBuilder.DropTable(
                name: "T_PurchaseReturns");

            migrationBuilder.DeleteData(
                table: "M_NumberSequences",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DropColumn(
                name: "CreditedAmount",
                table: "T_SupplierInvoices");
        }
    }
}

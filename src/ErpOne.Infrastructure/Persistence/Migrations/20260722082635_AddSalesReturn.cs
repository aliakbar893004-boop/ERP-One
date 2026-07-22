using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditedAmount",
                table: "T_CustomerInvoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "T_SalesReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: true),
                    CustomerInvoiceId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_T_SalesReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_SalesReturns_M_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "M_Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_SalesReturnLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesReturnId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderLineId = table.Column<int>(type: "int", nullable: false),
                    CustomerInvoiceLineId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_T_SalesReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_SalesReturnLines_M_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "M_ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_SalesReturnLines_T_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "T_SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DateFormat", "ModifiedAt", "ModifiedBy", "Padding", "Prefix", "ResetPeriod", "Separator" },
                values: new object[] { 17, "SalesReturn", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "CN", "Monthly", "-" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SalesReturnLines_DeliveryOrderLineId",
                table: "T_SalesReturnLines",
                column: "DeliveryOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SalesReturnLines_ProductVariantId",
                table: "T_SalesReturnLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SalesReturnLines_SalesReturnId",
                table: "T_SalesReturnLines",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SalesReturns_CustomerId",
                table: "T_SalesReturns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SalesReturns_ReturnNumber",
                table: "T_SalesReturns",
                column: "ReturnNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_SalesReturnLines");

            migrationBuilder.DropTable(
                name: "T_SalesReturns");

            migrationBuilder.DeleteData(
                table: "M_NumberSequences",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DropColumn(
                name: "CreditedAmount",
                table: "T_CustomerInvoices");
        }
    }
}

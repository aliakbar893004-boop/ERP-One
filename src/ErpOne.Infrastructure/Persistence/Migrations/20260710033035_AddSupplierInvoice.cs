using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_SupplierInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierInvoiceNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_SupplierInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_SupplierInvoices_M_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "M_Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_SupplierInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierInvoiceId = table.Column<int>(type: "int", nullable: false),
                    GoodsReceiptId = table.Column<int>(type: "int", nullable: false),
                    GoodsReceiptLineId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_T_SupplierInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_SupplierInvoiceLines_M_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "M_ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_SupplierInvoiceLines_T_GoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "T_GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_SupplierInvoiceLines_T_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "T_SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DateFormat", "ModifiedAt", "ModifiedBy", "Padding", "Prefix", "ResetPeriod", "Separator" },
                values: new object[] { 7, "SupplierInvoice", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "APV", "Monthly", "-" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierInvoiceLines_GoodsReceiptId",
                table: "T_SupplierInvoiceLines",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierInvoiceLines_ProductVariantId",
                table: "T_SupplierInvoiceLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierInvoiceLines_SupplierInvoiceId",
                table: "T_SupplierInvoiceLines",
                column: "SupplierInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierInvoices_InvoiceNumber",
                table: "T_SupplierInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierInvoices_SupplierId",
                table: "T_SupplierInvoices",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_SupplierInvoiceLines");

            migrationBuilder.DropTable(
                name: "T_SupplierInvoices");

            migrationBuilder.DeleteData(
                table: "M_NumberSequences",
                keyColumn: "Id",
                keyValue: 7);
        }
    }
}

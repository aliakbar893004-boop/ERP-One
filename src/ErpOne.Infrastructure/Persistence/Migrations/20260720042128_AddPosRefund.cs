using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPosRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_PosRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RefundNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PosSaleId = table.Column<int>(type: "int", nullable: false),
                    CashierShiftId = table.Column<int>(type: "int", nullable: false),
                    RefundDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentMethodId = table.Column<int>(type: "int", nullable: false),
                    IsCashPayment = table.Column<bool>(type: "bit", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TransactionDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CogsTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AuthorizedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CashierUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CashierName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_PosRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_PosRefunds_M_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "M_PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_PosRefunds_T_CashierShifts_CashierShiftId",
                        column: x => x.CashierShiftId,
                        principalTable: "T_CashierShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_PosRefunds_T_PosSales_PosSaleId",
                        column: x => x.PosSaleId,
                        principalTable: "T_PosSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_PosRefundLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PosRefundId = table.Column<int>(type: "int", nullable: false),
                    PosSaleLineId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    VariantSku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_PosRefundLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_PosRefundLines_M_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "M_ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_PosRefundLines_T_PosRefunds_PosRefundId",
                        column: x => x.PosRefundId,
                        principalTable: "T_PosRefunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_T_PosRefundLines_T_PosSaleLines_PosSaleLineId",
                        column: x => x.PosSaleLineId,
                        principalTable: "T_PosSaleLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DateFormat", "ModifiedAt", "ModifiedBy", "Padding", "Prefix", "ResetPeriod", "Separator" },
                values: new object[] { 15, "PosRefund", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMMdd", null, null, 4, "RFN", "Daily", "-" });

            migrationBuilder.CreateIndex(
                name: "IX_T_PosRefundLines_PosRefundId",
                table: "T_PosRefundLines",
                column: "PosRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PosRefundLines_PosSaleLineId",
                table: "T_PosRefundLines",
                column: "PosSaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PosRefundLines_ProductVariantId",
                table: "T_PosRefundLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PosRefunds_CashierShiftId",
                table: "T_PosRefunds",
                column: "CashierShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PosRefunds_PaymentMethodId",
                table: "T_PosRefunds",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PosRefunds_PosSaleId",
                table: "T_PosRefunds",
                column: "PosSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_T_PosRefunds_RefundNumber",
                table: "T_PosRefunds",
                column: "RefundNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_PosRefundLines");

            migrationBuilder.DropTable(
                name: "T_PosRefunds");

            migrationBuilder.DeleteData(
                table: "M_NumberSequences",
                keyColumn: "Id",
                keyValue: 15);
        }
    }
}

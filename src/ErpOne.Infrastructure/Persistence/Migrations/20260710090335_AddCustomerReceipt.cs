using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_CustomerReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CashBankAccountId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_CustomerReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_CustomerReceipts_M_CashBankAccounts_CashBankAccountId",
                        column: x => x.CashBankAccountId,
                        principalTable: "M_CashBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_CustomerReceipts_M_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "M_Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_CustomerReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerReceiptId = table.Column<int>(type: "int", nullable: false),
                    CustomerInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_CustomerReceiptAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_CustomerReceiptAllocations_T_CustomerInvoices_CustomerInvoiceId",
                        column: x => x.CustomerInvoiceId,
                        principalTable: "T_CustomerInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_CustomerReceiptAllocations_T_CustomerReceipts_CustomerReceiptId",
                        column: x => x.CustomerReceiptId,
                        principalTable: "T_CustomerReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DateFormat", "ModifiedAt", "ModifiedBy", "Padding", "Prefix", "ResetPeriod", "Separator" },
                values: new object[] { 10, "CustomerReceipt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "ARR", "Monthly", "-" });

            migrationBuilder.CreateIndex(
                name: "IX_T_CustomerReceiptAllocations_CustomerInvoiceId",
                table: "T_CustomerReceiptAllocations",
                column: "CustomerInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_T_CustomerReceiptAllocations_CustomerReceiptId",
                table: "T_CustomerReceiptAllocations",
                column: "CustomerReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_T_CustomerReceipts_CashBankAccountId",
                table: "T_CustomerReceipts",
                column: "CashBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_T_CustomerReceipts_CustomerId",
                table: "T_CustomerReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_T_CustomerReceipts_ReceiptNumber",
                table: "T_CustomerReceipts",
                column: "ReceiptNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_CustomerReceiptAllocations");

            migrationBuilder.DropTable(
                name: "T_CustomerReceipts");

            migrationBuilder.DeleteData(
                table: "M_NumberSequences",
                keyColumn: "Id",
                keyValue: 10);
        }
    }
}

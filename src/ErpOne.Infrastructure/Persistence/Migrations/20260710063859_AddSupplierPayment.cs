using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "S_CashBankMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CashBankAccountId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RefId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_S_CashBankMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_S_CashBankMovements_M_CashBankAccounts_CashBankAccountId",
                        column: x => x.CashBankAccountId,
                        principalTable: "M_CashBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    CashBankAccountId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RejectionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_SupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_SupplierPayments_M_CashBankAccounts_CashBankAccountId",
                        column: x => x.CashBankAccountId,
                        principalTable: "M_CashBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_SupplierPayments_M_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "M_Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "T_SupplierPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierPaymentId = table.Column<int>(type: "int", nullable: false),
                    SupplierInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_SupplierPaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_SupplierPaymentAllocations_T_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "T_SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_T_SupplierPaymentAllocations_T_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "T_SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DateFormat", "ModifiedAt", "ModifiedBy", "Padding", "Prefix", "ResetPeriod", "Separator" },
                values: new object[] { 8, "SupplierPayment", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "APP", "Monthly", "-" });

            migrationBuilder.CreateIndex(
                name: "IX_S_CashBankMovements_CashBankAccountId_Date",
                table: "S_CashBankMovements",
                columns: new[] { "CashBankAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierPaymentAllocations_SupplierInvoiceId",
                table: "T_SupplierPaymentAllocations",
                column: "SupplierInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierPaymentAllocations_SupplierPaymentId",
                table: "T_SupplierPaymentAllocations",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierPayments_CashBankAccountId",
                table: "T_SupplierPayments",
                column: "CashBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierPayments_PaymentNumber",
                table: "T_SupplierPayments",
                column: "PaymentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_SupplierPayments_SupplierId",
                table: "T_SupplierPayments",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "S_CashBankMovements");

            migrationBuilder.DropTable(
                name: "T_SupplierPaymentAllocations");

            migrationBuilder.DropTable(
                name: "T_SupplierPayments");

            migrationBuilder.DeleteData(
                table: "M_NumberSequences",
                keyColumn: "Id",
                keyValue: 8);
        }
    }
}

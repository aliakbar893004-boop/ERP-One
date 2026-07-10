using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddF0Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M_CompanySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ReceiptHeader = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiptFooter = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_CompanySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_Currencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    IsBase = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_NumberSequenceCounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SequenceCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_NumberSequenceCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_NumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Padding = table.Column<int>(type: "int", nullable: false),
                    ResetPeriod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Separator = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_NumberSequences", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "M_CompanySettings",
                columns: new[] { "Id", "Address", "CompanyName", "CreatedAt", "CreatedBy", "Email", "LogoUrl", "ModifiedAt", "ModifiedBy", "Phone", "ReceiptFooter", "ReceiptHeader", "TaxId" },
                values: new object[] { 1, null, "ERP_One", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", null, null, null, null, null, null, null, null });

            migrationBuilder.InsertData(
                table: "M_Currencies",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DecimalPlaces", "IsActive", "IsBase", "ModifiedAt", "ModifiedBy", "Name", "Symbol" },
                values: new object[] { 1, "IDR", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", 0, true, true, null, null, "Rupiah", "Rp" });

            migrationBuilder.InsertData(
                table: "M_NumberSequences",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "DateFormat", "ModifiedAt", "ModifiedBy", "Padding", "Prefix", "ResetPeriod", "Separator" },
                values: new object[,]
                {
                    { 1, "PurchaseOrder", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "PO", "Monthly", "-" },
                    { 2, "SalesOrder", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "SO", "Monthly", "-" },
                    { 3, "GoodsReceipt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "GRN", "Monthly", "-" },
                    { 4, "DeliveryOrder", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMM", null, null, 4, "DO", "Monthly", "-" },
                    { 5, "PosSale", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMMdd", null, null, 4, "POS", "Daily", "-" },
                    { 6, "CashierShift", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "yyyyMMdd", null, null, 4, "SHIFT", "Daily", "-" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_M_Currencies_Code",
                table: "M_Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_M_NumberSequenceCounters_SequenceCode_PeriodKey",
                table: "M_NumberSequenceCounters",
                columns: new[] { "SequenceCode", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_M_NumberSequences_Code",
                table: "M_NumberSequences",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "M_CompanySettings");

            migrationBuilder.DropTable(
                name: "M_Currencies");

            migrationBuilder.DropTable(
                name: "M_NumberSequenceCounters");

            migrationBuilder.DropTable(
                name: "M_NumberSequences");
        }
    }
}

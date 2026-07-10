using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashBankAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M_CashBankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccountHolder = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_CashBankAccounts", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "M_CashBankAccounts",
                columns: new[] { "Id", "AccountHolder", "AccountNumber", "BankName", "Code", "CreatedAt", "CreatedBy", "Currency", "IsActive", "ModifiedAt", "ModifiedBy", "Name", "OpeningBalance", "Type" },
                values: new object[] { 1, null, null, null, "CASH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "IDR", true, null, null, "Main Cash", 0m, "Cash" });

            migrationBuilder.CreateIndex(
                name: "IX_M_CashBankAccounts_Code",
                table: "M_CashBankAccounts",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "M_CashBankAccounts");
        }
    }
}

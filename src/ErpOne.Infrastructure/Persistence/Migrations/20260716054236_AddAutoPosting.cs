using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "T_JournalEntries",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "T_JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "T_JournalEntries",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GlAccountId",
                table: "M_ExpenseCategories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GlAccountId",
                table: "M_CashBankAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "M_PostingConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ArAccountId = table.Column<int>(type: "int", nullable: true),
                    ApAccountId = table.Column<int>(type: "int", nullable: true),
                    InventoryAccountId = table.Column<int>(type: "int", nullable: true),
                    GrIrAccountId = table.Column<int>(type: "int", nullable: true),
                    SalesAccountId = table.Column<int>(type: "int", nullable: true),
                    CogsAccountId = table.Column<int>(type: "int", nullable: true),
                    InputTaxAccountId = table.Column<int>(type: "int", nullable: true),
                    OutputTaxAccountId = table.Column<int>(type: "int", nullable: true),
                    PosCashAccountId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_PostingConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_ApAccountId",
                        column: x => x.ApAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_ArAccountId",
                        column: x => x.ArAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_CogsAccountId",
                        column: x => x.CogsAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_GrIrAccountId",
                        column: x => x.GrIrAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_InputTaxAccountId",
                        column: x => x.InputTaxAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_InventoryAccountId",
                        column: x => x.InventoryAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_OutputTaxAccountId",
                        column: x => x.OutputTaxAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_PosCashAccountId",
                        column: x => x.PosCashAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_M_PostingConfigurations_M_Accounts_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "M_Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "M_CashBankAccounts",
                keyColumn: "Id",
                keyValue: 1,
                column: "GlAccountId",
                value: null);

            migrationBuilder.InsertData(
                table: "M_PostingConfigurations",
                columns: new[] { "Id", "ApAccountId", "ArAccountId", "CogsAccountId", "CreatedAt", "CreatedBy", "GrIrAccountId", "InputTaxAccountId", "InventoryAccountId", "ModifiedAt", "ModifiedBy", "OutputTaxAccountId", "PosCashAccountId", "SalesAccountId" },
                values: new object[] { 1, null, null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", null, null, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_T_JournalEntries_SourceType_SourceId",
                table: "T_JournalEntries",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_M_ExpenseCategories_GlAccountId",
                table: "M_ExpenseCategories",
                column: "GlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_CashBankAccounts_GlAccountId",
                table: "M_CashBankAccounts",
                column: "GlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_ApAccountId",
                table: "M_PostingConfigurations",
                column: "ApAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_ArAccountId",
                table: "M_PostingConfigurations",
                column: "ArAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_CogsAccountId",
                table: "M_PostingConfigurations",
                column: "CogsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_GrIrAccountId",
                table: "M_PostingConfigurations",
                column: "GrIrAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_InputTaxAccountId",
                table: "M_PostingConfigurations",
                column: "InputTaxAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_InventoryAccountId",
                table: "M_PostingConfigurations",
                column: "InventoryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_OutputTaxAccountId",
                table: "M_PostingConfigurations",
                column: "OutputTaxAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_PosCashAccountId",
                table: "M_PostingConfigurations",
                column: "PosCashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_SalesAccountId",
                table: "M_PostingConfigurations",
                column: "SalesAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_M_CashBankAccounts_M_Accounts_GlAccountId",
                table: "M_CashBankAccounts",
                column: "GlAccountId",
                principalTable: "M_Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_M_ExpenseCategories_M_Accounts_GlAccountId",
                table: "M_ExpenseCategories",
                column: "GlAccountId",
                principalTable: "M_Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_M_CashBankAccounts_M_Accounts_GlAccountId",
                table: "M_CashBankAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_M_ExpenseCategories_M_Accounts_GlAccountId",
                table: "M_ExpenseCategories");

            migrationBuilder.DropTable(
                name: "M_PostingConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_T_JournalEntries_SourceType_SourceId",
                table: "T_JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_M_ExpenseCategories_GlAccountId",
                table: "M_ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_M_CashBankAccounts_GlAccountId",
                table: "M_CashBankAccounts");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "T_JournalEntries");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "T_JournalEntries");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "T_JournalEntries");

            migrationBuilder.DropColumn(
                name: "GlAccountId",
                table: "M_ExpenseCategories");

            migrationBuilder.DropColumn(
                name: "GlAccountId",
                table: "M_CashBankAccounts");
        }
    }
}

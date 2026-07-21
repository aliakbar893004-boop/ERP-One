using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasePriceVarianceAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchasePriceVarianceAccountId",
                table: "M_PostingConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "M_PostingConfigurations",
                keyColumn: "Id",
                keyValue: 1,
                column: "PurchasePriceVarianceAccountId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_M_PostingConfigurations_PurchasePriceVarianceAccountId",
                table: "M_PostingConfigurations",
                column: "PurchasePriceVarianceAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_M_PostingConfigurations_M_Accounts_PurchasePriceVarianceAccountId",
                table: "M_PostingConfigurations",
                column: "PurchasePriceVarianceAccountId",
                principalTable: "M_Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_M_PostingConfigurations_M_Accounts_PurchasePriceVarianceAccountId",
                table: "M_PostingConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_M_PostingConfigurations_PurchasePriceVarianceAccountId",
                table: "M_PostingConfigurations");

            migrationBuilder.DropColumn(
                name: "PurchasePriceVarianceAccountId",
                table: "M_PostingConfigurations");
        }
    }
}

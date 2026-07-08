using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedShiftPerWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashierShifts_CashierUserId",
                table: "CashierShifts");

            migrationBuilder.DropIndex(
                name: "IX_CashierShifts_WarehouseId",
                table: "CashierShifts");

            // Tambah nullable dulu agar bisa di-backfill sebelum jadi NOT NULL.
            migrationBuilder.AddColumn<string>(
                name: "CashierName",
                table: "PosSales",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CashierUserId",
                table: "PosSales",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            // Backfill riwayat: kasir = pembuka shift-nya.
            migrationBuilder.Sql(@"
                UPDATE p
                SET p.CashierUserId = s.CashierUserId,
                    p.CashierName    = s.CashierName
                FROM PosSales p
                INNER JOIN CashierShifts s ON s.Id = p.CashierShiftId;");

            migrationBuilder.AlterColumn<string>(
                name: "CashierName",
                table: "PosSales",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CashierUserId",
                table: "PosSales",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CashierShifts_Warehouse_Open",
                table: "CashierShifts",
                column: "WarehouseId",
                unique: true,
                filter: "[Status] = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CashierShifts_Warehouse_Open",
                table: "CashierShifts");

            migrationBuilder.DropColumn(
                name: "CashierName",
                table: "PosSales");

            migrationBuilder.DropColumn(
                name: "CashierUserId",
                table: "PosSales");

            migrationBuilder.CreateIndex(
                name: "IX_CashierShifts_CashierUserId",
                table: "CashierShifts",
                column: "CashierUserId",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_CashierShifts_WarehouseId",
                table: "CashierShifts",
                column: "WarehouseId");
        }
    }
}

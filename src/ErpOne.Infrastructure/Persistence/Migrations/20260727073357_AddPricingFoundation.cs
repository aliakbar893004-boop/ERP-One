using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultPriceListId",
                table: "M_Warehouses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceListId",
                table: "M_Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountPercent",
                table: "AspNetRoles",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "M_PriceLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_PriceLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_PricingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefaultMaxDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_PricingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_PriceListLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PriceListId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    MinQty = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_PriceListLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_M_PriceListLines_M_PriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "M_PriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_M_PriceListLines_M_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "M_ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "M_PricingSettings",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DefaultMaxDiscountPercent", "ModifiedAt", "ModifiedBy" },
                values: new object[] { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", 100m, null, null });

            migrationBuilder.UpdateData(
                table: "M_Warehouses",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultPriceListId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_M_Warehouses_DefaultPriceListId",
                table: "M_Warehouses",
                column: "DefaultPriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_M_Customers_PriceListId",
                table: "M_Customers",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PriceListLines_PriceListId_ProductVariantId_MinQty",
                table: "M_PriceListLines",
                columns: new[] { "PriceListId", "ProductVariantId", "MinQty" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_M_PriceListLines_ProductVariantId",
                table: "M_PriceListLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_M_PriceLists_Code",
                table: "M_PriceLists",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_M_Customers_M_PriceLists_PriceListId",
                table: "M_Customers",
                column: "PriceListId",
                principalTable: "M_PriceLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_M_Warehouses_M_PriceLists_DefaultPriceListId",
                table: "M_Warehouses",
                column: "DefaultPriceListId",
                principalTable: "M_PriceLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_M_Customers_M_PriceLists_PriceListId",
                table: "M_Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_M_Warehouses_M_PriceLists_DefaultPriceListId",
                table: "M_Warehouses");

            migrationBuilder.DropTable(
                name: "M_PriceListLines");

            migrationBuilder.DropTable(
                name: "M_PricingSettings");

            migrationBuilder.DropTable(
                name: "M_PriceLists");

            migrationBuilder.DropIndex(
                name: "IX_M_Warehouses_DefaultPriceListId",
                table: "M_Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_M_Customers_PriceListId",
                table: "M_Customers");

            migrationBuilder.DropColumn(
                name: "DefaultPriceListId",
                table: "M_Warehouses");

            migrationBuilder.DropColumn(
                name: "PriceListId",
                table: "M_Customers");

            migrationBuilder.DropColumn(
                name: "MaxDiscountPercent",
                table: "AspNetRoles");
        }
    }
}

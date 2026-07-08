using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorProductToVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Rename Sku -> Code (preserves existing SKU value as the product base code).
            migrationBuilder.RenameColumn(
                name: "Sku",
                table: "Products",
                newName: "Code");

            migrationBuilder.RenameIndex(
                name: "IX_Products_Sku",
                table: "Products",
                newName: "IX_Products_Code");

            // 2. Add parent FK columns.
            migrationBuilder.AddColumn<int>(
                name: "BaseUnitId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxId",
                table: "Products",
                type: "int",
                nullable: true);

            // 3. Create variant tables.
            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CostPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Dimensions = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Stock = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantAttributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    AttributeValueId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributes_AttributeValues_AttributeValueId",
                        column: x => x.AttributeValueId,
                        principalTable: "AttributeValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributes_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 4. Backfill: 1 varian default per produk lama (salin SKU/harga/stok), SEBELUM kolom lama di-drop.
            migrationBuilder.Sql(@"
                INSERT INTO ProductVariants (ProductId, Sku, Barcode, Price, DiscountPrice, CostPrice, Weight, Dimensions, Stock, IsActive, CreatedAt, CreatedBy)
                SELECT Id, Code, NULL, Price, DiscountPrice, 0, Weight, Dimensions, Stock, 1, SYSUTCDATETIME(), 'migration'
                FROM Products;");

            // 5. Indexes.
            migrationBuilder.CreateIndex(
                name: "IX_Products_BaseUnitId",
                table: "Products",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId",
                table: "Products",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TaxId",
                table: "Products",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributes_AttributeValueId",
                table: "ProductVariantAttributes",
                column: "AttributeValueId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributes_ProductVariantId",
                table: "ProductVariantAttributes",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Sku",
                table: "ProductVariants",
                column: "Sku",
                unique: true);

            // 6. Parent FKs.
            migrationBuilder.AddForeignKey(
                name: "FK_Products_Brands_BrandId",
                table: "Products",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Taxes_TaxId",
                table: "Products",
                column: "TaxId",
                principalTable: "Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Units_BaseUnitId",
                table: "Products",
                column: "BaseUnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 7. Drop kolom lama dari Products (data sudah dipindah ke ProductVariants).
            migrationBuilder.DropColumn(name: "Dimensions", table: "Products");
            migrationBuilder.DropColumn(name: "DiscountPrice", table: "Products");
            migrationBuilder.DropColumn(name: "Price", table: "Products");
            migrationBuilder.DropColumn(name: "Stock", table: "Products");
            migrationBuilder.DropColumn(name: "Weight", table: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Re-add kolom lama ke Products.
            migrationBuilder.AddColumn<string>(
                name: "Dimensions",
                table: "Products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPrice",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Stock",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Weight",
                table: "Products",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            // 2. Backfill balik dari varian pertama tiap produk (produk multi-varian hanya pulih varian pertamanya).
            migrationBuilder.Sql(@"
                UPDATE p SET p.Price = v.Price, p.DiscountPrice = v.DiscountPrice,
                             p.Stock = v.Stock, p.Weight = v.Weight, p.Dimensions = v.Dimensions
                FROM Products p
                CROSS APPLY (SELECT TOP 1 * FROM ProductVariants pv WHERE pv.ProductId = p.Id ORDER BY pv.Id) v;");

            // 3. Drop FK & tabel varian.
            migrationBuilder.DropForeignKey(name: "FK_Products_Brands_BrandId", table: "Products");
            migrationBuilder.DropForeignKey(name: "FK_Products_Taxes_TaxId", table: "Products");
            migrationBuilder.DropForeignKey(name: "FK_Products_Units_BaseUnitId", table: "Products");

            migrationBuilder.DropTable(name: "ProductVariantAttributes");
            migrationBuilder.DropTable(name: "ProductVariants");

            migrationBuilder.DropIndex(name: "IX_Products_BaseUnitId", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Products_BrandId", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Products_TaxId", table: "Products");

            migrationBuilder.DropColumn(name: "BaseUnitId", table: "Products");
            migrationBuilder.DropColumn(name: "BrandId", table: "Products");
            migrationBuilder.DropColumn(name: "TaxId", table: "Products");

            // 4. Rename Code -> Sku (pulihkan nama & nilai SKU).
            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Products",
                newName: "Sku");

            migrationBuilder.RenameIndex(
                name: "IX_Products_Code",
                table: "Products",
                newName: "IX_Products_Sku");
        }
    }
}

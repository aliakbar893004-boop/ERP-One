using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpOne.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrefixBusinessTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttributeValues_ProductAttributes_AttributeId",
                table: "AttributeValues");

            migrationBuilder.DropForeignKey(
                name: "FK_CashierShifts_Warehouses_WarehouseId",
                table: "CashierShifts");

            migrationBuilder.DropForeignKey(
                name: "FK_CashierShiftTotals_CashierShifts_CashierShiftId",
                table: "CashierShiftTotals");

            migrationBuilder.DropForeignKey(
                name: "FK_CashierShiftTotals_PaymentMethods_PaymentMethodId",
                table: "CashierShiftTotals");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrders_DeliveryOrderId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_ProductVariants_ProductVariantId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_SalesOrderLines_SalesOrderLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrders_SalesOrders_SalesOrderId",
                table: "DeliveryOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_ProductVariants_ProductVariantId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceipts_PurchaseOrders_PurchaseOrderId",
                table: "GoodsReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSaleLines_PosSales_PosSaleId",
                table: "PosSaleLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSaleLines_ProductVariants_ProductVariantId",
                table: "PosSaleLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSales_CashierShifts_CashierShiftId",
                table: "PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSales_PaymentMethods_PaymentMethodId",
                table: "PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSales_Taxes_TaxId",
                table: "PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSales_Warehouses_WarehouseId",
                table: "PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductImages_Products_ProductId",
                table: "ProductImages");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Brands_BrandId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductCategories_CategoryId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Taxes_TaxId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Units_BaseUnitId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_ProductVariants_ProductVariantId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Warehouses_WarehouseId",
                table: "ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributes_AttributeValues_AttributeValueId",
                table: "ProductVariantAttributes");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributes_ProductVariants_ProductVariantId",
                table: "ProductVariantAttributes");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariants_Products_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_ProductVariants_ProductVariantId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_Taxes_TaxId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Suppliers_SupplierId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Warehouses_WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_ProductVariants_ProductVariantId",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_Taxes_TaxId",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Customers_CustomerId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Warehouses_WarehouseId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_ProductVariants_ProductVariantId",
                table: "StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Warehouses_WarehouseId",
                table: "StockMovements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Warehouses",
                table: "Warehouses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Units",
                table: "Units");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Taxes",
                table: "Taxes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Suppliers",
                table: "Suppliers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StockMovements",
                table: "StockMovements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SalesOrders",
                table: "SalesOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SalesOrderLines",
                table: "SalesOrderLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PurchaseOrders",
                table: "PurchaseOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PurchaseOrderLines",
                table: "PurchaseOrderLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductVariants",
                table: "ProductVariants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductVariantAttributes",
                table: "ProductVariantAttributes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductStocks",
                table: "ProductStocks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Products",
                table: "Products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductImages",
                table: "ProductImages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductCategories",
                table: "ProductCategories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductAttributes",
                table: "ProductAttributes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PosSales",
                table: "PosSales");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PosSaleLines",
                table: "PosSaleLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PaymentMethods",
                table: "PaymentMethods");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GoodsReceipts",
                table: "GoodsReceipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GoodsReceiptLines",
                table: "GoodsReceiptLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DeliveryOrders",
                table: "DeliveryOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DeliveryOrderLines",
                table: "DeliveryOrderLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customers",
                table: "Customers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CashierShiftTotals",
                table: "CashierShiftTotals");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CashierShifts",
                table: "CashierShifts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Brands",
                table: "Brands");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AttributeValues",
                table: "AttributeValues");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApprovalSteps",
                table: "ApprovalSteps");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApprovalChainSteps",
                table: "ApprovalChainSteps");

            migrationBuilder.RenameTable(
                name: "Warehouses",
                newName: "M_Warehouses");

            migrationBuilder.RenameTable(
                name: "Units",
                newName: "M_Units");

            migrationBuilder.RenameTable(
                name: "Taxes",
                newName: "M_Taxes");

            migrationBuilder.RenameTable(
                name: "Suppliers",
                newName: "M_Suppliers");

            migrationBuilder.RenameTable(
                name: "StockMovements",
                newName: "S_StockMovements");

            migrationBuilder.RenameTable(
                name: "SalesOrders",
                newName: "T_SalesOrders");

            migrationBuilder.RenameTable(
                name: "SalesOrderLines",
                newName: "T_SalesOrderLines");

            migrationBuilder.RenameTable(
                name: "PurchaseOrders",
                newName: "T_PurchaseOrders");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderLines",
                newName: "T_PurchaseOrderLines");

            migrationBuilder.RenameTable(
                name: "ProductVariants",
                newName: "M_ProductVariants");

            migrationBuilder.RenameTable(
                name: "ProductVariantAttributes",
                newName: "M_ProductVariantAttributes");

            migrationBuilder.RenameTable(
                name: "ProductStocks",
                newName: "S_ProductStocks");

            migrationBuilder.RenameTable(
                name: "Products",
                newName: "M_Products");

            migrationBuilder.RenameTable(
                name: "ProductImages",
                newName: "M_ProductImages");

            migrationBuilder.RenameTable(
                name: "ProductCategories",
                newName: "M_ProductCategories");

            migrationBuilder.RenameTable(
                name: "ProductAttributes",
                newName: "M_ProductAttributes");

            migrationBuilder.RenameTable(
                name: "PosSales",
                newName: "T_PosSales");

            migrationBuilder.RenameTable(
                name: "PosSaleLines",
                newName: "T_PosSaleLines");

            migrationBuilder.RenameTable(
                name: "PaymentMethods",
                newName: "M_PaymentMethods");

            migrationBuilder.RenameTable(
                name: "GoodsReceipts",
                newName: "T_GoodsReceipts");

            migrationBuilder.RenameTable(
                name: "GoodsReceiptLines",
                newName: "T_GoodsReceiptLines");

            migrationBuilder.RenameTable(
                name: "DeliveryOrders",
                newName: "T_DeliveryOrders");

            migrationBuilder.RenameTable(
                name: "DeliveryOrderLines",
                newName: "T_DeliveryOrderLines");

            migrationBuilder.RenameTable(
                name: "Customers",
                newName: "M_Customers");

            migrationBuilder.RenameTable(
                name: "CashierShiftTotals",
                newName: "T_CashierShiftTotals");

            migrationBuilder.RenameTable(
                name: "CashierShifts",
                newName: "T_CashierShifts");

            migrationBuilder.RenameTable(
                name: "Brands",
                newName: "M_Brands");

            migrationBuilder.RenameTable(
                name: "AttributeValues",
                newName: "M_AttributeValues");

            migrationBuilder.RenameTable(
                name: "ApprovalSteps",
                newName: "T_ApprovalSteps");

            migrationBuilder.RenameTable(
                name: "ApprovalChainSteps",
                newName: "M_ApprovalChainSteps");

            migrationBuilder.RenameIndex(
                name: "IX_Warehouses_Code",
                table: "M_Warehouses",
                newName: "IX_M_Warehouses_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Units_Code",
                table: "M_Units",
                newName: "IX_M_Units_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Taxes_Code",
                table: "M_Taxes",
                newName: "IX_M_Taxes_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Suppliers_Code",
                table: "M_Suppliers",
                newName: "IX_M_Suppliers_Code");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_WarehouseId",
                table: "S_StockMovements",
                newName: "IX_S_StockMovements_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_ProductVariantId_WarehouseId",
                table: "S_StockMovements",
                newName: "IX_S_StockMovements_ProductVariantId_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_SalesOrders_WarehouseId",
                table: "T_SalesOrders",
                newName: "IX_T_SalesOrders_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_SalesOrders_SoNumber",
                table: "T_SalesOrders",
                newName: "IX_T_SalesOrders_SoNumber");

            migrationBuilder.RenameIndex(
                name: "IX_SalesOrders_CustomerId",
                table: "T_SalesOrders",
                newName: "IX_T_SalesOrders_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_SalesOrderLines_TaxId",
                table: "T_SalesOrderLines",
                newName: "IX_T_SalesOrderLines_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_SalesOrderLines_SalesOrderId",
                table: "T_SalesOrderLines",
                newName: "IX_T_SalesOrderLines_SalesOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_SalesOrderLines_ProductVariantId",
                table: "T_SalesOrderLines",
                newName: "IX_T_SalesOrderLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrders_WarehouseId",
                table: "T_PurchaseOrders",
                newName: "IX_T_PurchaseOrders_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrders_SupplierId",
                table: "T_PurchaseOrders",
                newName: "IX_T_PurchaseOrders_SupplierId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrders_PoNumber",
                table: "T_PurchaseOrders",
                newName: "IX_T_PurchaseOrders_PoNumber");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrderLines_TaxId",
                table: "T_PurchaseOrderLines",
                newName: "IX_T_PurchaseOrderLines_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                table: "T_PurchaseOrderLines",
                newName: "IX_T_PurchaseOrderLines_PurchaseOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrderLines_ProductVariantId",
                table: "T_PurchaseOrderLines",
                newName: "IX_T_PurchaseOrderLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariants_Sku",
                table: "M_ProductVariants",
                newName: "IX_M_ProductVariants_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariants_ProductId",
                table: "M_ProductVariants",
                newName: "IX_M_ProductVariants_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariantAttributes_ProductVariantId",
                table: "M_ProductVariantAttributes",
                newName: "IX_M_ProductVariantAttributes_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariantAttributes_AttributeValueId",
                table: "M_ProductVariantAttributes",
                newName: "IX_M_ProductVariantAttributes_AttributeValueId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductStocks_WarehouseId",
                table: "S_ProductStocks",
                newName: "IX_S_ProductStocks_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductStocks_ProductVariantId_WarehouseId",
                table: "S_ProductStocks",
                newName: "IX_S_ProductStocks_ProductVariantId_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_Products_TaxId",
                table: "M_Products",
                newName: "IX_M_Products_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_Products_Code",
                table: "M_Products",
                newName: "IX_M_Products_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Products_CategoryId",
                table: "M_Products",
                newName: "IX_M_Products_CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_Products_BrandId",
                table: "M_Products",
                newName: "IX_M_Products_BrandId");

            migrationBuilder.RenameIndex(
                name: "IX_Products_BaseUnitId",
                table: "M_Products",
                newName: "IX_M_Products_BaseUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductImages_ProductId",
                table: "M_ProductImages",
                newName: "IX_M_ProductImages_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductCategories_Name",
                table: "M_ProductCategories",
                newName: "IX_M_ProductCategories_Name");

            migrationBuilder.RenameIndex(
                name: "IX_ProductCategories_Code",
                table: "M_ProductCategories",
                newName: "IX_M_ProductCategories_Code");

            migrationBuilder.RenameIndex(
                name: "IX_ProductAttributes_Code",
                table: "M_ProductAttributes",
                newName: "IX_M_ProductAttributes_Code");

            migrationBuilder.RenameIndex(
                name: "IX_PosSales_WarehouseId",
                table: "T_PosSales",
                newName: "IX_T_PosSales_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_PosSales_TaxId",
                table: "T_PosSales",
                newName: "IX_T_PosSales_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_PosSales_SaleNumber",
                table: "T_PosSales",
                newName: "IX_T_PosSales_SaleNumber");

            migrationBuilder.RenameIndex(
                name: "IX_PosSales_PaymentMethodId",
                table: "T_PosSales",
                newName: "IX_T_PosSales_PaymentMethodId");

            migrationBuilder.RenameIndex(
                name: "IX_PosSales_CashierShiftId",
                table: "T_PosSales",
                newName: "IX_T_PosSales_CashierShiftId");

            migrationBuilder.RenameIndex(
                name: "IX_PosSaleLines_ProductVariantId",
                table: "T_PosSaleLines",
                newName: "IX_T_PosSaleLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_PosSaleLines_PosSaleId",
                table: "T_PosSaleLines",
                newName: "IX_T_PosSaleLines_PosSaleId");

            migrationBuilder.RenameIndex(
                name: "IX_PaymentMethods_Code",
                table: "M_PaymentMethods",
                newName: "IX_M_PaymentMethods_Code");

            migrationBuilder.RenameIndex(
                name: "IX_GoodsReceipts_PurchaseOrderId",
                table: "T_GoodsReceipts",
                newName: "IX_T_GoodsReceipts_PurchaseOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_GoodsReceipts_GrnNumber",
                table: "T_GoodsReceipts",
                newName: "IX_T_GoodsReceipts_GrnNumber");

            migrationBuilder.RenameIndex(
                name: "IX_GoodsReceiptLines_PurchaseOrderLineId",
                table: "T_GoodsReceiptLines",
                newName: "IX_T_GoodsReceiptLines_PurchaseOrderLineId");

            migrationBuilder.RenameIndex(
                name: "IX_GoodsReceiptLines_ProductVariantId",
                table: "T_GoodsReceiptLines",
                newName: "IX_T_GoodsReceiptLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_GoodsReceiptLines_GoodsReceiptId",
                table: "T_GoodsReceiptLines",
                newName: "IX_T_GoodsReceiptLines_GoodsReceiptId");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryOrders_SalesOrderId",
                table: "T_DeliveryOrders",
                newName: "IX_T_DeliveryOrders_SalesOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryOrders_DoNumber",
                table: "T_DeliveryOrders",
                newName: "IX_T_DeliveryOrders_DoNumber");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryOrderLines_SalesOrderLineId",
                table: "T_DeliveryOrderLines",
                newName: "IX_T_DeliveryOrderLines_SalesOrderLineId");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryOrderLines_ProductVariantId",
                table: "T_DeliveryOrderLines",
                newName: "IX_T_DeliveryOrderLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryOrderLines_DeliveryOrderId",
                table: "T_DeliveryOrderLines",
                newName: "IX_T_DeliveryOrderLines_DeliveryOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_Code",
                table: "M_Customers",
                newName: "IX_M_Customers_Code");

            migrationBuilder.RenameIndex(
                name: "IX_CashierShiftTotals_PaymentMethodId",
                table: "T_CashierShiftTotals",
                newName: "IX_T_CashierShiftTotals_PaymentMethodId");

            migrationBuilder.RenameIndex(
                name: "IX_CashierShiftTotals_CashierShiftId",
                table: "T_CashierShiftTotals",
                newName: "IX_T_CashierShiftTotals_CashierShiftId");

            migrationBuilder.RenameIndex(
                name: "IX_CashierShifts_ShiftNumber",
                table: "T_CashierShifts",
                newName: "IX_T_CashierShifts_ShiftNumber");

            migrationBuilder.RenameIndex(
                name: "IX_Brands_Code",
                table: "M_Brands",
                newName: "IX_M_Brands_Code");

            migrationBuilder.RenameIndex(
                name: "IX_AttributeValues_AttributeId",
                table: "M_AttributeValues",
                newName: "IX_M_AttributeValues_AttributeId");

            migrationBuilder.RenameIndex(
                name: "IX_ApprovalSteps_DocumentType_DocumentId_StepOrder",
                table: "T_ApprovalSteps",
                newName: "IX_T_ApprovalSteps_DocumentType_DocumentId_StepOrder");

            migrationBuilder.RenameIndex(
                name: "IX_ApprovalChainSteps_DocumentType_StepOrder",
                table: "M_ApprovalChainSteps",
                newName: "IX_M_ApprovalChainSteps_DocumentType_StepOrder");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_Warehouses",
                table: "M_Warehouses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_Units",
                table: "M_Units",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_Taxes",
                table: "M_Taxes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_Suppliers",
                table: "M_Suppliers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_S_StockMovements",
                table: "S_StockMovements",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_SalesOrders",
                table: "T_SalesOrders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_SalesOrderLines",
                table: "T_SalesOrderLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_PurchaseOrders",
                table: "T_PurchaseOrders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_PurchaseOrderLines",
                table: "T_PurchaseOrderLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_ProductVariants",
                table: "M_ProductVariants",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_ProductVariantAttributes",
                table: "M_ProductVariantAttributes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_S_ProductStocks",
                table: "S_ProductStocks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_Products",
                table: "M_Products",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_ProductImages",
                table: "M_ProductImages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_ProductCategories",
                table: "M_ProductCategories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_ProductAttributes",
                table: "M_ProductAttributes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_PosSales",
                table: "T_PosSales",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_PosSaleLines",
                table: "T_PosSaleLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_PaymentMethods",
                table: "M_PaymentMethods",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_GoodsReceipts",
                table: "T_GoodsReceipts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_GoodsReceiptLines",
                table: "T_GoodsReceiptLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_DeliveryOrders",
                table: "T_DeliveryOrders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_DeliveryOrderLines",
                table: "T_DeliveryOrderLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_Customers",
                table: "M_Customers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_CashierShiftTotals",
                table: "T_CashierShiftTotals",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_CashierShifts",
                table: "T_CashierShifts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_Brands",
                table: "M_Brands",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_AttributeValues",
                table: "M_AttributeValues",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_T_ApprovalSteps",
                table: "T_ApprovalSteps",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_M_ApprovalChainSteps",
                table: "M_ApprovalChainSteps",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_M_AttributeValues_M_ProductAttributes_AttributeId",
                table: "M_AttributeValues",
                column: "AttributeId",
                principalTable: "M_ProductAttributes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_M_ProductImages_M_Products_ProductId",
                table: "M_ProductImages",
                column: "ProductId",
                principalTable: "M_Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_M_Products_M_Brands_BrandId",
                table: "M_Products",
                column: "BrandId",
                principalTable: "M_Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_M_Products_M_ProductCategories_CategoryId",
                table: "M_Products",
                column: "CategoryId",
                principalTable: "M_ProductCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_M_Products_M_Taxes_TaxId",
                table: "M_Products",
                column: "TaxId",
                principalTable: "M_Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_M_Products_M_Units_BaseUnitId",
                table: "M_Products",
                column: "BaseUnitId",
                principalTable: "M_Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_M_ProductVariantAttributes_M_AttributeValues_AttributeValueId",
                table: "M_ProductVariantAttributes",
                column: "AttributeValueId",
                principalTable: "M_AttributeValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_M_ProductVariantAttributes_M_ProductVariants_ProductVariantId",
                table: "M_ProductVariantAttributes",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_M_ProductVariants_M_Products_ProductId",
                table: "M_ProductVariants",
                column: "ProductId",
                principalTable: "M_Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_S_ProductStocks_M_ProductVariants_ProductVariantId",
                table: "S_ProductStocks",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_S_ProductStocks_M_Warehouses_WarehouseId",
                table: "S_ProductStocks",
                column: "WarehouseId",
                principalTable: "M_Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_S_StockMovements_M_ProductVariants_ProductVariantId",
                table: "S_StockMovements",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_S_StockMovements_M_Warehouses_WarehouseId",
                table: "S_StockMovements",
                column: "WarehouseId",
                principalTable: "M_Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_CashierShifts_M_Warehouses_WarehouseId",
                table: "T_CashierShifts",
                column: "WarehouseId",
                principalTable: "M_Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_CashierShiftTotals_M_PaymentMethods_PaymentMethodId",
                table: "T_CashierShiftTotals",
                column: "PaymentMethodId",
                principalTable: "M_PaymentMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_CashierShiftTotals_T_CashierShifts_CashierShiftId",
                table: "T_CashierShiftTotals",
                column: "CashierShiftId",
                principalTable: "T_CashierShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_T_DeliveryOrderLines_M_ProductVariants_ProductVariantId",
                table: "T_DeliveryOrderLines",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_DeliveryOrderLines_T_DeliveryOrders_DeliveryOrderId",
                table: "T_DeliveryOrderLines",
                column: "DeliveryOrderId",
                principalTable: "T_DeliveryOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_T_DeliveryOrderLines_T_SalesOrderLines_SalesOrderLineId",
                table: "T_DeliveryOrderLines",
                column: "SalesOrderLineId",
                principalTable: "T_SalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_DeliveryOrders_T_SalesOrders_SalesOrderId",
                table: "T_DeliveryOrders",
                column: "SalesOrderId",
                principalTable: "T_SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_GoodsReceiptLines_M_ProductVariants_ProductVariantId",
                table: "T_GoodsReceiptLines",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_GoodsReceiptLines_T_GoodsReceipts_GoodsReceiptId",
                table: "T_GoodsReceiptLines",
                column: "GoodsReceiptId",
                principalTable: "T_GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_T_GoodsReceiptLines_T_PurchaseOrderLines_PurchaseOrderLineId",
                table: "T_GoodsReceiptLines",
                column: "PurchaseOrderLineId",
                principalTable: "T_PurchaseOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_GoodsReceipts_T_PurchaseOrders_PurchaseOrderId",
                table: "T_GoodsReceipts",
                column: "PurchaseOrderId",
                principalTable: "T_PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PosSaleLines_M_ProductVariants_ProductVariantId",
                table: "T_PosSaleLines",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PosSaleLines_T_PosSales_PosSaleId",
                table: "T_PosSaleLines",
                column: "PosSaleId",
                principalTable: "T_PosSales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PosSales_M_PaymentMethods_PaymentMethodId",
                table: "T_PosSales",
                column: "PaymentMethodId",
                principalTable: "M_PaymentMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PosSales_M_Taxes_TaxId",
                table: "T_PosSales",
                column: "TaxId",
                principalTable: "M_Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PosSales_M_Warehouses_WarehouseId",
                table: "T_PosSales",
                column: "WarehouseId",
                principalTable: "M_Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PosSales_T_CashierShifts_CashierShiftId",
                table: "T_PosSales",
                column: "CashierShiftId",
                principalTable: "T_CashierShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PurchaseOrderLines_M_ProductVariants_ProductVariantId",
                table: "T_PurchaseOrderLines",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PurchaseOrderLines_M_Taxes_TaxId",
                table: "T_PurchaseOrderLines",
                column: "TaxId",
                principalTable: "M_Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PurchaseOrderLines_T_PurchaseOrders_PurchaseOrderId",
                table: "T_PurchaseOrderLines",
                column: "PurchaseOrderId",
                principalTable: "T_PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PurchaseOrders_M_Suppliers_SupplierId",
                table: "T_PurchaseOrders",
                column: "SupplierId",
                principalTable: "M_Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_PurchaseOrders_M_Warehouses_WarehouseId",
                table: "T_PurchaseOrders",
                column: "WarehouseId",
                principalTable: "M_Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_SalesOrderLines_M_ProductVariants_ProductVariantId",
                table: "T_SalesOrderLines",
                column: "ProductVariantId",
                principalTable: "M_ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_SalesOrderLines_M_Taxes_TaxId",
                table: "T_SalesOrderLines",
                column: "TaxId",
                principalTable: "M_Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_T_SalesOrderLines_T_SalesOrders_SalesOrderId",
                table: "T_SalesOrderLines",
                column: "SalesOrderId",
                principalTable: "T_SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_T_SalesOrders_M_Customers_CustomerId",
                table: "T_SalesOrders",
                column: "CustomerId",
                principalTable: "M_Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_T_SalesOrders_M_Warehouses_WarehouseId",
                table: "T_SalesOrders",
                column: "WarehouseId",
                principalTable: "M_Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_M_AttributeValues_M_ProductAttributes_AttributeId",
                table: "M_AttributeValues");

            migrationBuilder.DropForeignKey(
                name: "FK_M_ProductImages_M_Products_ProductId",
                table: "M_ProductImages");

            migrationBuilder.DropForeignKey(
                name: "FK_M_Products_M_Brands_BrandId",
                table: "M_Products");

            migrationBuilder.DropForeignKey(
                name: "FK_M_Products_M_ProductCategories_CategoryId",
                table: "M_Products");

            migrationBuilder.DropForeignKey(
                name: "FK_M_Products_M_Taxes_TaxId",
                table: "M_Products");

            migrationBuilder.DropForeignKey(
                name: "FK_M_Products_M_Units_BaseUnitId",
                table: "M_Products");

            migrationBuilder.DropForeignKey(
                name: "FK_M_ProductVariantAttributes_M_AttributeValues_AttributeValueId",
                table: "M_ProductVariantAttributes");

            migrationBuilder.DropForeignKey(
                name: "FK_M_ProductVariantAttributes_M_ProductVariants_ProductVariantId",
                table: "M_ProductVariantAttributes");

            migrationBuilder.DropForeignKey(
                name: "FK_M_ProductVariants_M_Products_ProductId",
                table: "M_ProductVariants");

            migrationBuilder.DropForeignKey(
                name: "FK_S_ProductStocks_M_ProductVariants_ProductVariantId",
                table: "S_ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_S_ProductStocks_M_Warehouses_WarehouseId",
                table: "S_ProductStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_S_StockMovements_M_ProductVariants_ProductVariantId",
                table: "S_StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_S_StockMovements_M_Warehouses_WarehouseId",
                table: "S_StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_T_CashierShifts_M_Warehouses_WarehouseId",
                table: "T_CashierShifts");

            migrationBuilder.DropForeignKey(
                name: "FK_T_CashierShiftTotals_M_PaymentMethods_PaymentMethodId",
                table: "T_CashierShiftTotals");

            migrationBuilder.DropForeignKey(
                name: "FK_T_CashierShiftTotals_T_CashierShifts_CashierShiftId",
                table: "T_CashierShiftTotals");

            migrationBuilder.DropForeignKey(
                name: "FK_T_DeliveryOrderLines_M_ProductVariants_ProductVariantId",
                table: "T_DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_DeliveryOrderLines_T_DeliveryOrders_DeliveryOrderId",
                table: "T_DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_DeliveryOrderLines_T_SalesOrderLines_SalesOrderLineId",
                table: "T_DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_DeliveryOrders_T_SalesOrders_SalesOrderId",
                table: "T_DeliveryOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_T_GoodsReceiptLines_M_ProductVariants_ProductVariantId",
                table: "T_GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_GoodsReceiptLines_T_GoodsReceipts_GoodsReceiptId",
                table: "T_GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_GoodsReceiptLines_T_PurchaseOrderLines_PurchaseOrderLineId",
                table: "T_GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_GoodsReceipts_T_PurchaseOrders_PurchaseOrderId",
                table: "T_GoodsReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PosSaleLines_M_ProductVariants_ProductVariantId",
                table: "T_PosSaleLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PosSaleLines_T_PosSales_PosSaleId",
                table: "T_PosSaleLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PosSales_M_PaymentMethods_PaymentMethodId",
                table: "T_PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PosSales_M_Taxes_TaxId",
                table: "T_PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PosSales_M_Warehouses_WarehouseId",
                table: "T_PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PosSales_T_CashierShifts_CashierShiftId",
                table: "T_PosSales");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PurchaseOrderLines_M_ProductVariants_ProductVariantId",
                table: "T_PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PurchaseOrderLines_M_Taxes_TaxId",
                table: "T_PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PurchaseOrderLines_T_PurchaseOrders_PurchaseOrderId",
                table: "T_PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PurchaseOrders_M_Suppliers_SupplierId",
                table: "T_PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_T_PurchaseOrders_M_Warehouses_WarehouseId",
                table: "T_PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_T_SalesOrderLines_M_ProductVariants_ProductVariantId",
                table: "T_SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_SalesOrderLines_M_Taxes_TaxId",
                table: "T_SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_SalesOrderLines_T_SalesOrders_SalesOrderId",
                table: "T_SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_T_SalesOrders_M_Customers_CustomerId",
                table: "T_SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_T_SalesOrders_M_Warehouses_WarehouseId",
                table: "T_SalesOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_SalesOrders",
                table: "T_SalesOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_SalesOrderLines",
                table: "T_SalesOrderLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_PurchaseOrders",
                table: "T_PurchaseOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_PurchaseOrderLines",
                table: "T_PurchaseOrderLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_PosSales",
                table: "T_PosSales");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_PosSaleLines",
                table: "T_PosSaleLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_GoodsReceipts",
                table: "T_GoodsReceipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_GoodsReceiptLines",
                table: "T_GoodsReceiptLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_DeliveryOrders",
                table: "T_DeliveryOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_DeliveryOrderLines",
                table: "T_DeliveryOrderLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_CashierShiftTotals",
                table: "T_CashierShiftTotals");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_CashierShifts",
                table: "T_CashierShifts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_T_ApprovalSteps",
                table: "T_ApprovalSteps");

            migrationBuilder.DropPrimaryKey(
                name: "PK_S_StockMovements",
                table: "S_StockMovements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_S_ProductStocks",
                table: "S_ProductStocks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_Warehouses",
                table: "M_Warehouses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_Units",
                table: "M_Units");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_Taxes",
                table: "M_Taxes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_Suppliers",
                table: "M_Suppliers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_ProductVariants",
                table: "M_ProductVariants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_ProductVariantAttributes",
                table: "M_ProductVariantAttributes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_Products",
                table: "M_Products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_ProductImages",
                table: "M_ProductImages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_ProductCategories",
                table: "M_ProductCategories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_ProductAttributes",
                table: "M_ProductAttributes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_PaymentMethods",
                table: "M_PaymentMethods");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_Customers",
                table: "M_Customers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_Brands",
                table: "M_Brands");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_AttributeValues",
                table: "M_AttributeValues");

            migrationBuilder.DropPrimaryKey(
                name: "PK_M_ApprovalChainSteps",
                table: "M_ApprovalChainSteps");

            migrationBuilder.RenameTable(
                name: "T_SalesOrders",
                newName: "SalesOrders");

            migrationBuilder.RenameTable(
                name: "T_SalesOrderLines",
                newName: "SalesOrderLines");

            migrationBuilder.RenameTable(
                name: "T_PurchaseOrders",
                newName: "PurchaseOrders");

            migrationBuilder.RenameTable(
                name: "T_PurchaseOrderLines",
                newName: "PurchaseOrderLines");

            migrationBuilder.RenameTable(
                name: "T_PosSales",
                newName: "PosSales");

            migrationBuilder.RenameTable(
                name: "T_PosSaleLines",
                newName: "PosSaleLines");

            migrationBuilder.RenameTable(
                name: "T_GoodsReceipts",
                newName: "GoodsReceipts");

            migrationBuilder.RenameTable(
                name: "T_GoodsReceiptLines",
                newName: "GoodsReceiptLines");

            migrationBuilder.RenameTable(
                name: "T_DeliveryOrders",
                newName: "DeliveryOrders");

            migrationBuilder.RenameTable(
                name: "T_DeliveryOrderLines",
                newName: "DeliveryOrderLines");

            migrationBuilder.RenameTable(
                name: "T_CashierShiftTotals",
                newName: "CashierShiftTotals");

            migrationBuilder.RenameTable(
                name: "T_CashierShifts",
                newName: "CashierShifts");

            migrationBuilder.RenameTable(
                name: "T_ApprovalSteps",
                newName: "ApprovalSteps");

            migrationBuilder.RenameTable(
                name: "S_StockMovements",
                newName: "StockMovements");

            migrationBuilder.RenameTable(
                name: "S_ProductStocks",
                newName: "ProductStocks");

            migrationBuilder.RenameTable(
                name: "M_Warehouses",
                newName: "Warehouses");

            migrationBuilder.RenameTable(
                name: "M_Units",
                newName: "Units");

            migrationBuilder.RenameTable(
                name: "M_Taxes",
                newName: "Taxes");

            migrationBuilder.RenameTable(
                name: "M_Suppliers",
                newName: "Suppliers");

            migrationBuilder.RenameTable(
                name: "M_ProductVariants",
                newName: "ProductVariants");

            migrationBuilder.RenameTable(
                name: "M_ProductVariantAttributes",
                newName: "ProductVariantAttributes");

            migrationBuilder.RenameTable(
                name: "M_Products",
                newName: "Products");

            migrationBuilder.RenameTable(
                name: "M_ProductImages",
                newName: "ProductImages");

            migrationBuilder.RenameTable(
                name: "M_ProductCategories",
                newName: "ProductCategories");

            migrationBuilder.RenameTable(
                name: "M_ProductAttributes",
                newName: "ProductAttributes");

            migrationBuilder.RenameTable(
                name: "M_PaymentMethods",
                newName: "PaymentMethods");

            migrationBuilder.RenameTable(
                name: "M_Customers",
                newName: "Customers");

            migrationBuilder.RenameTable(
                name: "M_Brands",
                newName: "Brands");

            migrationBuilder.RenameTable(
                name: "M_AttributeValues",
                newName: "AttributeValues");

            migrationBuilder.RenameTable(
                name: "M_ApprovalChainSteps",
                newName: "ApprovalChainSteps");

            migrationBuilder.RenameIndex(
                name: "IX_T_SalesOrders_WarehouseId",
                table: "SalesOrders",
                newName: "IX_SalesOrders_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_T_SalesOrders_SoNumber",
                table: "SalesOrders",
                newName: "IX_SalesOrders_SoNumber");

            migrationBuilder.RenameIndex(
                name: "IX_T_SalesOrders_CustomerId",
                table: "SalesOrders",
                newName: "IX_SalesOrders_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_T_SalesOrderLines_TaxId",
                table: "SalesOrderLines",
                newName: "IX_SalesOrderLines_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_T_SalesOrderLines_SalesOrderId",
                table: "SalesOrderLines",
                newName: "IX_SalesOrderLines_SalesOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_T_SalesOrderLines_ProductVariantId",
                table: "SalesOrderLines",
                newName: "IX_SalesOrderLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PurchaseOrders_WarehouseId",
                table: "PurchaseOrders",
                newName: "IX_PurchaseOrders_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PurchaseOrders_SupplierId",
                table: "PurchaseOrders",
                newName: "IX_PurchaseOrders_SupplierId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PurchaseOrders_PoNumber",
                table: "PurchaseOrders",
                newName: "IX_PurchaseOrders_PoNumber");

            migrationBuilder.RenameIndex(
                name: "IX_T_PurchaseOrderLines_TaxId",
                table: "PurchaseOrderLines",
                newName: "IX_PurchaseOrderLines_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PurchaseOrderLines_PurchaseOrderId",
                table: "PurchaseOrderLines",
                newName: "IX_PurchaseOrderLines_PurchaseOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PurchaseOrderLines_ProductVariantId",
                table: "PurchaseOrderLines",
                newName: "IX_PurchaseOrderLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PosSales_WarehouseId",
                table: "PosSales",
                newName: "IX_PosSales_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PosSales_TaxId",
                table: "PosSales",
                newName: "IX_PosSales_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PosSales_SaleNumber",
                table: "PosSales",
                newName: "IX_PosSales_SaleNumber");

            migrationBuilder.RenameIndex(
                name: "IX_T_PosSales_PaymentMethodId",
                table: "PosSales",
                newName: "IX_PosSales_PaymentMethodId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PosSales_CashierShiftId",
                table: "PosSales",
                newName: "IX_PosSales_CashierShiftId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PosSaleLines_ProductVariantId",
                table: "PosSaleLines",
                newName: "IX_PosSaleLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_T_PosSaleLines_PosSaleId",
                table: "PosSaleLines",
                newName: "IX_PosSaleLines_PosSaleId");

            migrationBuilder.RenameIndex(
                name: "IX_T_GoodsReceipts_PurchaseOrderId",
                table: "GoodsReceipts",
                newName: "IX_GoodsReceipts_PurchaseOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_T_GoodsReceipts_GrnNumber",
                table: "GoodsReceipts",
                newName: "IX_GoodsReceipts_GrnNumber");

            migrationBuilder.RenameIndex(
                name: "IX_T_GoodsReceiptLines_PurchaseOrderLineId",
                table: "GoodsReceiptLines",
                newName: "IX_GoodsReceiptLines_PurchaseOrderLineId");

            migrationBuilder.RenameIndex(
                name: "IX_T_GoodsReceiptLines_ProductVariantId",
                table: "GoodsReceiptLines",
                newName: "IX_GoodsReceiptLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_T_GoodsReceiptLines_GoodsReceiptId",
                table: "GoodsReceiptLines",
                newName: "IX_GoodsReceiptLines_GoodsReceiptId");

            migrationBuilder.RenameIndex(
                name: "IX_T_DeliveryOrders_SalesOrderId",
                table: "DeliveryOrders",
                newName: "IX_DeliveryOrders_SalesOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_T_DeliveryOrders_DoNumber",
                table: "DeliveryOrders",
                newName: "IX_DeliveryOrders_DoNumber");

            migrationBuilder.RenameIndex(
                name: "IX_T_DeliveryOrderLines_SalesOrderLineId",
                table: "DeliveryOrderLines",
                newName: "IX_DeliveryOrderLines_SalesOrderLineId");

            migrationBuilder.RenameIndex(
                name: "IX_T_DeliveryOrderLines_ProductVariantId",
                table: "DeliveryOrderLines",
                newName: "IX_DeliveryOrderLines_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_T_DeliveryOrderLines_DeliveryOrderId",
                table: "DeliveryOrderLines",
                newName: "IX_DeliveryOrderLines_DeliveryOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_T_CashierShiftTotals_PaymentMethodId",
                table: "CashierShiftTotals",
                newName: "IX_CashierShiftTotals_PaymentMethodId");

            migrationBuilder.RenameIndex(
                name: "IX_T_CashierShiftTotals_CashierShiftId",
                table: "CashierShiftTotals",
                newName: "IX_CashierShiftTotals_CashierShiftId");

            migrationBuilder.RenameIndex(
                name: "IX_T_CashierShifts_ShiftNumber",
                table: "CashierShifts",
                newName: "IX_CashierShifts_ShiftNumber");

            migrationBuilder.RenameIndex(
                name: "IX_T_ApprovalSteps_DocumentType_DocumentId_StepOrder",
                table: "ApprovalSteps",
                newName: "IX_ApprovalSteps_DocumentType_DocumentId_StepOrder");

            migrationBuilder.RenameIndex(
                name: "IX_S_StockMovements_WarehouseId",
                table: "StockMovements",
                newName: "IX_StockMovements_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_S_StockMovements_ProductVariantId_WarehouseId",
                table: "StockMovements",
                newName: "IX_StockMovements_ProductVariantId_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_S_ProductStocks_WarehouseId",
                table: "ProductStocks",
                newName: "IX_ProductStocks_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_S_ProductStocks_ProductVariantId_WarehouseId",
                table: "ProductStocks",
                newName: "IX_ProductStocks_ProductVariantId_WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_M_Warehouses_Code",
                table: "Warehouses",
                newName: "IX_Warehouses_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_Units_Code",
                table: "Units",
                newName: "IX_Units_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_Taxes_Code",
                table: "Taxes",
                newName: "IX_Taxes_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_Suppliers_Code",
                table: "Suppliers",
                newName: "IX_Suppliers_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductVariants_Sku",
                table: "ProductVariants",
                newName: "IX_ProductVariants_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductVariants_ProductId",
                table: "ProductVariants",
                newName: "IX_ProductVariants_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductVariantAttributes_ProductVariantId",
                table: "ProductVariantAttributes",
                newName: "IX_ProductVariantAttributes_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductVariantAttributes_AttributeValueId",
                table: "ProductVariantAttributes",
                newName: "IX_ProductVariantAttributes_AttributeValueId");

            migrationBuilder.RenameIndex(
                name: "IX_M_Products_TaxId",
                table: "Products",
                newName: "IX_Products_TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_M_Products_Code",
                table: "Products",
                newName: "IX_Products_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_Products_CategoryId",
                table: "Products",
                newName: "IX_Products_CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_M_Products_BrandId",
                table: "Products",
                newName: "IX_Products_BrandId");

            migrationBuilder.RenameIndex(
                name: "IX_M_Products_BaseUnitId",
                table: "Products",
                newName: "IX_Products_BaseUnitId");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductImages_ProductId",
                table: "ProductImages",
                newName: "IX_ProductImages_ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductCategories_Name",
                table: "ProductCategories",
                newName: "IX_ProductCategories_Name");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductCategories_Code",
                table: "ProductCategories",
                newName: "IX_ProductCategories_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_ProductAttributes_Code",
                table: "ProductAttributes",
                newName: "IX_ProductAttributes_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_PaymentMethods_Code",
                table: "PaymentMethods",
                newName: "IX_PaymentMethods_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_Customers_Code",
                table: "Customers",
                newName: "IX_Customers_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_Brands_Code",
                table: "Brands",
                newName: "IX_Brands_Code");

            migrationBuilder.RenameIndex(
                name: "IX_M_AttributeValues_AttributeId",
                table: "AttributeValues",
                newName: "IX_AttributeValues_AttributeId");

            migrationBuilder.RenameIndex(
                name: "IX_M_ApprovalChainSteps_DocumentType_StepOrder",
                table: "ApprovalChainSteps",
                newName: "IX_ApprovalChainSteps_DocumentType_StepOrder");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SalesOrders",
                table: "SalesOrders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SalesOrderLines",
                table: "SalesOrderLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PurchaseOrders",
                table: "PurchaseOrders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PurchaseOrderLines",
                table: "PurchaseOrderLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PosSales",
                table: "PosSales",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PosSaleLines",
                table: "PosSaleLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GoodsReceipts",
                table: "GoodsReceipts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GoodsReceiptLines",
                table: "GoodsReceiptLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeliveryOrders",
                table: "DeliveryOrders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeliveryOrderLines",
                table: "DeliveryOrderLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CashierShiftTotals",
                table: "CashierShiftTotals",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CashierShifts",
                table: "CashierShifts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApprovalSteps",
                table: "ApprovalSteps",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockMovements",
                table: "StockMovements",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductStocks",
                table: "ProductStocks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Warehouses",
                table: "Warehouses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Units",
                table: "Units",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Taxes",
                table: "Taxes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Suppliers",
                table: "Suppliers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductVariants",
                table: "ProductVariants",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductVariantAttributes",
                table: "ProductVariantAttributes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Products",
                table: "Products",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductImages",
                table: "ProductImages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductCategories",
                table: "ProductCategories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductAttributes",
                table: "ProductAttributes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PaymentMethods",
                table: "PaymentMethods",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customers",
                table: "Customers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Brands",
                table: "Brands",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AttributeValues",
                table: "AttributeValues",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApprovalChainSteps",
                table: "ApprovalChainSteps",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AttributeValues_ProductAttributes_AttributeId",
                table: "AttributeValues",
                column: "AttributeId",
                principalTable: "ProductAttributes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CashierShifts_Warehouses_WarehouseId",
                table: "CashierShifts",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashierShiftTotals_CashierShifts_CashierShiftId",
                table: "CashierShiftTotals",
                column: "CashierShiftId",
                principalTable: "CashierShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CashierShiftTotals_PaymentMethods_PaymentMethodId",
                table: "CashierShiftTotals",
                column: "PaymentMethodId",
                principalTable: "PaymentMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrders_DeliveryOrderId",
                table: "DeliveryOrderLines",
                column: "DeliveryOrderId",
                principalTable: "DeliveryOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_ProductVariants_ProductVariantId",
                table: "DeliveryOrderLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_SalesOrderLines_SalesOrderLineId",
                table: "DeliveryOrderLines",
                column: "SalesOrderLineId",
                principalTable: "SalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrders_SalesOrders_SalesOrderId",
                table: "DeliveryOrders",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId",
                table: "GoodsReceiptLines",
                column: "GoodsReceiptId",
                principalTable: "GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_ProductVariants_ProductVariantId",
                table: "GoodsReceiptLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                table: "GoodsReceiptLines",
                column: "PurchaseOrderLineId",
                principalTable: "PurchaseOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_PurchaseOrders_PurchaseOrderId",
                table: "GoodsReceipts",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSaleLines_PosSales_PosSaleId",
                table: "PosSaleLines",
                column: "PosSaleId",
                principalTable: "PosSales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSaleLines_ProductVariants_ProductVariantId",
                table: "PosSaleLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSales_CashierShifts_CashierShiftId",
                table: "PosSales",
                column: "CashierShiftId",
                principalTable: "CashierShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSales_PaymentMethods_PaymentMethodId",
                table: "PosSales",
                column: "PaymentMethodId",
                principalTable: "PaymentMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSales_Taxes_TaxId",
                table: "PosSales",
                column: "TaxId",
                principalTable: "Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSales_Warehouses_WarehouseId",
                table: "PosSales",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductImages_Products_ProductId",
                table: "ProductImages",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Brands_BrandId",
                table: "Products",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductCategories_CategoryId",
                table: "Products",
                column: "CategoryId",
                principalTable: "ProductCategories",
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

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_ProductVariants_ProductVariantId",
                table: "ProductStocks",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Warehouses_WarehouseId",
                table: "ProductStocks",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributes_AttributeValues_AttributeValueId",
                table: "ProductVariantAttributes",
                column: "AttributeValueId",
                principalTable: "AttributeValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributes_ProductVariants_ProductVariantId",
                table: "ProductVariantAttributes",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariants_Products_ProductId",
                table: "ProductVariants",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_ProductVariants_ProductVariantId",
                table: "PurchaseOrderLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_Taxes_TaxId",
                table: "PurchaseOrderLines",
                column: "TaxId",
                principalTable: "Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Suppliers_SupplierId",
                table: "PurchaseOrders",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Warehouses_WarehouseId",
                table: "PurchaseOrders",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_ProductVariants_ProductVariantId",
                table: "SalesOrderLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                table: "SalesOrderLines",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_Taxes_TaxId",
                table: "SalesOrderLines",
                column: "TaxId",
                principalTable: "Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_Customers_CustomerId",
                table: "SalesOrders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_Warehouses_WarehouseId",
                table: "SalesOrders",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_ProductVariants_ProductVariantId",
                table: "StockMovements",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Warehouses_WarehouseId",
                table: "StockMovements",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

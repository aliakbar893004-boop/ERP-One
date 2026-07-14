using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ErpOne.Application.Common;
using ErpOne.Application.Logs;
using ErpOne.Application.ProductCategories;
using ErpOne.Application.Products;
using ErpOne.Application.Brands;
using ErpOne.Application.CashBank;
using ErpOne.Application.CompanySettings;
using ErpOne.Application.Currencies;
using ErpOne.Application.Dashboard;
using ErpOne.Application.Numbering;
using ErpOne.Application.Reports;
using ErpOne.Application.Units;
using ErpOne.Application.Warehouses;
using ErpOne.Application.Taxes;
using ErpOne.Application.PaymentMethods;
using ErpOne.Application.Attributes;
using ErpOne.Application.Stock;
using ErpOne.Application.Customers;
using ErpOne.Application.Suppliers;
using ErpOne.Application.Approvals;
using ErpOne.Application.GoodsReceipts;
using ErpOne.Application.PurchaseOrders;
using ErpOne.Application.SalesOrders;
using ErpOne.Application.CustomerInvoices;
using ErpOne.Application.CustomerReceipts;
using ErpOne.Application.Expenses;
using ErpOne.Application.SupplierInvoices;
using ErpOne.Application.SupplierPayments;
using ErpOne.Application.DeliveryOrders;
using ErpOne.Application.CashierShifts;
using ErpOne.Application.PosSales;
using ErpOne.Infrastructure.Persistence;
using ErpOne.Infrastructure.Services;

namespace ErpOne.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));

        // Fallback current-user (di-override host web dengan implementasi berbasis HttpContext)
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        // Validators (FluentValidation) dari assembly Application
        services.AddValidatorsFromAssemblyContaining<CreateProductValidator>();

        // Application services
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductCategoryService, ProductCategoryService>();
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<ICurrencyService, CurrencyService>();
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        services.AddScoped<INumberSequenceService, NumberSequenceService>();
        services.AddScoped<ICompanySettingService, CompanySettingService>();
        services.AddScoped<ICashBankAccountService, CashBankAccountService>();
        services.AddScoped<ISupplierInvoiceService, SupplierInvoiceService>();
        services.AddScoped<ISupplierPaymentService, SupplierPaymentService>();
        services.AddScoped<ICustomerInvoiceService, CustomerInvoiceService>();
        services.AddScoped<ICustomerReceiptService, CustomerReceiptService>();
        services.AddScoped<IExpenseCategoryService, ExpenseCategoryService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<ITaxService, TaxService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IAttributeService, AttributeService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IReportExporter, ReportExporter>();
        services.AddScoped<IStockLedgerReportService, StockLedgerReportService>();
        services.AddScoped<IInventoryValuationReportService, InventoryValuationReportService>();
        services.AddScoped<SalesFactProvider>();
        services.AddScoped<ISalesReportService, SalesReportService>();
        services.AddScoped<IPurchaseReportService, PurchaseReportService>();
        services.AddScoped<IGrossProfitReportService, GrossProfitReportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ILogService, LogService>();
        services.AddScoped<IApprovalChainService, ApprovalChainService>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<ISalesOrderService, SalesOrderService>();
        services.AddScoped<IDeliveryOrderService, DeliveryOrderService>();
        services.AddScoped<ICashierShiftService, CashierShiftService>();
        services.AddScoped<IPosSaleService, PosSaleService>();
        services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
        services.Configure<GoodsReceiptOptions>(configuration.GetSection("GoodsReceipt"));

        return services;
    }
}

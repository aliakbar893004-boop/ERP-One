using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Common;
using ErpOne.Domain.Common;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;

namespace ErpOne.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    private readonly ICurrentUser _currentUser = currentUser ?? new NullCurrentUser();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantAttribute> ProductVariantAttributes => Set<ProductVariantAttribute>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<NumberSequenceCounter> NumberSequenceCounters => Set<NumberSequenceCounter>();
    public DbSet<CompanySetting> CompanySettings => Set<CompanySetting>();
    public DbSet<CashBankAccount> CashBankAccounts => Set<CashBankAccount>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Tax> Taxes => Set<Tax>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<DeliveryOrder> DeliveryOrders => Set<DeliveryOrder>();
    public DbSet<DeliveryOrderLine> DeliveryOrderLines => Set<DeliveryOrderLine>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<CashierShiftTotal> CashierShiftTotals => Set<CashierShiftTotal>();
    public DbSet<PosSale> PosSales => Set<PosSale>();
    public DbSet<PosSaleLine> PosSaleLines => Set<PosSaleLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
    public DbSet<CashBankMovement> CashBankMovements => Set<CashBankMovement>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations => Set<SupplierPaymentAllocation>();
    public DbSet<CustomerInvoice> CustomerInvoices => Set<CustomerInvoice>();
    public DbSet<CustomerInvoiceLine> CustomerInvoiceLines => Set<CustomerInvoiceLine>();
    public DbSet<ApprovalChainStep> ApprovalChainSteps => Set<ApprovalChainStep>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<AttributeValue> AttributeValues => Set<AttributeValue>();
    public DbSet<LogEntry> Logs => Set<LogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // skema Identity (AspNetUsers, AspNetRoles, dst)

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Code).HasMaxLength(50).IsRequired();
            e.HasIndex(p => p.Code).IsUnique();
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Description); // nvarchar(max) — deskripsi panjang (TEXT)
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            // FK ke master F0 (tanpa navigation property — pakai shadow FK)
            e.HasOne<Brand>().WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Unit>().WithMany().HasForeignKey(p => p.BaseUnitId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Tax>().WithMany().HasForeignKey(p => p.TaxId).OnDelete(DeleteBehavior.SetNull);

            e.HasMany(p => p.Images)
                .WithOne()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(Product.Images))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);

            e.HasMany(p => p.Variants)
                .WithOne()
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(Product.Variants))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ProductVariant>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Sku).HasMaxLength(60).IsRequired();
            e.HasIndex(v => v.Sku).IsUnique();
            e.Property(v => v.Barcode).HasMaxLength(50);
            e.Property(v => v.Price).HasPrecision(18, 2);
            e.Property(v => v.DiscountPrice).HasPrecision(18, 2);
            e.Property(v => v.DiscountPercent).HasPrecision(18, 2);
            e.Property(v => v.CostPrice).HasPrecision(18, 2);
            e.Property(v => v.Weight).HasPrecision(18, 3);
            e.Property(v => v.Dimensions).HasMaxLength(100);

            e.HasMany(v => v.Attributes)
                .WithOne()
                .HasForeignKey(a => a.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(ProductVariant.Attributes))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ProductVariantAttribute>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasOne<AttributeValue>().WithMany()
                .HasForeignKey(a => a.AttributeValueId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(m => m.UnitCost).HasPrecision(18, 2);
            e.Property(m => m.RefType).HasMaxLength(50);
            e.Property(m => m.Note).HasMaxLength(500);

            e.HasOne<ProductVariant>().WithMany()
                .HasForeignKey(m => m.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany()
                .HasForeignKey(m => m.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(m => new { m.ProductVariantId, m.WarehouseId });
        });

        modelBuilder.Entity<ProductStock>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.ProductVariantId, s.WarehouseId }).IsUnique();

            e.HasOne<ProductVariant>().WithMany()
                .HasForeignKey(s => s.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany()
                .HasForeignKey(s => s.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductImage>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.StoredPath).HasMaxLength(400).IsRequired();
            e.Property(i => i.OriginalFileName).HasMaxLength(260).IsRequired();
            e.Property(i => i.ContentType).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<ProductCategory>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Description).HasMaxLength(500);
            e.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Unit>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(u => u.Code).IsUnique();
            e.Property(u => u.Name).HasMaxLength(100).IsRequired();
            e.Property(u => u.Description).HasMaxLength(300);
        });

        modelBuilder.Entity<Brand>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
        });

        modelBuilder.Entity<Currency>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(3).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Symbol).HasMaxLength(6).IsRequired();

            // Base currency default (IDR). HasData butuh nilai statik.
            e.HasData(new
            {
                Id = 1,
                Code = "IDR",
                Name = "Rupiah",
                Symbol = "Rp",
                DecimalPlaces = 0,
                IsBase = true,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });

        modelBuilder.Entity<NumberSequence>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Prefix).HasMaxLength(10).IsRequired();
            e.Property(x => x.DateFormat).HasMaxLength(12);
            e.Property(x => x.Separator).HasMaxLength(3);
            e.Property(x => x.ResetPeriod).HasConversion<string>().HasMaxLength(10).IsRequired();

            var seedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            e.HasData(
                new { Id = 1, Code = "PurchaseOrder", Prefix = "PO",    DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 2, Code = "SalesOrder",    Prefix = "SO",    DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 3, Code = "GoodsReceipt",  Prefix = "GRN",   DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 4, Code = "DeliveryOrder", Prefix = "DO",    DateFormat = "yyyyMM",   Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 5, Code = "PosSale",       Prefix = "POS",   DateFormat = "yyyyMMdd", Padding = 4, ResetPeriod = ResetPeriod.Daily,   Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 6, Code = "CashierShift",  Prefix = "SHIFT", DateFormat = "yyyyMMdd", Padding = 4, ResetPeriod = ResetPeriod.Daily,   Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 7, Code = "SupplierInvoice", Prefix = "APV", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 8, Code = "SupplierPayment", Prefix = "APP", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" },
                new { Id = 9, Code = "CustomerInvoice", Prefix = "ARV", DateFormat = "yyyyMM", Padding = 4, ResetPeriod = ResetPeriod.Monthly, Separator = "-", CreatedAt = seedAt, CreatedBy = (string?)"system" }
            );
        });

        modelBuilder.Entity<NumberSequenceCounter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SequenceCode).HasMaxLength(40).IsRequired();
            e.Property(x => x.PeriodKey).HasMaxLength(12).IsRequired();
            e.HasIndex(x => new { x.SequenceCode, x.PeriodKey }).IsUnique();
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<CompanySetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CompanyName).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(400);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Email).HasMaxLength(120);
            e.Property(x => x.TaxId).HasMaxLength(40);
            e.Property(x => x.LogoUrl).HasMaxLength(400);
            e.Property(x => x.ReceiptHeader).HasMaxLength(500);
            e.Property(x => x.ReceiptFooter).HasMaxLength(500);

            // Baris tunggal default agar service selalu punya row untuk di-update.
            e.HasData(new
            {
                Id = 1,
                CompanyName = (string?)"ERP_One",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });

        modelBuilder.Entity<CashBankAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            e.Property(x => x.BankName).HasMaxLength(100);
            e.Property(x => x.AccountNumber).HasMaxLength(50);
            e.Property(x => x.AccountHolder).HasMaxLength(100);

            e.HasData(new
            {
                Id = 1,
                Code = "CASH",
                Name = "Main Cash",
                Type = CashBankType.Cash,
                Currency = "IDR",
                OpeningBalance = 0m,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });

        modelBuilder.Entity<Warehouse>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);

            // Gudang default agar fase stok (F2) punya tujuan stok awal.
            // HasData butuh nilai statik (bukan DateTime.UtcNow).
            e.HasData(new
            {
                Id = 1,
                Code = "WH-MAIN",
                Name = "Gudang Utama",
                Address = (string?)null,
                IsActive = true,
                IsDefault = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = (string?)"system"
            });
        });

        modelBuilder.Entity<Tax>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Rate).HasPrecision(5, 2);
            e.Property(x => x.Description).HasMaxLength(300);
        });

        modelBuilder.Entity<PaymentMethod>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<Supplier>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContactPerson).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxId).HasMaxLength(30);
            e.Property(x => x.DefaultCurrency).HasMaxLength(3).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(100);
            e.Property(x => x.BankAccountNumber).HasMaxLength(50);
            e.Property(x => x.BankAccountName).HasMaxLength(100);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContactPerson).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxId).HasMaxLength(30);
            e.Property(x => x.DefaultCurrency).HasMaxLength(3).IsRequired();
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PoNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.PoNumber).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);

            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(PurchaseOrder.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PurchaseOrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.TaxRateSnapshot).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineDiscount).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tax>().WithMany().HasForeignKey(x => x.TaxId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SalesOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SoNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.SoNumber).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);

            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.SalesOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(SalesOrder.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SalesOrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.TaxRateSnapshot).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineDiscount).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tax>().WithMany().HasForeignKey(x => x.TaxId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DeliveryOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DoNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.DoNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.DeliveryOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(DeliveryOrder.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<DeliveryOrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashierShift>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShiftNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.ShiftNumber).IsUnique();
            e.Property(x => x.CashierUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.CashierName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.OpeningFloat).HasPrecision(18, 2);
            e.Property(x => x.CashSalesTotal).HasPrecision(18, 2);
            e.Property(x => x.CountedCash).HasPrecision(18, 2);
            e.Property(x => x.CashVariance).HasPrecision(18, 2);
            e.Property(x => x.ClosingNote).HasMaxLength(500);

            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

            // Pengaman DB: hanya satu shift Open per gudang.
            e.HasIndex(x => x.WarehouseId).IsUnique()
                .HasFilter("[Status] = 'Open'")
                .HasDatabaseName("UX_CashierShifts_Warehouse_Open");

            e.HasMany(x => x.Totals)
                .WithOne()
                .HasForeignKey(t => t.CashierShiftId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(CashierShift.Totals))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<CashierShiftTotal>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosSale>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SaleNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.SaleNumber).IsUnique();
            e.Property(x => x.CashierUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.CashierName).HasMaxLength(256).IsRequired();
            e.Property(x => x.TaxRateSnapshot).HasPrecision(18, 2);
            e.Property(x => x.TransactionDiscount).HasPrecision(18, 2);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.Property(x => x.AmountTendered).HasPrecision(18, 2);
            e.Property(x => x.ChangeGiven).HasPrecision(18, 2);
            e.Property(x => x.CogsTotal).HasPrecision(18, 2);

            e.HasOne<CashierShift>().WithMany().HasForeignKey(x => x.CashierShiftId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tax>().WithMany().HasForeignKey(x => x.TaxId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.PosSaleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(PosSale.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PosSaleLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.VariantSku).HasMaxLength(64).IsRequired();
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(18, 2);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineDiscount).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GoodsReceipt>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.GrnNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.GrnNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.GoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(GoodsReceipt.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<GoodsReceiptLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SupplierInvoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.InvoiceNumber).IsUnique();
            e.Property(x => x.SupplierInvoiceNo).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Ignore(x => x.Outstanding);

            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.SupplierInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(SupplierInvoice.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SupplierInvoiceLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.TaxRateSnapshot).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineDiscount).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GoodsReceipt>().WithMany().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashBankMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(4).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.RefType).HasMaxLength(40).IsRequired();
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne<CashBankAccount>().WithMany().HasForeignKey(x => x.CashBankAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CashBankAccountId, x.Date });
        });

        modelBuilder.Entity<SupplierPayment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PaymentNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.PaymentNumber).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectionNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashBankAccount>().WithMany().HasForeignKey(x => x.CashBankAccountId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Allocations)
                .WithOne()
                .HasForeignKey(a => a.SupplierPaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(SupplierPayment.Allocations))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SupplierPaymentAllocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne<SupplierInvoice>().WithMany().HasForeignKey(x => x.SupplierInvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerInvoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.InvoiceNumber).IsUnique();
            e.Property(x => x.CustomerRef).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Ignore(x => x.Outstanding);

            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.CustomerInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Metadata.FindNavigation(nameof(CustomerInvoice.Lines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<CustomerInvoiceLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.TaxRateSnapshot).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineDiscount).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalChainStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
            e.Property(x => x.RoleName).HasMaxLength(256).IsRequired();
            e.HasIndex(x => new { x.DocumentType, x.StepOrder }).IsUnique();
        });

        modelBuilder.Entity<ApprovalStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
            e.Property(x => x.RoleName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ActedByUserId).HasMaxLength(450);
            e.Property(x => x.ActedByName).HasMaxLength(256);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.DocumentType, x.DocumentId, x.StepOrder });
        });

        modelBuilder.Entity<ProductAttribute>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(a => a.Code).IsUnique();
            e.Property(a => a.Name).HasMaxLength(100).IsRequired();

            e.HasMany(a => a.Values)
                .WithOne()
                .HasForeignKey(v => v.AttributeId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Metadata.FindNavigation(nameof(ProductAttribute.Values))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AttributeValue>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Code).HasMaxLength(20).IsRequired();
            e.Property(v => v.Value).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<ApplicationRole>(e =>
        {
            e.Property(r => r.Description).HasMaxLength(500);
        });

        // Tabel Serilog: dikelola oleh sink, bukan migrations EF.
        modelBuilder.Entity<LogEntry>(e =>
        {
            e.ToTable("Logs", t => t.ExcludeFromMigrations());
            e.HasKey(l => l.Id);
            e.Property(l => l.Level).HasMaxLength(128);
        });

        // Kolom audit untuk semua IAuditable
        foreach (var type in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IAuditable).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(type.ClrType).Property<string>(nameof(IAuditable.CreatedBy)).HasMaxLength(256);
            modelBuilder.Entity(type.ClrType).Property<string>(nameof(IAuditable.ModifiedBy)).HasMaxLength(256);
        }

        // Prefix nama tabel bisnis: M_ (master), T_ (transaksi), S_ (stok).
        // Nama tetap jamak (mis. Products -> M_Products). Identity (AspNet*) & Logs
        // sengaja TIDAK diberi prefix. Diterapkan di akhir agar GetTableName() sudah
        // mengembalikan nama default.
        var tablePrefixes = new Dictionary<string, string>
        {
            // Master
            [nameof(Product)] = "M_",
            [nameof(ProductVariant)] = "M_",
            [nameof(ProductVariantAttribute)] = "M_",
            [nameof(ProductImage)] = "M_",
            [nameof(ProductCategory)] = "M_",
            [nameof(ProductAttribute)] = "M_",
            [nameof(AttributeValue)] = "M_",
            [nameof(Unit)] = "M_",
            [nameof(Brand)] = "M_",
            [nameof(Currency)] = "M_",
            [nameof(NumberSequence)] = "M_",
            [nameof(NumberSequenceCounter)] = "M_",
            [nameof(CompanySetting)] = "M_",
            [nameof(CashBankAccount)] = "M_",
            [nameof(Warehouse)] = "M_",
            [nameof(Tax)] = "M_",
            [nameof(PaymentMethod)] = "M_",
            [nameof(Supplier)] = "M_",
            [nameof(Customer)] = "M_",
            [nameof(ApprovalChainStep)] = "M_",
            // Transaksi
            [nameof(PurchaseOrder)] = "T_",
            [nameof(PurchaseOrderLine)] = "T_",
            [nameof(GoodsReceipt)] = "T_",
            [nameof(GoodsReceiptLine)] = "T_",
            [nameof(SupplierInvoice)] = "T_",
            [nameof(SupplierInvoiceLine)] = "T_",
            [nameof(SupplierPayment)] = "T_",
            [nameof(SupplierPaymentAllocation)] = "T_",
            [nameof(CustomerInvoice)] = "T_",
            [nameof(CustomerInvoiceLine)] = "T_",
            [nameof(SalesOrder)] = "T_",
            [nameof(SalesOrderLine)] = "T_",
            [nameof(DeliveryOrder)] = "T_",
            [nameof(DeliveryOrderLine)] = "T_",
            [nameof(CashierShift)] = "T_",
            [nameof(CashierShiftTotal)] = "T_",
            [nameof(PosSale)] = "T_",
            [nameof(PosSaleLine)] = "T_",
            [nameof(ApprovalStep)] = "T_",
            // Stok
            [nameof(ProductStock)] = "S_",
            [nameof(StockMovement)] = "S_",
            [nameof(CashBankMovement)] = "S_",
        };

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (tablePrefixes.TryGetValue(entityType.ClrType.Name, out var prefix))
            {
                var current = entityType.GetTableName();
                if (current is not null && !current.StartsWith(prefix))
                    entityType.SetTableName(prefix + current);
            }
        }

        // Pengaman: setiap tabel bisnis WAJIB berprefix M_/T_/S_. Bila ada entity baru
        // yang belum terdaftar di 'tablePrefixes', model GAGAL dibangun dengan pesan jelas
        // (app tak start & test integrasi merah) — mencegah tabel tanpa prefix lolos diam-diam.
        // Pengecualian: tabel Identity (AspNet* / Identity*) & Serilog (Logs).
        var allowedUnprefixed = new HashSet<string> { "Logs" };
        var unclassified = modelBuilder.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => name is not null)
            .Distinct()
            .Where(name => !name!.StartsWith("M_") && !name.StartsWith("T_") && !name.StartsWith("S_")
                           && !name.StartsWith("AspNet") && !name.StartsWith("Identity")
                           && !allowedUnprefixed.Contains(name))
            .ToList();

        if (unclassified.Count > 0)
            throw new InvalidOperationException(
                $"Tabel tanpa prefix M_/T_/S_: {string.Join(", ", unclassified)}. " +
                "Daftarkan entity-nya di 'tablePrefixes' (AppDbContext), atau tambahkan ke " +
                "'allowedUnprefixed' bila memang bukan tabel bisnis.");
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAudit();
        return base.SaveChanges();
    }

    private void StampAudit()
    {
        var now = DateTime.UtcNow;
        var by = _currentUser.UserName;

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.MarkCreated(now, by);
            else if (entry.State == EntityState.Modified)
                entry.Entity.MarkModified(now, by);
        }
    }
}

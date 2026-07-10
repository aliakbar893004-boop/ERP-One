# Fase 3b-i — Customer Invoice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use `- [ ]`.

**Goal:** Record AR liabilities as Customer Invoices built from one or more approved Sales Orders (lines = SO qty × SO pricing), with real outstanding + credit-limit visibility. No receipt/void (3b-ii).

**Architecture:** Direct mirror of the **SupplierInvoice** module (3a-i), swapping GRN→SO, PO→SO, Supplier→Customer, `APV`→`ARV`, `PurchaseOrderLine`→`SalesOrderLine`, quantity source `QuantityReceived`→SO line `Quantity`. Adds AR-specific credit info. Use `src/**/SupplierInvoice*.cs`, `SupplierInvoiceService.cs`, and `Finance/ApInvoices/*.razor` as the template.

**Tech Stack:** .NET/C#, EF Core, Blazor Server, FluentValidation, xUnit.

## Global Constraints
- UI English; `.pi`/`.cf`/`.pf`. Register entities in `tablePrefixes` (`T_`). Money precision 18,2; `Status` string conversion. Numbering `ARV-{yyyyMM}-{0000}` (NumberSequence Id=9). Tests use `EnsureCreated()`. Commit per task.

---

## Task 1: Domain + config + numbering + migration

**Files:** Create `CustomerInvoiceStatus.cs`, `CustomerInvoiceLine.cs`, `CustomerInvoice.cs` in Domain; modify `DocumentTypes.cs`, `AppDbContext.cs`; migration `AddCustomerInvoice`.

- [ ] **Step 1: Entities** — copy `SupplierInvoiceStatus`/`SupplierInvoiceLine`/`SupplierInvoice` renaming Supplier→Customer, and on the line replace `GoodsReceiptId/GoodsReceiptLineId` with `SalesOrderId/SalesOrderLineId`; on the aggregate rename `SupplierInvoiceNo`→`CustomerRef`. Keep `ApplyPayment`/`ReversePayment` (needed by 3b-ii). Line `Recompute` identical.

`CustomerInvoiceStatus.cs`:
```csharp
namespace ErpOne.Domain.Entities;
public enum CustomerInvoiceStatus { Open, PartiallyPaid, Paid, Cancelled }
```

`CustomerInvoiceLine.cs`:
```csharp
namespace ErpOne.Domain.Entities;

public class CustomerInvoiceLine
{
    public int Id { get; private set; }
    public int CustomerInvoiceId { get; private set; }
    public int SalesOrderId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRateSnapshot { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTax { get; private set; }
    public decimal LineTotal { get; private set; }

    private CustomerInvoiceLine() { }

    public CustomerInvoiceLine(int salesOrderId, int salesOrderLineId, int productVariantId,
        int quantity, decimal unitPrice, decimal discountPercent, decimal taxRateSnapshot)
    {
        if (salesOrderId <= 0) throw new ArgumentException("SalesOrderId is required.", nameof(salesOrderId));
        if (salesOrderLineId <= 0) throw new ArgumentException("SalesOrderLineId is required.", nameof(salesOrderLineId));
        if (productVariantId <= 0) throw new ArgumentException("ProductVariantId is required.", nameof(productVariantId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("UnitPrice cannot be negative.", nameof(unitPrice));
        if (discountPercent is < 0 or > 100) throw new ArgumentException("DiscountPercent must be 0..100.", nameof(discountPercent));
        if (taxRateSnapshot is < 0 or > 100) throw new ArgumentException("TaxRateSnapshot must be 0..100.", nameof(taxRateSnapshot));
        SalesOrderId = salesOrderId; SalesOrderLineId = salesOrderLineId; ProductVariantId = productVariantId;
        Quantity = quantity; UnitPrice = unitPrice; DiscountPercent = discountPercent; TaxRateSnapshot = taxRateSnapshot;
        Recompute();
    }

    private void Recompute()
    {
        LineSubtotal = Round(Quantity * UnitPrice);
        LineDiscount = Round(LineSubtotal * DiscountPercent / 100m);
        LineTax = Round((LineSubtotal - LineDiscount) * TaxRateSnapshot / 100m);
        LineTotal = LineSubtotal - LineDiscount + LineTax;
    }
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
```

`CustomerInvoice.cs`: copy `SupplierInvoice.cs` verbatim, rename class→CustomerInvoice, status enum→CustomerInvoiceStatus, `SupplierId`→`CustomerId`, `SupplierInvoiceNo`→`CustomerRef`, lines type→CustomerInvoiceLine, keep `Subtotal/Discount/Tax/Grand/PaidAmount/Outstanding`, `SetLines`, `UpdateHeader(invoiceDate,dueDate,customerRef,notes)`, `Cancel`, `ApplyPayment`, `ReversePayment` exactly as SupplierInvoice.

- [ ] **Step 2: DocumentTypes** — add `public const string CustomerInvoice = "CustomerInvoice";`

- [ ] **Step 3: AppDbContext** — DbSets `CustomerInvoices`/`CustomerInvoiceLines`; config mirrors SupplierInvoice (FK `Customer`; line FK `SalesOrder` Restrict + `ProductVariant` Restrict; `e.Ignore(x=>x.Outstanding)`); `tablePrefixes` both `T_`; add NumberSequence seed row Id=9 `{ Code="CustomerInvoice", Prefix="ARV", DateFormat="yyyyMM", Padding=4, ResetPeriod=Monthly, Separator="-" }`.

- [ ] **Step 4: Build + migration** `dotnet ef migrations add AddCustomerInvoice ...`; commit.

---

## Task 2: CustomerInvoiceService + DTOs + validators + DI + tests

**Files:** `ErpOne.Application/CustomerInvoices/*` (Dtos, ISvc, Validators), `Infrastructure/Services/CustomerInvoiceService.cs`, DI, test.

- [ ] **Step 1: DTOs** — mirror SupplierInvoice DTOs. Records:
  - `CustomerInvoiceListItemDto(int Id, string InvoiceNumber, string CustomerName, DateTime InvoiceDate, DateTime DueDate, string Currency, decimal GrandTotal, decimal PaidAmount, decimal Outstanding, string Status)`
  - `CustomerInvoiceLineDto(int Id, int SalesOrderId, string SoNumber, int ProductVariantId, string Sku, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent, decimal TaxRateSnapshot, decimal LineSubtotal, decimal LineDiscount, decimal LineTax, decimal LineTotal)`
  - `CustomerInvoiceDto(... same shape as SupplierInvoiceDto with CustomerId/CustomerName/CustomerRef ...)`
  - `UninvoicedSalesOrderDto(int SalesOrderId, string SoNumber, DateTime OrderDate, decimal GrandTotal, IReadOnlyList<CustomerInvoiceLineDto> Lines)`
  - `CustomerInvoiceDashboardDto(int Total, int Open, int PartiallyPaid, int Paid, decimal TotalOutstanding)`
  - `CustomerCreditDto(decimal CreditLimit, decimal Outstanding, decimal Available)`
  - `CreateCustomerInvoiceRequest(int CustomerId, DateTime InvoiceDate, DateTime? DueDate, string? CustomerRef, string? Notes, IReadOnlyList<int> SalesOrderIds)`
  - `UpdateCustomerInvoiceHeaderRequest(DateTime InvoiceDate, DateTime DueDate, string? CustomerRef, string? Notes)`

- [ ] **Step 2: ISvc** — mirror ISupplierInvoiceService: GetPagedAsync, GetByIdAsync, GetDashboardAsync, `GetUninvoicedSalesOrdersAsync(int customerId)`, `GetCustomerCreditAsync(int customerId)`, CreateAsync, UpdateHeaderAsync, CancelAsync.

- [ ] **Step 3: Validators** — mirror (SalesOrderIds NotEmpty; CustomerRef/Notes maxlen; DueDate ≥ InvoiceDate when set).

- [ ] **Step 4: Tests** — mirror SupplierInvoiceServiceTests seed but for the sales side. Seed helper: customer + product + SO **Confirmed** (via `ISalesOrderService.CreateAsync` then `SubmitAsync` → empty chain auto-confirms). Reconcile against real `ISalesOrderService`/`CreateSalesOrderRequest`/`SalesOrderLineRequest` signatures (open `src/ErpOne.Application/SalesOrders/*`). Tests: create-from-1-SO totals+ARV number+Open; uninvoiced excludes-then-freed-on-cancel; empty-list throws; `GetCustomerCreditAsync` available = limit − outstanding.

- [ ] **Step 5: Service** — copy `SupplierInvoiceService.cs`, rename Supplier→Customer, GRN→SO, `PurchaseOrders`→`SalesOrders`, `PurchaseOrderLines`→`SalesOrderLines`, `PoNumber`→`SoNumber`, `GoodsReceipts`→`SalesOrders`, `GoodsReceiptStatus.Posted`→SO invoiceable check, `QuantityReceived`→`Quantity`, `DocumentTypes.SupplierInvoice`→`CustomerInvoice`. Key differences:
  - `GetUninvoicedSalesOrdersAsync`: SOs where `Status ∈ {Confirmed, PartiallyDelivered, Delivered, Closed}` AND `CustomerId==customerId` AND not referenced by a non-cancelled CustomerInvoiceLine. Build derived lines from `SalesOrderLines` (Quantity × pricing).
  - Line build in Create: for each SO, load its `SalesOrderLines`, one invoice line per SO line (`sol.Quantity`, `sol.UnitPrice`, `sol.DiscountPercent`, `sol.TaxRateSnapshot`).
  - Validate: SOs exist, invoiceable status, belong to customer, same currency, not already invoiced.
  - DueDate default = InvoiceDate + `customer.PaymentTermDays`; currency from SO.
  - Add `GetCustomerCreditAsync`:
    ```csharp
    public async Task<CustomerCreditDto> GetCustomerCreditAsync(int customerId, CancellationToken ct = default)
    {
        var limit = await db.Customers.Where(c => c.Id == customerId).Select(c => c.CreditLimit).FirstOrDefaultAsync(ct);
        var outstanding = await db.CustomerInvoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.Status != CustomerInvoiceStatus.Cancelled)
            .SumAsync(i => (decimal?)(i.GrandTotal - i.PaidAmount), ct) ?? 0m;
        return new CustomerCreditDto(limit, outstanding, limit - outstanding);
    }
    ```

- [ ] **Step 6: DI** — `services.AddScoped<ICustomerInvoiceService, CustomerInvoiceService>();` + using.

- [ ] **Step 7: Run tests → pass; commit.**

---

## Task 3: Web pages + menu + _Imports

**Files:** `Web/Components/Pages/Finance/ArInvoices/ArInvoiceIndex.razor`(+css), `ArInvoiceForm.razor`, `ArInvoiceDetail.razor`(+css); modify `AppMenus.cs`, `_Imports.razor`.

- [ ] **Step 1:** AppMenus Finance group: `new("finance.ar-invoices", "Customer Invoices", "bi-receipt-cutoff", CRUD),`. `_Imports`: `@using ErpOne.Application.CustomerInvoices`.
- [ ] **Step 2:** Index — copy `ApInvoiceIndex.razor`(+css), rename to AR, route `/finance/ar-invoices`, service→CustomerInvoice, "Supplier"→"Customer", status enum→CustomerInvoiceStatus, add status chips (like ApInvoice after its filter was added).
- [ ] **Step 3:** Form — copy `ApInvoiceForm.razor`(+css), rename; supplier→customer dropdown (`ICustomerService.GetAllAsync`); GRN picker → SO picker (`GetUninvoicedSalesOrdersAsync`); add a **credit panel**: on customer change also call `GetCustomerCreditAsync`, show Limit / Outstanding / Available, and a warning `@if (selectedTotal > _credit.Available)` "Exceeds available credit". Keep expandable line preview + select-all + summary.
- [ ] **Step 4:** Detail — copy `ApInvoiceDetail.razor`(+css), rename; lines show SO number instead of GRN; Cancel action same.
- [ ] **Step 5:** Build; commit.

Reconcile `ICustomerService`/`CustomerDto` (has `Id, Code, Name, CreditLimit`) and `ISalesOrderService` shapes before writing.

## Final verification
- `dotnet build && dotnet test`; `dotnet ef database update`; smoke: Confirmed SO → Customer Invoice → outstanding + credit panel; cancel frees SO.

## Self-review
- Mirrors SupplierInvoice; only source (SO), field names, credit panel differ. `ApplyPayment/ReversePayment` on entity ready for 3b-ii. NumberSequence count test will need bump to 9 (update `NumberSequenceServiceTests`).

using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ErpOne.Application.Accounting;
using ErpOne.Application.Costing;
using ErpOne.Application.Numbering;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Persistence;

namespace ErpOne.Infrastructure.Services;

public class JournalPostingService(AppDbContext db, IDocumentNumberService docNumbers,
    ICostingSettingService costingSettings) : IJournalPostingService
{
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private async Task<PostingConfiguration> ConfigAsync(CancellationToken ct) =>
        await db.PostingConfigurations.FirstOrDefaultAsync(ct)
        ?? throw Fail("Posting configuration is missing.");

    private static int RequireAccount(int? id, string label) =>
        id ?? throw Fail($"Account not mapped: {label}. Set it in Settings → Posting Configuration.");

    // Builds one balanced System journal. Idempotent by (sourceType, sourceId). Enlists in caller tx.
    private async Task PostBalancedAsync(DateTime date, string description, string sourceType, int sourceId,
        IEnumerable<(int AccountId, decimal Debit, decimal Credit, string? Memo)> lines, CancellationToken ct)
    {
        if (await db.JournalEntries.AnyAsync(x => x.SourceType == sourceType && x.SourceId == sourceId, ct))
            return; // already posted

        var filtered = lines.Where(l => l.Debit > 0m || l.Credit > 0m).ToList();
        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, date, ct);
        var je = new JournalEntry(number, date, description);
        je.SetLines(filtered.Select(l => (l.AccountId, l.Debit, l.Credit, l.Memo)));
        je.MarkSystemSource(sourceType, sourceId);
        je.Post();
        db.JournalEntries.Add(je);
        await db.SaveChangesAsync(ct);
    }

    public async Task PostGoodsReceiptAsync(GoodsReceipt grn, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var grIr = RequireAccount(cfg.GrIrAccountId, "GR-IR");
        var grValue = grn.Lines.Sum(l => Round(l.QuantityReceived * l.UnitCost)); // actual

        var method = await costingSettings.GetMethodAsync(ct);
        if (method != CostingMethod.StandardCost)
        {
            await PostBalancedAsync(grn.ReceiptDate, $"GRN {grn.GrnNumber}", "GoodsReceipt", grn.Id,
                [(inventory, grValue, 0m, "Inventory received"), (grIr, 0m, grValue, "Goods received not invoiced")], ct);
            return;
        }

        // Standard costing: inventory at standard (variant.CostPrice), GR-IR at actual, balance via PPV.
        var ppv = RequireAccount(cfg.PurchasePriceVarianceAccountId, "Purchase Price Variance");
        var variantIds = grn.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var standardByVariant = await db.ProductVariants.Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.CostPrice, ct);
        var invValue = grn.Lines.Sum(l => Round(l.QuantityReceived * standardByVariant[l.ProductVariantId]));
        var d = grValue - invValue;

        await PostBalancedAsync(grn.ReceiptDate, $"GRN {grn.GrnNumber}", "GoodsReceipt", grn.Id,
        [
            (inventory, invValue, 0m, "Inventory received @ standard"),
            (grIr, 0m, grValue, "Goods received not invoiced"),
            (ppv, Math.Max(d, 0m), Math.Max(-d, 0m), "Purchase price variance"),
        ], ct);
    }

    public async Task PostSupplierInvoiceAsync(SupplierInvoice inv, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var grIr = RequireAccount(cfg.GrIrAccountId, "GR-IR");
        var ap = RequireAccount(cfg.ApAccountId, "Accounts Payable");
        var net = inv.Subtotal - inv.DiscountTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (grIr, net, 0m, "Goods invoiced"),
            (ap, 0m, inv.GrandTotal, "Supplier payable"),
        };
        if (inv.TaxTotal > 0m)
            lines.Insert(1, (RequireAccount(cfg.InputTaxAccountId, "Input Tax"), inv.TaxTotal, 0m, "Input VAT"));
        await PostBalancedAsync(inv.InvoiceDate, $"Supplier Invoice {inv.InvoiceNumber}", "SupplierInvoice", inv.Id, lines, ct);
    }

    public async Task PostSupplierPaymentAsync(SupplierPayment pay, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var ap = RequireAccount(cfg.ApAccountId, "Accounts Payable");
        var cash = RequireAccount(await CashGlAsync(pay.CashBankAccountId, ct), "Cash/Bank");
        await PostBalancedAsync(pay.PaymentDate, $"Supplier Payment {pay.PaymentNumber}", "SupplierPayment", pay.Id,
            [(ap, pay.Amount, 0m, "Settle payable"), (cash, 0m, pay.Amount, "Cash out")], ct);
    }

    public async Task PostCustomerInvoiceAsync(CustomerInvoice inv, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var ar = RequireAccount(cfg.ArAccountId, "Accounts Receivable");
        var sales = RequireAccount(cfg.SalesAccountId, "Sales");
        var net = inv.Subtotal - inv.DiscountTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (ar, inv.GrandTotal, 0m, "Customer receivable"),
            (sales, 0m, net, "Revenue"),
        };
        if (inv.TaxTotal > 0m)
            lines.Add((RequireAccount(cfg.OutputTaxAccountId, "Output Tax"), 0m, inv.TaxTotal, "Output VAT"));
        await PostBalancedAsync(inv.InvoiceDate, $"Customer Invoice {inv.InvoiceNumber}", "CustomerInvoice", inv.Id, lines, ct);
    }

    public async Task PostDeliveryOrderAsync(DeliveryOrder dorder, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var cogs = RequireAccount(cfg.CogsAccountId, "COGS");
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var value = dorder.Lines.Sum(l => Round(l.QuantityDelivered * l.UnitCost));
        if (value <= 0m) return; // nothing to post
        await PostBalancedAsync(dorder.DeliveryDate, $"Delivery {dorder.DoNumber}", "DeliveryOrder", dorder.Id,
            [(cogs, value, 0m, "Cost of goods sold"), (inventory, 0m, value, "Inventory shipped")], ct);
    }

    public async Task PostCustomerReceiptAsync(CustomerReceipt rec, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var ar = RequireAccount(cfg.ArAccountId, "Accounts Receivable");
        var cash = RequireAccount(await CashGlAsync(rec.CashBankAccountId, ct), "Cash/Bank");
        await PostBalancedAsync(rec.ReceiptDate, $"Customer Receipt {rec.ReceiptNumber}", "CustomerReceipt", rec.Id,
            [(cash, rec.Amount, 0m, "Cash in"), (ar, 0m, rec.Amount, "Settle receivable")], ct);
    }

    public async Task PostExpenseAsync(Expense exp, CancellationToken ct = default)
    {
        var expenseAcc = RequireAccount(
            await db.ExpenseCategories.Where(c => c.Id == exp.ExpenseCategoryId).Select(c => c.GlAccountId).FirstOrDefaultAsync(ct),
            "Expense category GL account");
        var cash = RequireAccount(await CashGlAsync(exp.CashBankAccountId, ct), "Cash/Bank");
        await PostBalancedAsync(exp.ExpenseDate, $"Expense {exp.ExpenseNumber}", "Expense", exp.Id,
            [(expenseAcc, exp.Amount, 0m, exp.Description), (cash, 0m, exp.Amount, "Cash out")], ct);
    }

    public async Task PostPosSaleAsync(PosSale sale, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var posCash = RequireAccount(cfg.PosCashAccountId, "POS Cash");
        var sales = RequireAccount(cfg.SalesAccountId, "Sales");
        var cogs = RequireAccount(cfg.CogsAccountId, "COGS");
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var net = sale.GrandTotal - sale.TaxTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (posCash, sale.GrandTotal, 0m, "POS cash in"),
            (sales, 0m, net, "POS revenue"),
        };
        if (sale.TaxTotal > 0m)
            lines.Add((RequireAccount(cfg.OutputTaxAccountId, "Output Tax"), 0m, sale.TaxTotal, "Output VAT"));
        if (sale.CogsTotal > 0m)
        {
            lines.Add((cogs, sale.CogsTotal, 0m, "COGS"));
            lines.Add((inventory, 0m, sale.CogsTotal, "Inventory sold"));
        }
        await PostBalancedAsync(sale.SaleDate, $"POS {sale.SaleNumber}", "PosSale", sale.Id, lines, ct);
    }

    public async Task PostPosRefundAsync(PosRefund refund, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var posCash = RequireAccount(cfg.PosCashAccountId, "POS Cash");
        var sales = RequireAccount(cfg.SalesAccountId, "Sales");
        var cogs = RequireAccount(cfg.CogsAccountId, "COGS");
        var inventory = RequireAccount(cfg.InventoryAccountId, "Inventory");
        var net = refund.GrandTotal - refund.TaxTotal;
        var lines = new List<(int, decimal, decimal, string?)>
        {
            (sales, net, 0m, "POS refund revenue"),
            (posCash, 0m, refund.GrandTotal, "POS cash out (refund)"),
        };
        if (refund.TaxTotal > 0m)
            lines.Insert(1, (RequireAccount(cfg.OutputTaxAccountId, "Output Tax"), refund.TaxTotal, 0m, "Output VAT reversed"));
        if (refund.CogsTotal > 0m)
        {
            lines.Add((inventory, refund.CogsTotal, 0m, "Inventory returned"));
            lines.Add((cogs, 0m, refund.CogsTotal, "COGS reversed"));
        }
        await PostBalancedAsync(refund.RefundDate, $"POS Refund {refund.RefundNumber}", "PosRefund", refund.Id, lines, ct);
    }

    public async Task ReverseForAsync(string sourceType, int sourceId, DateTime date, string? note, CancellationToken ct = default)
    {
        var original = await db.JournalEntries.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SourceType == sourceType && x.SourceId == sourceId
                && x.Source == JournalSource.System && x.Status == JournalEntryStatus.Posted && x.ReversedByEntryId == null, ct);
        if (original is null) return; // nothing posted / already reversed

        var number = await docNumbers.NextAsync(DocumentTypes.JournalEntry, date, ct);
        var desc = string.IsNullOrWhiteSpace(note)
            ? $"Reversal of {original.EntryNumber}"
            : $"Reversal of {original.EntryNumber}: {note.Trim()}";
        var reversal = new JournalEntry(number, date, desc);
        reversal.SetLines(original.Lines.Select(l => (l.AccountId, l.Credit, l.Debit, l.Memo)));
        reversal.MarkSystemSource($"{sourceType}Void", sourceId);
        reversal.MarkAsReversalOf(original.Id);
        reversal.Post();
        db.JournalEntries.Add(reversal);
        await db.SaveChangesAsync(ct);

        original.MarkReversed(reversal.Id);
        await db.SaveChangesAsync(ct);
    }

    // GL account for a cash/bank account: its own mapping, else the config's default cash account.
    private async Task<int?> CashGlAsync(int cashBankAccountId, CancellationToken ct)
    {
        var gl = await db.CashBankAccounts.Where(a => a.Id == cashBankAccountId)
            .Select(a => a.GlAccountId).FirstOrDefaultAsync(ct);
        if (gl is not null) return gl;
        return await db.PostingConfigurations.Select(c => c.PosCashAccountId).FirstOrDefaultAsync(ct);
    }

    private static ValidationException Fail(string message) =>
        new([new FluentValidation.Results.ValidationFailure("Posting", message)]);
}

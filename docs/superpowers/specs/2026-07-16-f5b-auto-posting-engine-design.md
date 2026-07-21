# Fase 5b — Auto-Posting Engine (Design Spec)

**Tanggal:** 2026-07-16
**Branch kerja:** `Development`
**Prasyarat:** Fase 5a (Ledger Foundation) — `Account`, `JournalEntry`/`JournalEntryLine`, `ILedgerService`, seed COA standar. Sudah diimplementasi.
**Bagian dari:** Fase 5 (Akuntansi Inti). Urutan: 5a ✅ → **5b (spec ini)** → 5c (Financial Statements).

---

## 1. Tujuan & Ruang Lingkup

Menghasilkan **jurnal double-entry otomatis** dari transaksi operasional, sehingga General Ledger & Trial Balance (5a) terisi tanpa entri manual, dan siap jadi sumber laporan keuangan (5c).

**Titik posting (8 event + reversal):**
1. Goods Receipt (GRN) post → Inventory / GR-IR
2. Supplier Invoice create → GR-IR + PPN Masukan / AP
3. Supplier Payment post → AP / Cash-Bank
4. Customer Invoice create → AR / Sales + PPN Keluaran
5. Delivery Order post → COGS / Inventory (COGS B2B)
6. Customer Receipt create → Cash-Bank / AR
7. Expense create → Beban / Cash-Bank
8. POS sale → POS-Cash + COGS / Sales + PPN Keluaran + Inventory
9. **Void/Cancel** (Supplier Payment void, Customer Receipt void, Expense void, Supplier Invoice cancel, Customer Invoice cancel) → **reversing entry** otomatis.

### Di luar scope 5b
- Financial statements (Neraca, L/R, Arus Kas) → 5c.
- Backfill jurnal transaksi lama (sebelum 5b live) — **tidak dilakukan**; hanya transaksi baru yang menghasilkan jurnal. Histori via JE saldo awal manual (5a).
- Purchase Price Variance (selisih harga GRN vs invoice) — tak ditangani khusus (lihat §6 simplifikasi).
- POS posting per PaymentMethod — semua metode POS → satu akun kas POS (lihat §6).
- Manajemen periode/tutup buku.

---

## 2. Keputusan Desain (hasil brainstorming)

| Topik | Keputusan |
|-------|-----------|
| Pemetaan akun | **Posting Config terpusat** (akun sistemik: AR/AP/Inventory/GR-IR/Sales/COGS/PPN In/PPN Out/POS-Cash) **+ FK akun di master** (`CashBankAccount.GlAccountId`, `ExpenseCategory.GlAccountId`). |
| Mapping belum di-set | **Gagalkan** operasi dokumen (fail-hard) dengan pesan jelas → menjamin GL selalu balance & lengkap. |
| Void/cancel | **Auto reversing entry** (jurnal balik, tanggal void), konsisten pola Reverse 5a. |
| Cakupan | 8 event **termasuk POS** + Delivery Order COGS. |
| Backfill | **Tidak**. |
| JE otomatis | `Source = System`, ditautkan `SourceType`+`SourceId`; **tak bisa** diedit/hapus/reverse via UI manual (dikoreksi lewat void dokumen). |

---

## 3. Perubahan `JournalEntry` (link ke sumber)

Tambah (migration `AddJournalEntrySource`):
- `Source` → enum `JournalSource { Manual, System }` (string(20)); default `Manual` (JE manual 5a), auto-post = `System`.
- `SourceType` → string? (maks 40), mis. `"GoodsReceipt"`, `"SupplierPayment"`, `"CustomerReceiptVoid"`.
- `SourceId` → int?.

**Method domain baru** `MarkSystemSource(string sourceType, int sourceId)` (set Source=System + kedua field).

**Guard `JournalEntryService` (5a) diperketat:** `UpdateDraftAsync`/`DeleteDraftAsync`/`ReverseAsync` menolak bila `Source == System` (pesan: "System-generated entries cannot be modified manually."). Idempotensi: `IJournalPostingService` menolak/skip bila sudah ada JE dgn `(SourceType, SourceId)` yang sama.

---

## 4. `PostingConfiguration` (single-row) + FK master

### 4.1 Entity `PostingConfiguration` — tabel `M_PostingConfiguration`, Id=1
FK nullable ke `Account`:
`ArAccountId, ApAccountId, InventoryAccountId, GrIrAccountId, SalesAccountId, CogsAccountId, InputTaxAccountId, OutputTaxAccountId, PosCashAccountId`.
- Single-row pattern seperti `CompanySetting` (HasData Id=1 kosong; service load/update baris Id=1).
- Method `Update(...)` menerima semua FK.

### 4.2 FK di master
- `CashBankAccount.GlAccountId` (int?) — akun Kas/Bank GL untuk mutasi dari account tsb.
- `ExpenseCategory.GlAccountId` (int?) — akun Beban GL untuk kategori tsb.
- Keduanya thread lewat ctor/Update (param trailing opsional), DTO, form.

### 4.3 Seed default (BootstrapSeeder, setelah COA ter-seed)
Petakan ke akun COA standar 5a (lookup by Code):
`AR=1130, AP=2110, Inventory=1140, GR-IR=1160, Sales=4100, COGS=5100, InputTax=1150, OutputTax=2120, PosCash=1110`.
`CashBankAccount "CASH".GlAccountId = 1110`. `ExpenseCategory` existing → default `6900` (bila null). Idempotent (hanya isi bila `PostingConfiguration` row Id=1 masih semua-null / belum diset).

### 4.4 Halaman Settings → Posting Configuration
`.cf` form (dropdown akun postable per field). Menu resource `settings.posting-config` (`[ActIndex, ActEdit]`). Service `IPostingConfigurationService` (`GetAsync`, `UpdateAsync`).

---

## 5. `IJournalPostingService` — inti engine

Namespace `ErpOne.Application.Accounting`; impl `Infrastructure/Services/Accounting/JournalPostingService.cs`.

**Prinsip:** tiap method membangun **satu JE balanced** (`Source=System`, nomor `JV` dari `IDocumentNumberService`, tautan `SourceType`/`SourceId`), lalu `db.JournalEntries.Add(je)` **pada `AppDbContext` yang sama** (ikut transaksi caller — dokumen service sudah membuka tx). Tidak membuka tx sendiri. Melakukan `db.SaveChangesAsync(ct)` internal (bagian dari tx caller) agar JE dapat Id.

**Resolusi akun** via helper `RequireAccount(int? id, string label)` → throw `ValidationException` (pola `Fail`) bila null → rollback dokumen (fail-hard). Akun sistemik dari `PostingConfiguration`; akun kas/beban dari FK master.

**Signature (semua `CancellationToken ct = default`):**
```
Task PostGoodsReceiptAsync(GoodsReceipt grn, ct);
Task PostSupplierInvoiceAsync(SupplierInvoice inv, ct);
Task PostSupplierPaymentAsync(SupplierPayment pay, ct);
Task PostCustomerInvoiceAsync(CustomerInvoice inv, ct);
Task PostDeliveryOrderAsync(DeliveryOrder dorder, ct);
Task PostCustomerReceiptAsync(CustomerReceipt rec, ct);
Task PostExpenseAsync(Expense exp, ct);
Task PostPosSaleAsync(PosSale sale, ct);
Task ReverseForAsync(string sourceType, int sourceId, DateTime date, string note, ct);
```
`ReverseForAsync` mencari JE `System` dgn `(SourceType, SourceId)` (yang belum di-reverse), membangun jurnal balik (debit↔kredit ditukar, tanggal `date`, Source=System, SourceType=`"{sourceType}Void"`, SourceId sama), post, dan menandai JE asal reversed.

Helper `PostBalancedAsync(entryDate, description, sourceType, sourceId, IEnumerable<(int accountId, decimal debit, decimal credit, string? memo)> lines)` — bikin `JournalEntry`, `SetLines`, `MarkSystemSource`, `Post()` (validasi balance 5a), add. Idempotensi: skip bila JE `(sourceType,sourceId)` sudah ada.

## 5A. Komposisi jurnal per event

Nilai diambil dari properti entity (semua sudah ada). Net = sisi yang menyeimbangkan grand total.

| Event | Debit | Credit |
|---|---|---|
| **GRN post** | Inventory = Σ(line.Qty × line.UnitCost) | GR-IR = idem |
| **Supplier Invoice create** | GR-IR = (Subtotal − DiscountTotal); PPN Masukan = TaxTotal | AP = GrandTotal |
| **Supplier Payment post** | AP = Amount | Cash/Bank(account.GlAccountId) = Amount |
| **Customer Invoice create** | AR = GrandTotal | Sales = (Subtotal − DiscountTotal); PPN Keluaran = TaxTotal |
| **Delivery Order post** | COGS = Σ(line.Qty × line.UnitCost) | Inventory = idem |
| **Customer Receipt create** | Cash/Bank(account.GlAccountId) = Amount | AR = Amount |
| **Expense create** | Beban(category.GlAccountId) = Amount | Cash/Bank(account.GlAccountId) = Amount |
| **POS sale** | POS-Cash = GrandTotal; COGS = CogsTotal | Sales = (GrandTotal − TaxTotal); PPN Keluaran = TaxTotal; Inventory = CogsTotal |
| **Void/Cancel** | *(jurnal balik dari JE sumber: debit↔kredit ditukar)* | |

Baris dengan nilai 0 (mis. TaxTotal=0) **di-skip** agar tiap baris tetap satu-sisi > 0 (invarian JournalEntryLine 5a). Bila setelah skip hanya tersisa <2 baris atau tak balance → `Post()` akan throw (seharusnya tak terjadi karena grand total>0).

## 5B. Integrasi ke dokumen service

Inject `IJournalPostingService` ke service berikut; sisipkan panggilan **sebelum `await tx.CommitAsync(ct)`** (setelah efek stok/kas existing):

| Service.Method | Panggilan |
|---|---|
| `GoodsReceiptService.PostAsync` | `PostGoodsReceiptAsync(grn, ct)` |
| `SupplierInvoiceService.CreateAsync` | `PostSupplierInvoiceAsync(inv, ct)` |
| `SupplierInvoiceService.CancelAsync` | `ReverseForAsync("SupplierInvoice", id, today, note, ct)` |
| `SupplierPaymentService.PostAsync` (private, saat approved) | `PostSupplierPaymentAsync(payment, ct)` |
| `SupplierPaymentService.VoidAsync` | `ReverseForAsync("SupplierPayment", id, today, note, ct)` |
| `CustomerInvoiceService.CreateAsync` | `PostCustomerInvoiceAsync(inv, ct)` |
| `CustomerInvoiceService.CancelAsync` | `ReverseForAsync("CustomerInvoice", id, today, note, ct)` |
| `DeliveryOrderService.PostAsync` | `PostDeliveryOrderAsync(dorder, ct)` |
| `CustomerReceiptService.CreateAsync` | `PostCustomerReceiptAsync(rec, ct)` |
| `CustomerReceiptService.VoidAsync` | `ReverseForAsync("CustomerReceipt", id, today, note, ct)` |
| `ExpenseService.CreateAsync` | `PostExpenseAsync(exp, ct)` |
| `ExpenseService.VoidAsync` | `ReverseForAsync("Expense", id, today, note, ct)` |
| `PosSaleService.CreateSaleAsync` | `PostPosSaleAsync(sale, ct)` |

Semua service ini sudah membungkus `BeginTransactionAsync`; JE ikut commit/rollback yang sama → atomik. **Jika mapping kurang → ValidationException → seluruh operasi dibatalkan** (fail-hard, sesuai keputusan).

---

## 6. Simplifikasi (disetujui)

1. **GR-IR / Purchase Price Variance:** invoice membukukan GR-IR sebesar (Subtotal−Discount); GRN mengkredit GR-IR sebesar cost. Bila berbeda (harga invoice ≠ cost GRN), selisih mengendap di GR-IR — **tidak** dialokasikan ke PPV. Dapat disempurnakan nanti.
2. **POS satu akun kas:** semua metode bayar POS (tunai/kartu) di-debit ke satu `PosCashAccount`. Rekonsiliasi per metode = lanjutan.
3. **Pengakuan pendapatan B2B:** Revenue+AR saat Customer Invoice create; COGS saat Delivery Order post — dua event terpisah (umum di ERP; bisa timing berbeda).
4. **Void membaca kondisi saat ini** (tak ada akun "clearing" khusus) — reversing entry pakai komposisi mirror dari JE asal.

---

## 7. Testing

Integration test (pola 5a; SQLite EnsureCreated; isolasi via Id sendiri):
- Per event: setelah operasi dokumen, ada JE `Source=System` dgn `SourceType/SourceId` benar, balanced, akun sesuai tabel §5A (assert debit/credit per akun).
- Missing mapping: kosongkan satu akun di PostingConfiguration → operasi dokumen terkait **throw ValidationException** & tak ada JE tercipta (rollback).
- Void: void dokumen → reversing JE (mirror) tercipta, GL net akun = 0.
- Idempotensi: memanggil ulang tak membuat JE dobel (guard `(SourceType,SourceId)`).
- `JournalEntryService` menolak Edit/Delete/Reverse JE `System`.
- Reuse helper seed (Warehouse/Product/Supplier/Customer) dari test existing untuk menyiapkan dokumen.

Target: seluruh suite hijau; +N test auto-posting. Update assert `NumberSequenceServiceTests` bila ada sequence baru (**tidak ada** — reversing JE pakai sequence `JournalEntry` Id=12 yang sudah ada).

---

## 8. Manual Steps (setelah pull)
- Restart app + sign out/in (permission `settings.posting-config.*`).
- BootstrapSeeder otomatis seed PostingConfiguration default (menunjuk COA standar) + `CASH`.GlAccountId.
- Verifikasi di **Settings → Posting Configuration** semua akun terisi sebelum transaksi baru (kalau tidak, operasi ditolak).
- Commit/merge/push manual (identity `aliakbar893004-boop`).

---

## 9. File Impact (ringkas)

**Create:**
- `src/ErpOne.Domain/Entities/Accounting/JournalSource.cs`, `PostingConfiguration.cs`
- `src/ErpOne.Application/Accounting/IJournalPostingService.cs`, `PostingConfigurationDtos.cs`, `IPostingConfigurationService.cs`, `PostingConfigurationValidators.cs`
- `src/ErpOne.Infrastructure/Services/Accounting/JournalPostingService.cs`, `PostingConfigurationService.cs`
- Migration `AddAutoPosting` (JournalEntry Source/SourceType/SourceId; M_PostingConfiguration; CashBankAccount.GlAccountId; ExpenseCategory.GlAccountId)
- `src/ErpOne.Web/Components/Pages/Settings/PostingConfiguration/PostingConfigForm.razor`
- `tests/ErpOne.IntegrationTests/AutoPostingTests.cs`, `PostingConfigurationTests.cs`

**Modify:**
- `src/ErpOne.Domain/Entities/Accounting/JournalEntry.cs` (Source fields + `MarkSystemSource`)
- `src/ErpOne.Domain/Entities/*/CashBankAccount.cs`, `ExpenseCategory.cs` (+ GlAccountId thread)
- `src/ErpOne.Infrastructure/Persistence/AppDbContext.cs` (config + tablePrefix + HasData PostingConfiguration Id=1)
- `src/ErpOne.Infrastructure/Services/Accounting/JournalEntryService.cs` (guard System)
- 8 dokumen service (inject + panggil poster) + 5 void/cancel method
- `src/ErpOne.Infrastructure/DependencyInjection.cs` (2 service)
- `src/ErpOne.Web/Authorization/AppMenus.cs` (`settings.posting-config`)
- `src/ErpOne.Web/Infrastructure/BootstrapSeeder.cs` (seed PostingConfiguration + master GlAccountId)
- CashBank/ExpenseCategory DTO + form (input akun GL) — opsional bila mau diedit via UI; minimal seed sudah cukup untuk jalan.

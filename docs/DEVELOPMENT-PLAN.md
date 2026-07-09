# ErpOne — Rencana Pengembangan (Development Plan)

Dokumen ini merangkum modul yang **belum ada** dan menyusunnya menjadi roadmap
berprioritas. Fondasi yang sudah ada (Master data, Inventory dasar, Purchasing,
Sales, POS, Approval Chain, HPP *moving average*, Credit Limit) menjadi basis
semua fase di bawah.

> Status saat ini: alur **Pembelian → Stok → Penjualan → POS** sudah lengkap,
> plus approval & costing. Yang kurang: **Finance (AR/AP), Retur, Transfer Stok,
> Laporan, Dashboard**, dan beberapa Settings.

---

## Pola pengerjaan tiap modul (konvensi wajib diikuti)

Setiap modul baru dikerjakan lintas layer dengan pola yang sudah dipakai
(`PurchaseOrder` sebagai contoh rujukan):

1. **Domain** — entity di `ErpOne.Domain/Entities` (+ enum status bila perlu).
2. **Infrastructure/EF** — `DbSet`, `IEntityTypeConfiguration`, lalu `dotnet ef migrations add`.
3. **Application** — `I<Fitur>Service` + DTO (`<Fitur>Dtos.cs`) + FluentValidation (`<Fitur>Validators.cs`) di `ErpOne.Application/<Fitur>`.
4. **Infrastructure/Services** — implementasi `<Fitur>Service`.
5. **Web** — halaman Blazor:
   - **Index** pakai desain global `.pi` (list + KPI + chips).
   - **Form** pakai desain global `.cf` (Atlas).
   - **Detail** pakai `.pf` (card + info-grid + items table + dashboard summary).
6. **Permissions** — daftarkan resource + actions di `Authorization/AppMenus.cs`,
   seed permission di `Infrastructure/BootstrapSeeder.cs`.
7. **Approval** (jika perlu) — tambah nilai di enum `ApprovalDocumentType` & reuse `IApprovalChainService`.
8. **Stok/Costing** (jika menyentuh stok) — reuse mekanisme *stock movement* + moving-average yang ada di `IStockService`.
9. **Tests** — unit test service + integration test (pola `PurchaseOrderServiceTests`).

Prinsip: **reuse** komponen yang ada (`SwalService`, `Pager`, `.pi/.cf/.pf`,
approval chain, stock movement) — jangan bikin ulang.

---

## Ringkasan Fase & Prioritas

| Fase | Tema | Prioritas | Ketergantungan |
|------|------|-----------|----------------|
| 0 | Cleanup & fondasi | ⭐ Cepat | — |
| 1 | Inventory lengkap (Transfer, Opname, Alert) | ⭐⭐⭐ Tinggi | Fase 0 |
| 2 | Retur (Beli, Jual, POS) | ⭐⭐ Menengah | stok + costing |
| 3 | Finance — Hutang/Piutang (AP/AR) | ⭐⭐⭐ Tinggi | Fase 0 (Currency) |
| 4 | Laporan & Dashboard | ⭐⭐⭐ Tinggi | Fase 1–3 (data) |
| 5 | Akuntansi inti (COA, Jurnal, GL) | ⭐ Lanjutan | Fase 3 |
| 6 | Opsional (Quotation, Requisition, Promo, Notifikasi, Audit) | ➖ Nice-to-have | — |

Rekomendasi urutan eksekusi: **0 → 1 → 3 → 4 → 2 → 5 → 6**
(Transfer Stok = quick win berdampak; AR/AP = paling bernilai; Laporan menyusul
setelah ada datanya).

---

## Fase 0 — Cleanup & Fondasi

Cepat, memuluskan fase berikutnya.

- [ ] **Hapus sisa template**: `Pages/Counter.razor`, `Pages/Weather.razor` (dan `WeatherForecast` bila ada).
- [ ] **Company Profile / General Settings** — 1 halaman settings: nama perusahaan, alamat, logo, NPWP, header/footer struk. Dipakai POS receipt & cetakan.
  - Entity `CompanySetting` (single-row) · service `ICompanySettingService` · page `.cf`.
- [ ] **Master Currency + Kurs** — DTO PO/SO sudah menyebut `Currency`, tapi belum ada masternya.
  - Entity `Currency` (kode, simbol, is-base) · `ExchangeRate` (opsional) · `master.currencies` (CRUD).
- [ ] **Konfigurasi Penomoran Dokumen** — format & reset (harian/bulanan/tahunan) untuk PO/SO/GRN/DO/Invoice.
  - Entity `NumberSequence` · service terpusat `IDocumentNumberService` (refactor logika penomoran yang sekarang tersebar).
- [ ] **Dashboard skeleton** — ganti `Home.razor` jadi dashboard KPI (diisi bertahap di Fase 4).

**AppMenus baru:** `settings.company`, `master.currencies` (grup Master/Settings).

---

## Fase 1 — Inventory Lengkap ⭐⭐⭐

### 1a. Transfer Stok antar Gudang *(quick win — reuse stock movement)*
- Entity `StockTransfer` + `StockTransferLine`, status `Draft → InTransit → Received` (atau langsung `Posted`).
- Service: kurangi stok gudang asal, tambah stok gudang tujuan, bawa HPP (tanpa mengubah nilai — internal move).
- Pages: Index `.pi`, Form `.cf` (pilih gudang asal/tujuan + baris item mirip Stock Adjustment), Detail `.pf`.
- Permissions: `inventory.transfers` (index/create/edit/delete/post).

### 1b. Stock Opname Formal (Physical Count)
- Dokumen opname: sistem qty vs qty fisik → selisih → posting sebagai adjustment.
- Reuse mekanisme Stock Adjustment yang ada (jadikan hasil opname sumber adjustment).
- Permissions: `inventory.stock-opname`.

### 1c. Reorder Level & Alert Stok Minim
- Tambah `ReorderLevel`/`MinStock` di Product/Variant per gudang.
- Widget "stok menipis" di dashboard + halaman daftar.

---

## Fase 2 — Retur ⭐⭐

### 2a. Retur Pembelian (Purchase Return / Debit Note)
- Entity `PurchaseReturn(+Line)` merujuk GRN/PO. Kurangi stok + koreksi HPP, buat debit note ke supplier (kaitkan ke AP Fase 3).
- Permissions: `transactions.purchase-returns`.

### 2b. Retur Penjualan (Sales Return / Credit Note)
- Entity `SalesReturn(+Line)` merujuk DO/SO. Tambah stok kembali + credit note ke customer (kaitkan ke AR Fase 3).
- Permissions: `transactions.sales-returns`.

### 2c. Void / Refund di POS
- Void transaksi POS (dalam shift berjalan) + refund tunai; pengaruhi rekonsiliasi kas shift.
- Permissions: tambah action `void`/`refund` di `cashier.pos`.

---

## Fase 3 — Finance: Hutang & Piutang (AP/AR) ⭐⭐⭐

Gap terbesar. Sekarang biaya & credit limit tercatat, tapi **uang masuk/keluar
nyata** (di luar kas POS) tidak pernah dicatat.

### 3a. Hutang / Accounts Payable
- **Supplier Invoice/Bill** — dari 1+ GRN → tagihan (jatuh tempo, termin).
  - Entity `SupplierInvoice(+Line)`, status `Open → PartiallyPaid → Paid`.
- **Supplier Payment** — pelunasan (tunai/bank), alokasi ke beberapa invoice.
  - Entity `SupplierPayment(+Allocation)`.
- Permissions: `finance.ap-invoices`, `finance.ap-payments`.

### 3b. Piutang / Accounts Receivable
- **Customer Invoice** — dari SO/DO → tagihan; menyelesaikan angka *outstanding* yang sekarang cuma "diperkirakan".
  - Entity `CustomerInvoice(+Line)`.
- **Customer Receipt** — penerimaan pembayaran + alokasi ke invoice → memutakhirkan credit limit terpakai.
  - Entity `CustomerReceipt(+Allocation)`.
- Permissions: `finance.ar-invoices`, `finance.ar-receipts`.

### 3c. Kas & Bank + Biaya
- Master `CashBankAccount`; mutasi kas/bank dari pembayaran & penerimaan.
- **Expense** (biaya operasional) sederhana.
- Permissions: `finance.cashbank`, `finance.expenses`.

**Grup AppMenus baru: `Finance`.**

---

## Fase 4 — Laporan & Dashboard ⭐⭐⭐

Belum ada modul Reports sama sekali. Buat grup **Reports** + isi dashboard.

- [ ] **Kartu Stok / Stock Ledger** (mutasi per produk per gudang)
- [ ] **Nilai Persediaan / Inventory Valuation** (qty × HPP)
- [ ] **Laporan Penjualan** (per periode/produk/customer/kasir)
- [ ] **Laporan Pembelian** (per periode/supplier)
- [ ] **Laba Kotor** (penjualan − HPP)
- [ ] **Aging Piutang & Hutang** (AR/AP)
- [ ] **Laporan Shift Kasir** (rekap per shift/kasir/metode)
- [ ] **Dashboard KPI**: omzet hari ini, transaksi, stok menipis, PO/SO pending approval, hutang/piutang jatuh tempo.
- Teknis: layanan query read-only + export (CSV/PDF); UI reuse pola dashboard `.cr-kpis`/`.cr-hero` yang sudah dibuat.
- Permissions: grup `reports.*` (view + export).

---

## Fase 5 — Akuntansi Inti (opsional/lanjutan) ⭐

Untuk yang butuh pembukuan penuh.

- **Chart of Accounts (COA)**
- **Jurnal & General Ledger** (otomatis dari transaksi: GRN, invoice, payment, dll.)
- **Laporan Keuangan**: Neraca, Laba/Rugi, Arus Kas.
- Permissions: grup `accounting.*`.

> Catatan: bisa ditunda; AP/AR + laporan (Fase 3–4) sudah cukup untuk kebanyakan
> kebutuhan operasional retail/distribusi.

---

## Fase 6 — Opsional / Nice-to-have

- **Quotation** (penawaran → SO) & **Purchase Requisition** (permintaan → PO).
- **Price List / Promo / Diskon** terpusat (POS sekarang diskon manual per baris).
- **Notifikasi** in-app: approval menunggu, stok menipis, jatuh tempo.
- **Audit / Activity Log** (jejak aksi user — beda dari Error Log yang teknis).
- **Batch / Expiry / Serial number** (jika produk butuh).
- **Backup & data tools**.

---

## Catatan Cross-cutting

- **Design consistency**: semua halaman baru wajib `.pi`/`.cf`/`.pf` + token Atlas (emerald). Jangan pakai `sh-header`/`fs-card`/`data-card` lama.
- **Bahasa**: seluruh UI Bahasa Inggris (konsisten dengan modul yang sudah dirapikan).
- **Approval**: modul bernilai uang (Invoice, Payment, Return) pertimbangkan lewat Approval Chain.
- **Permission seeding**: setiap resource baru harus otomatis ter-seed agar role admin bisa akses.
- **Migrations**: setiap entity baru = satu migration; jangan lupa update `docs/` bila skema besar.

---

## Saran langkah pertama

Mulai dari **Fase 0 (cleanup + Company/Currency/Numbering)** lalu **Fase 1a
(Transfer Stok)** sebagai bukti pola end-to-end, sebelum masuk **Fase 3 (AR/AP)**
yang paling berdampak. Untuk tiap modul yang dipilih, dibuatkan *detailed
implementation plan* tersendiri sebelum coding.

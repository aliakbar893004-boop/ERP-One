# Discount % pada Varian Produk + Tampilan Diskon di Kasir — Design

**Tanggal:** 2026-07-03
**Konteks:** Master Produk (`ProductVariant`) + Layar Kasir POS (D2, `PosRegister`).

## Tujuan
Menambah **Discount %** pada varian produk yang **disimpan di DB**, dengan kalkulasi dua-arah terhadap **Discount Price** di form, memformat field harga dengan pemisah ribuan, dan menampilkan info diskon (persentase / harga discount) di layar Kasir.

## Model diskon (inti)
- `ProductVariant` mendapat field baru **`DiscountPercent` (`decimal?`, 0–100)** — disimpan di DB.
- `DiscountPrice` (`decimal?`) tetap ada & disimpan seperti sekarang.
- **Harga jual efektif tetap `DiscountPrice ?? Price`** — TIDAK berubah. `DiscountPercent` adalah metadata tampilan yang menandai "diskon berbasis persen".
- Aturan tampil di Kasir:
  - **`DiscountPercent` tidak null** → tampilkan **badge %** + **harga discount** (harga asli dicoret).
  - **`DiscountPercent` null** → diskon diambil dari **`DiscountPrice`** (tampilkan harga discount, tanpa badge %). Bila `DiscountPrice` juga null → tanpa diskon.

Konsistensi: saat `DiscountPercent` diisi di form, `DiscountPrice` di-set = `round(Price × (1 − %/100), 2)`. Jadi harga efektif tetap konsisten lewat `DiscountPrice ?? Price` untuk kedua kasus.

## Komponen

### 1. Domain (`ProductVariant`)
- Tambah prop `DiscountPercent { get; private set; }` (`decimal?`).
- `ctor` & `Update(...)` terima `decimal? discountPercent`; simpan via helper `SetDiscountPercent` (validasi 0–100 bila non-null; null diperbolehkan).
- Tidak ada perubahan logika harga (efektif tetap `DiscountPrice ?? Price`).

### 2. Persistence
- Mapping `DiscountPercent` `HasPrecision(18, 2)` (nullable).
- Migration `AddVariantDiscountPercent` (kolom nullable → aman untuk baris lama).

### 3. Application
- `ProductVariantDto` + `decimal? DiscountPercent`.
- `VariantInput` + `decimal? DiscountPercent`.
- `CreateProductValidator` / update validator: `DiscountPercent` `InclusiveBetween(0,100)` `.When(has value)`.
- `PosProductOptionDto` tambah `decimal Price` (harga asli) + `decimal? DiscountPercent`. `UnitPrice` tetap = harga efektif (`DiscountPrice ?? Price`).
- **Import CSV: DI LUAR SCOPE.** Bila import memetakan ke `VariantInput`, cukup teruskan `DiscountPercent: null` (backward-compatible, tanpa kolom CSV baru).

### 4. Web — `ProductForm`
Mode **single item** dan **tabel kombinasi varian**:
- Tambah input **Discount %** (samping Discount Price).
- Perilaku:
  - Ketik **%** → hitung & isi **Discount Price** = `round(Price × (1 − %/100), 2)`.
  - Ketik **Discount Price** langsung → set **% = null** (mode diskon-harga).
  - Ubah **Price** saat % terisi → hitung ulang Discount Price dari %.
  - Saat load (edit) → isi % dari `DiscountPercent` tersimpan.
- Format ribuan (mis. `100.000`) untuk **Selling Price, Discount Price, Cost Price** di mode single & tabel kombinasi (input teks numerik: tampil `N0`, ambil digit saja saat mengetik — pola sama dgn field "Uang Diterima" di PosRegister).

### 5. Web — Kasir (`PosRegister`)
- **Hasil pencarian (dropdown)** & **baris keranjang**: bila ada diskon (produk), tampilkan **harga asli dicoret** + **harga discount**; **badge %** bila `DiscountPercent` terisi, kalau kosong cukup harga discount.
- Harga yang masuk keranjang & transaksi = harga efektif (perilaku lama). Diskon per-baris manual di keranjang tetap terpisah dan tidak berubah.

## Di luar scope
- Struk cetak & `PosSaleDetail` tetap menampilkan harga yang benar-benar ditagih (tidak menampilkan diskon master produk) — dokumen historis mencerminkan transaksi nyata.
- Tidak mengubah perhitungan pajak/COGS/shift.

## Testing
- **Unit (Domain):** `DiscountPercent` valid 0–100; null diperbolehkan; `Update` menyetel nilai.
- **Unit (Validator):** `DiscountPercent` di luar 0–100 → invalid; null → valid.
- **Integrasi:** `SearchProductsAsync` mengembalikan `Price` (asli) + `DiscountPercent` + `UnitPrice` efektif yang benar untuk (a) varian dgn %, (b) varian dgn discount price saja, (c) tanpa diskon.
- Baseline suite tetap hijau (209).

## Catatan implementasi
- **JANGAN `--no-build` untuk `dotnet ef`**; pastikan dev app mati sebelum build/test.
- Enum/uang: `decimal(18,2)`, `Math.Round(v,2,MidpointRounding.AwayFromZero)`.

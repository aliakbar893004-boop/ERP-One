# ERP_One — Rancangan Toko Online Publik (Storefront) Terintegrasi

**Tanggal:** 2026-07-15
**Status:** Rancangan awal (belum disetujui untuk implementasi)
**Keputusan terkunci (dari diskusi):**

1. **Model bisnis:** B2C publik (retail) — katalog publik, pelanggan self-register, harga retail tunggal, guest checkout.
2. **Arsitektur:** direkomendasikan di sini (lihat §2).
3. **Pembayaran:** manual dulu (transfer/COD) dengan abstraksi `IPaymentProvider`; gateway (Midtrans/Xendit) di fase berikutnya.
4. **Integrasi:** order online → **Sales Order** ERP (alur SO/DO/Invoice yang sudah ada); stok tampil dari `ProductStock`.

> **Ringkasan kerumitan (jawaban "seberapa besar & rumit"):** ini **proyek menengah-besar**, bukan satu modul. Estimasi v1 ≈ **15–18 minggu-dev** (~3,5–4,5 bulan solo, atau ~2–2,5 bulan dengan 2 dev), di luar desain visual & QA. Rincian di §9–§10. Pendorong kerumitan utama: **keamanan aplikasi publik (isolasi dari ERP internal)**, **dua domain identitas (staf vs pembeli)**, **konsistensi stok/harga lintas channel**, dan **operasional hosting publik (domain, TLS, gambar/CDN)**.

---

## 1. Tujuan & prinsip

Menambah situs belanja publik (`shop.domain`) yang:
- Menampilkan katalog produk ERP_One ke publik, mendukung pencarian, keranjang, checkout, dan pelacakan pesanan.
- **Reuse** logika bisnis ERP yang sudah ada (produk, stok, Sales Order, Customer, penomoran dokumen) — **tanpa** menduplikasi.
- Terisolasi secara keamanan dari aplikasi admin ERP internal.
- Dirancang bertahap: rilis cepat (manual pay) lalu berkembang (gateway, kurir, promo).

Prinsip: **satu sumber kebenaran = ERP**. Storefront adalah *channel* penjualan tambahan yang menaruh order sebagai Sales Order; semua proses lanjutan (approval, Delivery Order, Invoice, HPP, stok) tetap di ERP.

---

## 2. Rekomendasi arsitektur

**Rekomendasi: project storefront baru di dalam solusi yang sama (`ErpOne.Storefront`), Blazor dengan Static Server-Side Rendering (SSR) + island interaktif seukuran perlu, berbagi layer `Domain`/`Application`/`Infrastructure`, tapi di-deploy sebagai host terpisah.**

```
ErpOne.slnx
 ├─ ErpOne.Domain            (entity — dipakai bersama)
 ├─ ErpOne.Application       (service + DTO — dipakai bersama; +Storefront/*)
 ├─ ErpOne.Infrastructure    (EF, service impl — dipakai bersama; +Storefront services)
 ├─ ErpOne.Web               (ADMIN internal — sudah ada, TIDAK diekspos publik)
 └─ ErpOne.Storefront        (PUBLIK — BARU: katalog, cart, checkout, akun pembeli)
         └─ deploy terpisah di shop.domain (host/proses sendiri)
```

**Kenapa begini (trade-off):**

| Aspek | Blazor SSR in-solusi (rekomendasi) | API terpisah + Next.js/React |
|------|-----------------------------------|------------------------------|
| Reuse logika ERP | Maksimal (share Application/Infra langsung) | Perlu bangun REST API + DTO baru |
| SEO & performa publik | Baik (SSR, halaman cacheable, tanpa SignalR circuit per pengunjung) | Sangat baik, tapi 2 codebase |
| Skala trafik publik | Baik (SSR stateless; hindari Blazor **Server** circuit) | Baik |
| Kebutuhan tim | 1 stack (.NET) — cocok tim sekarang | Perlu skill JS/React + .NET |
| Kecepatan rilis | Lebih cepat | Lebih lambat (dua sisi + auth token) |
| Isolasi keamanan | Host terpisah, DB sama, hanya endpoint storefront | Natural (API sbagai gerbang) |

> **Penting:** JANGAN pakai Blazor **Server (InteractiveServer)** sebagai render mode utama storefront publik — tiap pengunjung memegang SignalR *circuit* (boros memori & rentan untuk trafik publik + SEO lemah). Gunakan **Static SSR** untuk katalog (cacheable, ramah SEO), dan **island interaktif** (InteractiveServer/WebAssembly per-komponen) hanya untuk cart & checkout.

**Kapan pilih API + Next.js:** bila ke depan ada tim frontend khusus, butuh aplikasi mobile yang berbagi API, atau kebutuhan marketing/SEO/animasi sangat berat. Bisa ditambah belakangan (API dibangun di atas Application layer yang sama).

**Basis data:** satu DB (`MyApp`). Storefront **membaca** katalog/stok dari tabel ERP, dan **menulis** ke tabel baru khusus storefront (Cart, WebOrder, akun pembeli) + membuat `SalesOrder` lewat service ERP. Pertimbangkan user DB terpisah untuk storefront dengan hak akses lebih sempit (defense-in-depth).

---

## 3. Cakupan fitur v1 (MVP)

**Katalog & belanja (publik):**
- Beranda: banner, kategori, produk unggulan/terbaru.
- Daftar produk per kategori + pencarian + filter (kategori, brand, harga) + urut.
- Halaman detail produk: galeri gambar, deskripsi, varian (SKU/atribut), harga, status stok ("Tersedia / Menipis / Habis"), tombol tambah ke keranjang.
- Keranjang: ubah qty, hapus, subtotal.
- Checkout: alamat kirim, metode kirim (flat/ambil di toko untuk v1), ringkasan, pilih pembayaran (transfer/COD), buat pesanan.

**Akun pembeli (self-service):**
- Register (email + password), verifikasi email, login, lupa/ubah password.
- Profil + buku alamat.
- "Pesanan saya": daftar + status + detail + instruksi pembayaran.

**Pembayaran (v1 manual):**
- Transfer bank (tampilkan rekening + unggah/konfirmasi bukti) & COD.
- Abstraksi `IPaymentProvider` agar gateway bisa dipasang tanpa bongkar ulang.

**Sisi admin (tambahan kecil di ERP_One):**
- Toggle "Terbit ke Web" + field web (slug, deskripsi web, galeri) per produk.
- Antrean "Pesanan Web": lihat, konfirmasi pembayaran, dorong ke proses SO.
- Setelan storefront (banner, ongkir flat, rekening pembayaran, produk unggulan).

**Non-fungsional v1:** responsif (mobile-first), SEO dasar (slug, meta, sitemap), caching katalog, optimasi gambar (resize/thumbnail), Bahasa Indonesia, HTTPS, rate-limiting dasar, email transaksional (konfirmasi order/pembayaran).

**DI LUAR v1 (fase berikutnya):** payment gateway, integrasi ongkir kurir (RajaOngkir/Biteship), promo/voucher, ulasan produk, wishlist, harga/termin per-customer (B2B), multi-bahasa penuh, retur online.

---

## 4. Model data baru

Storefront memperkenalkan *bounded context* sendiri (jangan cemari entity ERP inti). Entity baru (nama indikatif):

**Identitas pembeli (terpisah dari staf `ApplicationUser`):**
- `ShopperAccount` — identitas login pembeli (email, hash password via ASP.NET Identity instance kedua / skema terpisah), `CustomerId` (FK ke `Customer` ERP), status verifikasi email.
  - Saat register → buat `Customer` ERP baru (Code auto via `IDocumentNumberService`, `PaymentTermDays=0`, `CreditLimit=0`, tandai `IsWebCustomer=true`) agar AR/history konsisten.

**Belanja:**
- `Cart` (ShopperAccountId **atau** anonymousKey untuk guest) + `CartItem` (ProductVariantId, Qty, harga snapshot).
- `WebOrder` — header pesanan web: nomor (via `IDocumentNumberService`, prefix mis. `WEB`), ShopperAccountId, CustomerId, **SalesOrderId** (FK ke SO ERP yang dibuat), alamat kirim (snapshot), metode & biaya kirim, `PaymentMethodType` (Transfer/COD/…), `PaymentStatus` (AwaitingPayment/Paid/Failed/Refunded), `FulfillmentStatus` (Baru/Diproses/Dikirim/Selesai/Batal), total.
- `WebOrderPayment` — catatan pembayaran (bukti transfer / referensi gateway kelak).
- `ShippingAddress` — buku alamat pembeli (bisa banyak per akun).

**Katalog web (metadata):**
- Opsi ringan: tambah field ke `Product`/`ProductVariant` (`PublishedToWeb bool`, `WebSlug`, `WebDescription`) — 1 migration.
- Opsi rapi: tabel `ProductWeb` (1-1 ke Product) untuk data khusus web (slug, deskripsi panjang, urutan galeri, SEO title/desc). **Rekomendasi**: `ProductWeb` agar tidak menggemukkan entity inti.

**Setelan:**
- `StorefrontSetting` (single-row): nama toko, banner, ongkir flat, rekening pembayaran, produk unggulan, kebijakan.

Semua entity baru = migration EF (ikut pola ERP). Stok **tidak** diduplikasi — dibaca dari `ProductStock`.

---

## 5. Integrasi ke ERP (pemetaan konkret)

| Kebutuhan storefront | Sumber/target di ERP (sudah ada) |
|---|---|
| Katalog produk & varian | `Product`, `ProductVariant` (`Sku`, `Price`, `DiscountPrice`, `IsActive`), `ProductCategory`, `Brand`, `ProductImage` — filter `PublishedToWeb` |
| Harga retail | `ProductVariant.Price` (pakai `DiscountPrice` bila ada). v1 tanpa harga per-customer |
| Ketersediaan stok | `ProductStock` (agregat lintas gudang **atau** satu "gudang web" khusus). v1 tampil-saja (lihat catatan oversell) |
| Buat pesanan | **`ISalesOrderService.CreateAsync(...)`** → `SalesOrder(+lines)` (varian, qty, harga). Pilih **gudang web default**. Lalu proses via alur SO existing (approval → `DeliveryOrder` → `CustomerInvoice`) |
| Pelanggan | `Customer` (dibuat saat register; `IsWebCustomer`). AR/piutang & receipt reuse Finance ERP |
| Penomoran | `IDocumentNumberService` + `NumberSequence` baru (mis. `WEB` untuk WebOrder) |
| Pembayaran tercatat | v1: konfirmasi manual → tandai `WebOrder.PaymentStatus=Paid`. Opsional buat `CustomerReceipt`/`CustomerInvoice` ERP saat lunas |
| Izin admin | Resource baru `AppMenus`: `web.catalog`, `web.orders`, `web.settings` (+seed via `BootstrapSeeder`) |

**Alur order (v1, ringkas):**
```
Pengunjung → katalog → cart → checkout (alamat + kirim + bayar manual)
   → buat WebOrder (AwaitingPayment) + buat SalesOrder ERP (status awal, mis. Draft/PendingApproval)
   → pembeli transfer + unggah bukti
   → admin konfirmasi (WebOrder.Paid) → SO diproses (approve → DO → invoice) di ERP seperti biasa
   → status dorong balik ke WebOrder (Diproses/Dikirim/Selesai) → pembeli lihat di "Pesanan saya"
```

**Catatan oversell (v1):** karena integrasi = "order→SO" (bukan reservasi real-time), stok bisa terjual berlebih bila stok tipis & trafik ramai. Mitigasi v1: tampilkan stok mendekati real-time + validasi ulang saat checkout + admin bisa batalkan. Reservasi stok real-time = peningkatan fase berikutnya (lihat §9).

---

## 6. Pembayaran (abstraksi siap-gateway)

```csharp
public interface IPaymentProvider {
    string Key { get; }                        // "manual-transfer", "cod", nanti "midtrans"
    Task<PaymentInitResult> InitAsync(WebOrder order, ...);   // v1: kembalikan instruksi transfer
    Task<PaymentStatus> HandleCallbackAsync(...);             // v2: webhook gateway
}
```
- v1: `ManualTransferProvider` (tampilkan rekening + terima bukti), `CodProvider`.
- v2: `MidtransProvider`/`XenditProvider` + endpoint webhook (verifikasi signature) → otomatis set `Paid` → picu proses SO. Karena abstraksi sudah ada, penambahan gateway **tidak** membongkar checkout.

---

## 7. Keamanan & isolasi (pendorong kerumitan #1)

- **Host terpisah**: `ErpOne.Storefront` proses/situs sendiri (`shop.domain`); aplikasi admin ERP **tidak** dapat diakses dari internet publik yang sama.
- **Dua domain identitas**: pembeli (`ShopperAccount`) benar-benar terpisah dari staf (`ApplicationUser`) — storefront tak pernah memuat permission/menu admin.
- **Least privilege DB**: idealnya user DB storefront hanya SELECT katalog/stok + INSERT/UPDATE tabel storefront + eksekusi via service order; tidak akses tabel sensitif (HPP/CostPrice **jangan** diekspos ke publik!).
- **Hardening publik**: HTTPS wajib, rate-limiting (login/checkout), proteksi bruteforce, anti-bot pada register, validasi input, CSRF/antiforgery, header keamanan (CSP), enkripsi secret (rekening/gateway keys via secret store).
- **Data pribadi**: alamat/email pembeli — perhatikan privasi & kebijakan.
- **Review keamanan** wajib sebelum go-live.

---

## 8. Non-fungsional

- **SEO**: SSR + slug ramah URL (`/produk/nama-produk`), meta/OpenGraph, sitemap.xml, robots.txt.
- **Performa**: cache katalog (output/response cache), paginasi, lazy-load gambar.
- **Gambar**: pipeline resize/thumbnail + (opsional) CDN; reuse `ProductImage` yang ada.
- **Mobile-first responsif** + tema visual storefront (beda dari admin — butuh desain UI tersendiri; mockup terpisah seperti yang login).
- **Email transaksional** (SMTP/service): konfirmasi order, instruksi bayar, status kirim.
- **Observability**: log error (reuse `ILogService`?), monitoring uptime.
- **Backup & DR** untuk data pesanan/pelanggan.

---

## 9. Fase pengerjaan (roadmap bertahap)

**Fase S0 — Fondasi (enabler):** project `ErpOne.Storefront` (SSR) di solusi, wiring DI/DB, deploy skeleton terisolasi, tema/layout dasar, health check. *Deliverable: situs kosong ter-deploy aman.*

**Fase S1 — Katalog publik (read-only):** `ProductWeb` + `PublishedToWeb`, layanan katalog (list/detail/search/filter), halaman katalog+detail, gambar, SEO dasar, caching. Admin: toggle terbit + field web. *Deliverable: orang bisa menjelajah produk (belum bisa beli).*

**Fase S2 — Akun pembeli:** `ShopperAccount` identity, register/verify/login/reset, profil + buku alamat, buat `Customer` ERP saat register. *Deliverable: pembeli punya akun.*

**Fase S3 — Cart & checkout → SO:** Cart/CartItem, halaman keranjang, checkout (alamat+kirim flat+bayar manual), buat `WebOrder` + `SalesOrder` ERP, validasi stok saat checkout, email konfirmasi. *Deliverable: bisa memesan; order masuk ERP.*

**Fase S4 — Pembayaran manual + pesanan saya + admin:** instruksi transfer/COD, unggah bukti, "Pesanan saya" + status, antrean "Pesanan Web" admin (konfirmasi bayar → proses SO), setelan storefront. *Deliverable: siklus order-bayar-proses lengkap (manual).* → **kandidat GO-LIVE v1.**

**Fase S5 — Pengerasan & rilis:** keamanan (rate-limit, CSP, secret), performa/caching, tes integrasi, review keamanan, pipeline deploy, domain+TLS. *Deliverable: layak produksi.*

**Fase berikutnya (v2+):** payment gateway (Midtrans/Xendit + webhook); reservasi stok real-time (anti-oversell); ongkir kurir (RajaOngkir/Biteship); promo/voucher; ulasan & rating; wishlist; retur online; harga/termin per-customer (B2B/hybrid); aplikasi mobile (butuh API layer).

---

## 10. Estimasi ukuran & kerumitan

Estimasi kasar **untuk 1 developer .NET berpengalaman** yang paham codebase ini (satuan: minggu-dev). "Kerumitan" 1–5.

| Area (v1) | Ukuran | Kerumitan | ~Minggu-dev |
|---|---|---|---|
| S0 Fondasi project + deploy terisolasi | M | 3 | 1,0 |
| S1 Katalog + `ProductWeb` + SEO + cache + admin publish | L | 3 | 2,5 |
| S2 Identitas & akun pembeli (+link Customer) | M–L | 4 | 1,5 |
| S3 Cart + checkout + buat SalesOrder + email | L | 4 | 2,5 |
| S4 Pembayaran manual + Pesanan saya + admin orders/setelan | L | 3 | 2,5 |
| Desain UI/tema storefront (responsif) | M–L | 3 | 2,0 |
| S5 Keamanan + performa + tes + review + deploy | L | 4 | 2,5 |
| Manajemen/integrasi/buffer | — | — | 1,5 |
| **Total v1** | | | **≈ 16 minggu-dev** |

- **Solo:** ~**3,5–4,5 bulan** (termasuk QA & bolak-balik desain).
- **2 developer:** ~**2–2,5 bulan** (paralelkan katalog vs akun/checkout).
- **Fase v2 (gateway + kurir + promo + reservasi stok):** tambahan **~8–12 minggu-dev** lagi.

**Verdict:** **Menengah-besar.** Bukan "tambah satu modul" seperti fase ERP sebelumnya (yang 1–3 hari). Ini aplikasi ke-2 dengan permukaan publik. Yang membuatnya berat **bukan** fitur belanjanya (itu pola CRUD+alur yang sudah kamu kuasai), melainkan: **(a) keamanan & operasional aplikasi publik**, **(b) dua domain identitas**, **(c) konsistensi data lintas channel (stok/harga/order)**, dan **(d) hosting/domain/gambar/email**.

---

## 11. Risiko & keputusan terbuka

- **Oversell stok** (v1 tanpa reservasi) — terima risiko + validasi checkout, atau naikkan ke reservasi (menambah S3+).
- **Gudang fulfilment web**: satu gudang khusus vs agregat semua — perlu ditentukan (mempengaruhi tampilan stok & SO).
- **Approval SO untuk order web**: apakah order web auto-confirm (lewati approval chain) atau tetap masuk approval? (mempengaruhi kecepatan proses).
- **Ekspos data**: pastikan `CostPrice`/HPP & data internal TIDAK bocor ke API/halaman publik.
- **Email/domain/hosting**: butuh keputusan infra (SMTP provider, domain `shop.*`, sertifikat, storage gambar/CDN).
- **Desain visual storefront**: perlu arah desain tersendiri (seperti proses mockup login) — belum termasuk detail di sini.
- **Pajak & ongkir**: aturan pajak jual & ongkir flat vs kurir — v1 disederhanakan.

---

## 12. Rekomendasi langkah

1. **Setujui arsitektur** (§2) & cakupan v1 (§3).
2. Jawab keputusan terbuka penting: **gudang web**, **approval order web**, **infra (domain/email/gambar)**.
3. Jika lanjut, pecah **Fase S0–S1** jadi *spec + implementation plan* tersendiri (pola `docs/superpowers/`) dan mulai dari fondasi + katalog (nilai terlihat paling cepat, risiko rendah).
4. Siapkan desain visual storefront (mockup) sebelum S1 UI.

> Catatan: dokumen ini rancangan tingkat tinggi. Tiap fase (S0–S5) nanti dibuatkan spec + plan detail seperti modul ERP lain sebelum coding.

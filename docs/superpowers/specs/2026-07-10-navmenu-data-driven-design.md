# NavMenu Data-Driven Refactor — Design

**Tanggal:** 2026-07-10
**Status:** Disetujui
**Ruang lingkup:** Ubah `NavMenu.razor` dari daftar link hardcoded menjadi data-driven dari `AppMenus.Groups`, sehingga resource baru cukup didaftarkan di `AppMenus.cs`.

## Masalah
`NavMenu.razor` menuliskan tiap link menu manual di dalam `<AuthorizeView>`. Menambah resource butuh edit **dua** tempat (`AppMenus.cs` untuk permission + `NavMenu.razor` untuk tampilan) — mudah lupa yang kedua (terjadi pada Fase 0: Currency/Company/Document Numbering tidak muncul).

## Pendekatan: Konvensi + Overrides
NavMenu render dari `AppMenus.Groups`. Info UI yang tidak ada di `AppMenus` (route, aksi-gate, mode match, link ganda) diturunkan by-convention, dengan tabel override kecil untuk kasus tak beraturan.

## Model
```csharp
public record NavItem(string Label, string Icon, string Href, string Policy, bool MatchAll);
public record NavSection(string? Label, IReadOnlyList<NavItem> Items);
```
Builder statik `NavMenuBuilder.Build() : IReadOnlyList<NavSection>` di `src/ErpOne.Web/Authorization/` (dekat `AppMenus`).

## Konvensi (21 item)
- **Href** = `resource.Key.Replace('.', '/')`.
- **Gate policy** = `{resource.Key}.index`.
- **Ikon** = `resource.Icon` tanpa sufiks `-fill` (mempertahankan tampilan non-fill saat ini).
- **Label section** = `group.GroupLabel` (null → tanpa header, mis. grup Home).

## Overrides (6 kasus)
| Resource | Override |
|---|---|
| `home` | href `""`, `MatchAll=true`, selalu tampil (tak digate) |
| `master.categories` | href `master/product-categories` |
| `inventory.adjustments` | href `inventory/adjustments/new`, gate action `create` |
| `transactions.hub` | href `transactions`, `MatchAll=true` |
| `settings.errorlog` | href `settings/error-log` |
| `cashier.pos` | href `cashier/pos`, gate action `create`; + link ekstra "Riwayat Penjualan" (`bi-clock-history`, `cashier/sales`, policy `cashier.pos.index`) |

Override model mendukung: custom `Href`, custom gate `Action`, `MatchAll`, `AlwaysVisible`, dan daftar `ExtraItems`.

## Rendering
`NavMenu.razor` inject `IAuthorizationService` + `AuthenticationStateProvider`. Di `OnInitializedAsync`:
1. `NavMenuBuilder.Build()` → daftar section.
2. Untuk tiap item: bila `AlwaysVisible` → tampil; selain itu `Auth.AuthorizeAsync(user, item.Policy)`.
3. Buang item tak berizin; sembunyikan section tanpa item.
Menggantikan nested `<AuthorizeView>` + policy `*.any` (section kosong otomatis hilang).

## Dipertahankan (jangan disentuh)
- Brand header, `navbar-toggler`, dan tombol WIP `#sidebar-toggle` di bawah.
- File WIP lain: `NavMenu.razor.css`, `MainLayout.razor.css`, `app-interop.js`.

## Verifikasi
- Build hijau.
- Bandingkan menu hasil render vs daftar 27 item lama: label, href, ikon, urutan sama.
- Uji unit `NavMenuBuilder.Build()` (opsional tapi disarankan): assert jumlah section, item Home href `""`, POS punya 2 item, categories href benar.

## Out of scope
- Menghapus policy `*.any` dari `Program.cs` (dibiarkan, tidak dipakai NavMenu lagi).
- Perubahan gaya/CSS sidebar (WIP terpisah).

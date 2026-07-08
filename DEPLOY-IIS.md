# Deploy ke IIS (Windows Authentication, on-premise)

Panduan hosting `MyApp.Web` di **IIS lokal (dev)** dengan **Windows Authentication**.
Hosting model: **in-process** (ASP.NET Core Module v2) — IIS yang melakukan Windows Auth,
aplikasi memakai skema `Windows` (lihat `Program.cs`).

---

## 1. Prasyarat (sekali per mesin)

1. **.NET 10 Hosting Bundle** (ANCM + runtime untuk IIS):
   <https://dotnet.microsoft.com/download/dotnet/10.0> → "Hosting Bundle".
   Setelah install, jalankan `net stop was /y && net start w3svc` atau restart IIS:
   ```powershell
   iisreset
   ```

2. **Fitur IIS** (Control Panel → Turn Windows features on/off, atau PowerShell admin):
   ```powershell
   Enable-WindowsOptionalFeature -Online -FeatureName `
     IIS-WebServerRole, IIS-WebServer, IIS-WindowsAuthentication, IIS-WebSockets -All
   ```
   - **Windows Authentication** → wajib untuk `[Authorize]` / grup AD.
   - **WebSockets** → wajib untuk Blazor Server (Interactive Server) agar koneksi stabil.

---

## 2. Publish aplikasi

```powershell
cd "F:\4. My Data\Project\MyApplication"
dotnet publish src/MyApp.Web -c Release -o publish/MyApp.Web
```

Output di `publish/MyApp.Web` sudah berisi `web.config` (in-process) yang benar.

---

## 3. Buat site IIS (otomatis)

Jalankan **PowerShell sebagai Administrator**:

```powershell
.\scripts\setup-iis.ps1
```

Skrip akan: membuat App Pool (No Managed Code), membuat site, mengaktifkan
Windows Auth + menonaktifkan Anonymous. Lihat parameter di dalam skrip untuk
mengubah nama site / port / path.

> Setelah itu buka **http://localhost:8080** — browser otomatis mengirim kredensial
> Windows (intranet zone). Nama user tampil di NavMenu.

---

## 4. Akses database (penting)

Connection string memakai **Trusted_Connection** (Windows Integrated). Saat di IIS,
yang konek ke SQL Server adalah **identitas App Pool** (default `IIS APPPOOL\MyAppPool`).
Beri akses di SQL Server:

```sql
CREATE LOGIN [IIS APPPOOL\MyAppPool] FROM WINDOWS;
CREATE USER  [IIS APPPOOL\MyAppPool] FOR LOGIN [IIS APPPOOL\MyAppPool];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\MyAppPool];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\MyAppPool];
-- untuk migration/DDL saat dev bisa pakai db_owner
```

> Alternatif: ganti App Pool identity ke akun domain layanan, atau gunakan SQL
> Authentication (ubah connection string: `User Id=...;Password=...`).

---

## 5. Grup AD pengelola

Edit `publish/MyApp.Web/appsettings.json` → set grup AD asli:
```json
"Authorization": { "ManagerGroup": "CONTOSO\\Product-Managers" }
```
Anggota grup itu melihat tombol Tambah/Hapus & boleh `POST/PUT/DELETE`. User lain hanya read.

---

## Dev cepat tanpa IIS penuh: IIS Express

Profil **"IIS Express"** sudah dikonfigurasi (`launchSettings.json`) dengan
`windowsAuthentication: true`. Dari Visual Studio pilih profil *IIS Express*, atau:
```powershell
dotnet run --project src/MyApp.Web --launch-profile "IIS Express"
```

---

## Troubleshooting

| Gejala | Penyebab / solusi |
|---|---|
| HTTP 500.19 | Hosting Bundle belum terpasang → install lalu `iisreset` |
| HTTP 500.30 | App gagal start → set `stdoutLogEnabled="true"` di `web.config`, cek `logs/stdout` |
| HTTP 401 terus | Windows Auth belum aktif / Anonymous belum dimatikan di site |
| Login gagal ke DB | App Pool identity belum diberi akses SQL (lihat bagian 4) |
| Blazor sering reconnect | Fitur **WebSockets** IIS belum diaktifkan |

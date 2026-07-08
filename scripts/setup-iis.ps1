#requires -RunAsAdministrator
<#
.SYNOPSIS
  Membuat/menyiapkan site IIS untuk MyApp.Web dengan Windows Authentication (dev).
.NOTES
  Jalankan sebagai Administrator. Memerlukan modul WebAdministration (terpasang bersama IIS).
#>
[CmdletBinding()]
param(
    [string]$SiteName    = "MyApp",
    [string]$AppPoolName = "MyAppPool",
    [int]   $Port        = 8080,
    [string]$PhysicalPath = (Resolve-Path "$PSScriptRoot\..\publish\MyApp.Web").Path
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration

Write-Host "Site         : $SiteName"
Write-Host "App Pool     : $AppPoolName"
Write-Host "Port         : $Port"
Write-Host "Physical path: $PhysicalPath`n"

if (-not (Test-Path $PhysicalPath)) {
    throw "Path '$PhysicalPath' tidak ditemukan. Jalankan 'dotnet publish' dulu (lihat DEPLOY-IIS.md)."
}

# --- App Pool (No Managed Code untuk ASP.NET Core) ---
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Write-Host "App Pool '$AppPoolName' dibuat."
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode             -Value "AlwaysRunning"

# --- Site ---
if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -Port $Port -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName | Out-Null
    Write-Host "Site '$SiteName' dibuat pada port $Port."
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath     -Value $PhysicalPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool  -Value $AppPoolName
    Write-Host "Site '$SiteName' diperbarui."
}

# --- Windows Authentication ON, Anonymous OFF ---
$psPath = "IIS:\Sites\$SiteName"
Set-WebConfigurationProperty -PSPath $psPath -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name enabled -Value $true
Set-WebConfigurationProperty -PSPath $psPath -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name enabled -Value $false
Write-Host "Windows Authentication: ON, Anonymous: OFF."

Write-Host "`nSelesai. Buka: http://localhost:$Port"
Write-Host "Ingat: beri akses SQL ke identitas App Pool 'IIS APPPOOL\$AppPoolName' (lihat DEPLOY-IIS.md bagian 4)."

# 1-Click Sideload Installer for JelliBox
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1. Check and enable Developer Mode if needed
$devKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
try {
    $currentVal = (Get-ItemProperty -Path $devKey -Name "AllowDevelopmentWithoutDevLicense" -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
    if ($currentVal -ne 1) {
        Write-Host "Enabling Developer Mode..." -ForegroundColor Cyan
        Set-ItemProperty -Path $devKey -Name "AllowDevelopmentWithoutDevLicense" -Value 1 -Force
    }
} catch {
    Write-Warning "Could not automatically toggle Developer Mode in registry. Ensure Developer Mode is enabled in Windows Settings."
}

# 2. Import certificate into Trusted People if present
$cer = Get-ChildItem -Path $scriptDir -Filter "*.cer" -Recurse | Select-Object -First 1
if ($cer) {
    Write-Host "Trusting certificate ($($cer.Name))..." -ForegroundColor Cyan
    try {
        Import-Certificate -FilePath $cer.FullName -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" -ErrorAction SilentlyContinue | Out-Null
    } catch { }
}

# 2. Locate the .msix package
$msix = Get-ChildItem -Path $scriptDir -Filter "*.msix" -Recurse | Select-Object -First 1
if (-not $msix) {
    Write-Error "Could not find .msix package in $scriptDir"
    exit 1
}

$extractDir = Join-Path $scriptDir "ExtractedPackage"
if (Test-Path $extractDir) {
    Remove-Item -Recurse -Force $extractDir
}

Write-Host "Extracting $($msix.Name)..." -ForegroundColor Cyan
$tempZip = Join-Path $env:TEMP "$($msix.BaseName)_tmp.zip"
Copy-Item -Path $msix.FullName -Destination $tempZip -Force
try {
    Expand-Archive -Path $tempZip -DestinationPath $extractDir -Force
} finally {
    Remove-Item -Path $tempZip -Force -ErrorAction SilentlyContinue
}

$manifest = Join-Path $extractDir "AppxManifest.xml"
if (-not (Test-Path $manifest)) {
    Write-Error "AppxManifest.xml not found after extraction."
    exit 1
}

Write-Host "Registering JelliBox into Windows..." -ForegroundColor Cyan
Add-AppxPackage -Register $manifest

Write-Host "`n[SUCCESS] JelliBox has been installed! You can now launch it from the Start Menu.`n" -ForegroundColor Green
Read-Host "Press Enter to exit"

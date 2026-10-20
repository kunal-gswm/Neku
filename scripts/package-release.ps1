[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $proj = Join-Path $root "src\DesktopPet\DesktopPet.csproj"
    if (Test-Path $proj) {
        $xml = [xml](Get-Content $proj)
        $Version = ($xml.Project.PropertyGroup.Version | Where-Object { $_ }) -join ''
    }
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "1.0.4"
}

$artifacts = Join-Path $root "artifacts"
$dist = Join-Path $root "dist"
$exe = Join-Path $dist "Neku.exe"

Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $artifacts)) {
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
}

# 1. Standardize installer name
$setupSrc = Join-Path $artifacts "Neku-Setup-$Version.exe"
$setupDst = Join-Path $artifacts "Neku-Setup-v$Version.exe"
if (Test-Path $setupSrc) {
    Copy-Item $setupSrc -Destination $setupDst -Force
    Write-Host "Created $setupDst from $setupSrc" -ForegroundColor Green
} elseif (Test-Path $setupDst) {
    Write-Host "Standardized installer already exists at $setupDst" -ForegroundColor Green
} else {
    throw "Neither source installer ($setupSrc) nor target installer ($setupDst) found."
}

# 2. Package portable zip
$zipName = "Neku-v$Version-win-x64.zip"
$zipPath = Join-Path $artifacts $zipName
$tempStage = Join-Path ([System.IO.Path]::GetTempPath()) "Neku-v$Version-win-x64"

if (Test-Path $tempStage) {
    Remove-Item $tempStage -Recurse -Force
}
New-Item -ItemType Directory -Path $tempStage -Force | Out-Null

Copy-Item $exe -Destination $tempStage
Copy-Item (Join-Path $root "LICENSE") -Destination $tempStage
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") -Destination $tempStage
Copy-Item (Join-Path $root "README.md") -Destination $tempStage

# Stage CLI binaries and launcher wrapper if available
$cliBin = Join-Path $dist "bin"
if (Test-Path $cliBin) {
    Copy-Item $cliBin -Destination $tempStage -Recurse -Force
}
$cliCmd = Join-Path $dist "neku.cmd"
if (Test-Path $cliCmd) {
    Copy-Item $cliCmd -Destination $tempStage -Force
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "Compressing portable zip..." -ForegroundColor Cyan
[System.IO.Compression.ZipFile]::CreateFromDirectory($tempStage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $tempStage -Recurse -Force

Write-Host "Created $zipPath" -ForegroundColor Green

# 3. Generate SHA256SUMS.txt
$shaFile = Join-Path $artifacts "SHA256SUMS.txt"
$filesToHash = @(
    "Neku-Setup-v$Version.exe",
    "Neku-v$Version-win-x64.zip"
)

$lines = @()
foreach ($file in $filesToHash) {
    $fullPath = Join-Path $artifacts $file
    if (Test-Path $fullPath) {
        $hash = (Get-FileHash -Path $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $lines += "$hash  $file"
        Write-Host "SHA256 ($file): $hash" -ForegroundColor Yellow
    }
}

$lines | Set-Content -Path $shaFile -Encoding utf8
Write-Host "`nSHA256SUMS.txt generated at $shaFile" -ForegroundColor Green

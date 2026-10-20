<#
  Neku Installer Build Script
  Compiles the Inno Setup 6 installer for Neku using the published self-contained binary.

  Usage:
    .\scripts\build-installer.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\DesktopPet\DesktopPet.csproj"
$iss  = Join-Path $root "installer\Neku.iss"
$dist = Join-Path $root "dist"
$exe  = Join-Path $dist "Neku.exe"
$artifacts = Join-Path $root "artifacts"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "               Neku Windows Installer Builder               " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# ── 1. Read application version from project file ──
if (-not (Test-Path $proj)) {
    throw "Project file not found at $proj"
}

$xml = [xml](Get-Content $proj)
$version = ($xml.Project.PropertyGroup.Version | Where-Object { $_ }) -join ''
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not determine <Version> from $proj"
}
Write-Host "Target Application Version: $version" -ForegroundColor Green

# ── 2. Build fresh self-contained executable (unless -SkipPublish is passed) ──
if (-not $SkipPublish) {
    Write-Host "`nPublishing fresh release executable via scripts\publish.ps1..." -ForegroundColor Cyan
    $publishScript = Join-Path $root "scripts\publish.ps1"
    & pwsh -File $publishScript
    if ($LASTEXITCODE -ne 0) {
        throw "publish.ps1 failed with exit code $LASTEXITCODE"
    }
}

# ── 3. Verify published executables exist ──
if (-not (Test-Path $exe)) {
    throw "Published executable not found at $exe"
}
Write-Host "`nPublished executable verified: $exe" -ForegroundColor Green

$cliExe = Join-Path $dist "bin\neku.exe"
if (-not (Test-Path $cliExe)) {
    Write-Host "Published CLI binary not found at $cliExe. Publishing via scripts\publish.ps1..." -ForegroundColor Cyan
    $publishScript = Join-Path $root "scripts\publish.ps1"
    & pwsh -File $publishScript
    if ($LASTEXITCODE -ne 0) {
        throw "publish.ps1 failed with exit code $LASTEXITCODE"
    }
}
Write-Host "Published CLI verified: $cliExe" -ForegroundColor Green

# ── 4. Verify absence of private companion assets in binary ──
Write-Host "Auditing binary for private asset exclusion..." -ForegroundColor Cyan
$exeBytes = [System.IO.File]::ReadAllBytes($exe)
$exeAscii = [System.Text.Encoding]::ASCII.GetString($exeBytes)
if ($exeAscii.Contains("neku-personal-local")) {
    throw "SECURITY ALERT: 'neku-personal-local' string found in $exe binary! Aborting installer build."
}
Write-Host "Privacy check passed: No private companion assets in binary." -ForegroundColor Green

# ── 5. Locate Inno Setup compiler (ISCC.exe) ──
Write-Host "`nLocating Inno Setup 6 compiler (ISCC.exe)..." -ForegroundColor Cyan
$candidates = @(
    (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$iscc = $candidates | Select-Object -First 1
if (-not $iscc) {
    Write-Host "`nERROR: Inno Setup 6 compiler (ISCC.exe) was not found." -ForegroundColor Red
    Write-Host "Please install Inno Setup 6:" -ForegroundColor Yellow
    Write-Host "  • Official installer: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "  • GitHub Releases: https://github.com/jrsoftware/issrc/releases/tag/is-6_3_3" -ForegroundColor Yellow
    Write-Host "  • Or via winget: winget install --id JRSoftware.InnoSetup -e" -ForegroundColor Yellow
    throw "Inno Setup 6 is required to build the Windows installer."
}
Write-Host "Using Inno Setup compiler: $iscc" -ForegroundColor Green

# ── 6. Ensure artifacts directory exists ──
if (-not (Test-Path $artifacts)) {
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
}

# ── 7. Compile the installer ──
Write-Host "`nCompiling installer script $iss..." -ForegroundColor Cyan
$outputSetupName = "Neku-Setup-$version"
$compileArgs = @(
    "/Qp",
    "/DMyAppVersion=$version",
    "/O$artifacts",
    "/F$outputSetupName",
    $iss
)

$proc = Start-Process -FilePath $iscc -ArgumentList $compileArgs -Wait -PassThru -NoNewWindow
if ($proc.ExitCode -ne 0) {
    throw "ISCC compilation failed with exit code $($proc.ExitCode)"
}

# ── 8. Verify generated installer artifact ──
$expectedInstaller = Join-Path $artifacts "$outputSetupName.exe"
if (-not (Test-Path $expectedInstaller)) {
    throw "Expected installer was not generated at $expectedInstaller"
}

$installerItem = Get-Item $expectedInstaller
if ($installerItem.Length -lt 1MB) {
    throw "Generated installer size is suspiciously small: $($installerItem.Length) bytes"
}

$sizeMb = [math]::Round($installerItem.Length / 1MB, 2)
$sha256 = (Get-FileHash $expectedInstaller -Algorithm SHA256).Hash

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host "             INSTALLER COMPILED SUCCESSFULLY!               " -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  Installer Path : $expectedInstaller" -ForegroundColor Green
Write-Host "  Version        : $version" -ForegroundColor Green
Write-Host "  Size           : $sizeMb MB ($($installerItem.Length) bytes)" -ForegroundColor Green
Write-Host "  SHA-256        : $sha256" -ForegroundColor Green
Write-Host "============================================================`n" -ForegroundColor Green

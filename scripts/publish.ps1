<#
  Build the release artifacts:
    1. Single self-contained Neku.exe (WPF desktop companion)
    2. Single self-contained bin\neku.exe (CLI command executable)
    3. Convenience root launcher neku.cmd

  Usage:
    .\scripts\publish.ps1              # build into .\dist
    .\scripts\publish.ps1 -Run         # ...and launch desktop app to check it works
#>
param(
    [string]$OutDir = "dist",
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$projApp = Join-Path $root "src\DesktopPet\DesktopPet.csproj"
$projCli = Join-Path $root "src\Neku.Cli\Neku.Cli.csproj"
$out  = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }
$binDir = Join-Path $out "bin"

Write-Host "Publishing Neku Desktop Companion and CLI..." -ForegroundColor Cyan

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

# 1. Publish DesktopPet (Neku.exe)
Write-Host "  -> Publishing DesktopPet (Neku.exe)..." -ForegroundColor Cyan
dotnet publish $projApp -c Release -o $out --nologo -p:_IsPublishing=true | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "DesktopPet publish failed" }

$exeApp = Join-Path $out "Neku.exe"
if (-not (Test-Path $exeApp)) { throw "No Neku.exe in $out" }
Get-ChildItem $out -Filter *.pdb | Remove-Item -Force

# 2. Publish Neku.Cli (bin\neku.exe)
Write-Host "  -> Publishing Neku.Cli (bin\neku.exe)..." -ForegroundColor Cyan
dotnet publish $projCli -c Release -o $binDir --nologo -p:_IsPublishing=true | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "Neku.Cli publish failed" }

$exeCli = Join-Path $binDir "neku.exe"
$rawCli = Join-Path $binDir "NekuCli.exe"
if (Test-Path $rawCli) {
    if (Test-Path $exeCli) { Remove-Item $exeCli -Force }
    Move-Item $rawCli $exeCli -Force
}
if (-not (Test-Path $exeCli)) { throw "No neku.exe in $binDir" }
Get-ChildItem $binDir -Filter *.pdb | Remove-Item -Force

# 3. Create root convenience launcher neku.cmd for portable users
$cmdPath = Join-Path $out "neku.cmd"
Set-Content -Path $cmdPath -Value "@echo off`r`n`"%~dp0bin\neku.exe`" %*`r`n" -Encoding ASCII

# Output details
$versionApp = (Get-Item $exeApp).VersionInfo.ProductVersion
$sizeMbApp  = [math]::Round((Get-Item $exeApp).Length / 1MB, 1)
$hashApp    = (Get-FileHash $exeApp -Algorithm SHA256).Hash

$versionCli = (Get-Item $exeCli).VersionInfo.ProductVersion
$sizeMbCli  = [math]::Round((Get-Item $exeCli).Length / 1MB, 1)
$hashCli    = (Get-FileHash $exeCli -Algorithm SHA256).Hash

Write-Host ""
Write-Host "  Published Desktop Companion : $exeApp" -ForegroundColor Green
Write-Host "    Version : $versionApp"  -ForegroundColor Green
Write-Host "    Size    : $sizeMbApp MB" -ForegroundColor Green
Write-Host "    SHA256  : $hashApp"     -ForegroundColor Green
Write-Host ""
Write-Host "  Published Terminal CLI      : $exeCli" -ForegroundColor Green
Write-Host "    Version : $versionCli"  -ForegroundColor Green
Write-Host "    Size    : $sizeMbCli MB" -ForegroundColor Green
Write-Host "    SHA256  : $hashCli"     -ForegroundColor Green
Write-Host ""
Write-Host "  Portable Command Wrapper    : $cmdPath" -ForegroundColor Green
Write-Host ""

# Verify publish layout
$stray = Get-ChildItem $out | Where-Object { $_.Name -notin @("Neku.exe", "bin", "neku.cmd") }
if ($stray) {
    Write-Warning "Unexpected extra files in publish output:"
    $stray | ForEach-Object { Write-Warning "  $($_.Name)" }
}

if ($Run) {
    Write-Host "Launching desktop app..." -ForegroundColor Cyan
    Start-Process $exeApp
}

<#
.SYNOPSIS
    Lightweight character manifest validator and contact sheet generator for Neku.

.DESCRIPTION
    Validates character packs against sprite sheet dimensions, ensures all animation frames
    are in bounds, identifies missing recommended animations, and optionally generates
    a labeled contact sheet preview image in tools/previews/.

.PARAMETER PackId
    Specific companion pack ID to validate (e.g. 'neku', 'neku-personal-local', 'cat').
    If omitted, validates all discovered packs in src/DesktopPet/Assets/pets.

.PARAMETER GenerateContactSheet
    If specified, renders a contact sheet PNG with numbered frames into tools/previews/<packId>_contact_sheet.png.

.EXAMPLE
    pwsh .\tools\validate_and_preview_companion.ps1
    pwsh .\tools\validate_and_preview_companion.ps1 -PackId neku -GenerateContactSheet
#>
param(
    [string]$PackId = "",
    [switch]$GenerateContactSheet
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$petsDir = Join-Path $root "src\DesktopPet\Assets\pets"

if (-not (Test-Path $petsDir)) {
    Write-Error "Pets directory not found at $petsDir"
    exit 1
}

$packs = if ($PackId) {
    @(Join-Path $petsDir $PackId)
} else {
    Get-ChildItem -Directory $petsDir | Select-Object -ExpandProperty FullName
}

Add-Type -AssemblyName System.Drawing

$previewDir = Join-Path $PSScriptRoot "previews"
if ($GenerateContactSheet -and -not (Test-Path $previewDir)) {
    New-Item -ItemType Directory -Path $previewDir -Force | Out-Null
}

$allValid = $true
$recommendedAnimations = @('idle', 'walk', 'sleep', 'fall', 'pet', 'stretch', 'typing', 'wave', 'celebrate', 'confused', 'happy', 'annoyed', 'surprised')

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "         Neku Character Pack & Animation Validator          " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

foreach ($dir in $packs) {
    $id = Split-Path -Leaf $dir
    $manifestPath = Join-Path $dir "manifest.json"

    Write-Host "`nChecking pack: $id" -ForegroundColor Yellow

    if (-not (Test-Path $manifestPath)) {
        Write-Host "  [FAIL] Missing manifest.json at $manifestPath" -ForegroundColor Red
        $allValid = $false
        continue
    }

    try {
        $json = Get-Content $manifestPath -Raw | ConvertFrom-Json
    } catch {
        Write-Host "  [FAIL] Invalid JSON syntax in manifest: $($_.Exception.Message)" -ForegroundColor Red
        $allValid = $false
        continue
    }

    $cellW = [int]$json.cellWidth
    $cellH = [int]$json.cellHeight
    $cols  = [int]$json.columns
    $sheetFile = [string]$json.sheet

    if ($cellW -le 0 -or $cellH -le 0) {
        Write-Host "  [FAIL] Invalid cell dimensions: ${cellW}x${cellH}" -ForegroundColor Red
        $allValid = $false
        continue
    }

    if ($cols -le 0) {
        Write-Host "  [FAIL] Invalid column count: $cols" -ForegroundColor Red
        $allValid = $false
        continue
    }

    $sheetPath = Join-Path $dir $sheetFile
    if (-not (Test-Path $sheetPath)) {
        Write-Host "  [FAIL] Spritesheet not found: $sheetPath" -ForegroundColor Red
        $allValid = $false
        continue
    }

    # Load image and compute total frame capacity
    $img = $null
    try {
        $img = [System.Drawing.Image]::FromFile($sheetPath)
        $sheetW = $img.Width
        $sheetH = $img.Height
    } catch {
        Write-Host "  [FAIL] Could not decode sprite sheet image: $($_.Exception.Message)" -ForegroundColor Red
        $allValid = $false
        if ($img) { $img.Dispose() }
        continue
    }

    $calcCols = [math]::Floor($sheetW / $cellW)
    $calcRows = [math]::Floor($sheetH / $cellH)
    $maxFrames = $calcCols * $calcRows

    Write-Host "  [INFO] Sheet: ${sheetW}x${sheetH} px | Cells: ${cellW}x${cellH} | Grid: ${calcCols} cols x ${calcRows} rows = $maxFrames frames" -ForegroundColor Gray

    if ($cols -gt $calcCols) {
        Write-Host "  [FAIL] Manifest columns ($cols) exceeds image width capacity ($calcCols)" -ForegroundColor Red
        $allValid = $false
    }

    # Validate animations
    $animProps = @($json.animations.PSObject.Properties)
    if ($animProps.Count -eq 0) {
        Write-Host "  [FAIL] No animations defined in manifest" -ForegroundColor Red
        $allValid = $false
        $img.Dispose()
        continue
    }

    $animNames = @($animProps | Select-Object -ExpandProperty Name)

    if (-not ($animNames -contains 'idle')) {
        Write-Host "  [FAIL] Missing required 'idle' animation" -ForegroundColor Red
        $allValid = $false
    }

    $missingRecommended = @()
    foreach ($rec in $recommendedAnimations) {
        if (-not ($animNames -contains $rec)) {
            $missingRecommended += $rec
        }
    }

    if ($missingRecommended.Count -gt 0) {
        Write-Host "  [WARN] Missing recommended animations (will use engine fallbacks): $($missingRecommended -join ', ')" -ForegroundColor DarkYellow
    }

    $hasFrameErrors = $false
    foreach ($prop in $animProps) {
        $animName = $prop.Name
        $def = $prop.Value
        $frames = @($def.frames)

        if ($frames.Count -eq 0) {
            Write-Host "  [FAIL] Animation '$animName' has 0 frames" -ForegroundColor Red
            $hasFrameErrors = $true
            continue
        }

        foreach ($f in $frames) {
            $idx = [int]$f
            if ($idx -lt 0) {
                Write-Host "  [FAIL] Animation '$animName' has negative frame index: $idx" -ForegroundColor Red
                $hasFrameErrors = $true
            } elseif ($idx -ge $maxFrames) {
                Write-Host "  [FAIL] Animation '$animName' frame index $idx exceeds sheet capacity ($maxFrames)" -ForegroundColor Red
                $hasFrameErrors = $true
            }
        }
    }

    if ($hasFrameErrors) {
        $allValid = $false
    } else {
        Write-Host "  [OK] All $($animProps.Count) animations and frame indices are valid!" -ForegroundColor Green
    }

    # Contact sheet generation
    if ($GenerateContactSheet -and -not $hasFrameErrors) {
        $sheetOut = Join-Path $previewDir "${id}_contact_sheet.png"
        Write-Host "  [PREVIEW] Rendering contact sheet: $sheetOut" -ForegroundColor Cyan

        $targetW = [int]($calcCols * $cellW)
        $targetH = [int]($calcRows * $cellH)
        $bmp = New-Object System.Drawing.Bitmap($targetW, $targetH, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(30, 30, 35))

        $font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(80, 255, 255, 255), 1)

        for ($idx = 0; $idx -lt $maxFrames; $idx++) {
            $col = $idx % $calcCols
            $row = [int][math]::Floor($idx / $calcCols)
            $srcX = [int]($col * $cellW)
            $srcY = [int]($row * $cellH)

            $srcRect = New-Object System.Drawing.Rectangle($srcX, $srcY, $cellW, $cellH)
            $destRect = New-Object System.Drawing.Rectangle($srcX, $srcY, $cellW, $cellH)

            $g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
            $g.DrawRectangle($pen, $destRect)
            $g.DrawString("#$idx", $font, $brush, [float]($srcX + 4), [float]($srcY + 4))
        }

        $font.Dispose()
        $brush.Dispose()
        $pen.Dispose()
        $g.Dispose()

        $bmp.Save($sheetOut, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "  [PREVIEW] Saved: $sheetOut" -ForegroundColor Green
    }

    $img.Dispose()
}

Write-Host "`n============================================================" -ForegroundColor Cyan
if ($allValid) {
    Write-Host "  ALL PACKS VALIDATED SUCCESSFULLY!                        " -ForegroundColor Green
} else {
    Write-Host "  VALIDATION FAILED ON ONE OR MORE PACKS                   " -ForegroundColor Red
}
Write-Host "============================================================`n" -ForegroundColor Cyan

if (-not $allValid) { exit 1 }

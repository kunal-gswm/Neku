<#
  Removes the Neku Desktop shortcut and disables launch-at-login.
  Does not delete the repo or built files.
#>
$ErrorActionPreference = "SilentlyContinue"

# Stop a running instance.
Get-Process Neku -ErrorAction SilentlyContinue | Stop-Process -Force

# Remove desktop shortcut.
$lnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "Neku.lnk"
if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Host "Removed desktop shortcut." -ForegroundColor Green }

# Remove auto-start entry.
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Neku" -ErrorAction SilentlyContinue
Write-Host "Auto-start at login: DISABLED" -ForegroundColor Green

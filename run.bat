@echo off
REM One-click launcher for Neku
cd /d "%~dp0"
if not exist "src\DesktopPet\bin\Debug\net10.0-windows\win-x64\Neku.exe" (
    echo Building Neku...
    dotnet build src\DesktopPet\DesktopPet.csproj
)
start "" "src\DesktopPet\bin\Debug\net10.0-windows\win-x64\Neku.exe"

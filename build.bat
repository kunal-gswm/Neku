@echo off
REM One-click build of Neku (Release). Double-click me.
cd /d "%~dp0"
echo Building Neku (Release)...
dotnet build src\DesktopPet\DesktopPet.csproj -c Release
echo.
echo Build finished. The app is at:
echo   src\DesktopPet\bin\Release\net10.0-windows\win-x64\Neku.exe
echo.
pause

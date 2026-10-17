@echo off
rem Send a developer event to Neku desktop companion
rem Usage: neku-event.bat build_success "Build completed successfully!"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0neku-event.ps1" %*

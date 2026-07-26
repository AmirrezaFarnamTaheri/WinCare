@echo off
setlocal
where pwsh.exe >nul 2>&1 || (echo WinCare requires PowerShell 7.2 or later. && exit /b 127)
pwsh.exe -NoLogo -NoProfile -STA -File "%~dp0WinCare.ps1" -Gui %*


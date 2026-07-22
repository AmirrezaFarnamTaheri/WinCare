@echo off
setlocal
where pwsh.exe >nul 2>&1
if errorlevel 1 (
  echo WinCare requires PowerShell 7.2 or later. Install PowerShell and try again.
  exit /b 127
)
pwsh.exe -NoLogo -NoProfile -STA -File "%~dp0WinCare-GUI.ps1" %*

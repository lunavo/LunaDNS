@echo off
rem LunaDNS: build tu ma nguon roi mo app (KH7 K12). Can .NET 8 SDK. Nguoi dung thuong: tai ban da build o GitHub Releases.
chcp 65001 >nul
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1 || goto :nosdk
dotnet --list-sdks | findstr /b /c:"8." >nul || goto :nosdk

set "PS=pwsh"
where pwsh >nul 2>&1 || set "PS=powershell"
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish.ps1" -SkipZip -SkipInstaller
if errorlevel 1 (
  echo.
  echo Build lỗi. Xem thông báo ở trên.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\publish\win-x64\LunaDNS.exe"
exit /b 0

:nosdk
echo Cần .NET 8 SDK để build từ mã nguồn.
echo   Cài: winget install Microsoft.DotNet.SDK.8
echo   Hoặc tải bản đã build sẵn (không cần .NET) ở trang Releases của repo trên GitHub.
pause
exit /b 1

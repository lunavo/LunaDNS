# Sinh icon và logo LunaDNS từ một nguồn hình (build/icongen, SkiaSharp):
#   lunadns.ico 16/20/24/32/48/64/256; tray-off/on/error.ico 16/20/24/32;
#   Assets/Logo.axaml (DrawingImage cho UI); docs/assets/lunadns-mark.svg (README).
# Mọi khung dùng chung một hình (SVG Gemini, PO chốt 14/11); khay chỉ khác màu: tắt xám, lỗi thêm chấm đỏ.
# Thêm đối số thứ hai là một thư mục để xuất PNG xem thử + sheet.png phóng to.
param([string]$PreviewDir)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$cmdArgs = @('run', '--project', (Join-Path $repo 'build\icongen'), '--', $repo)
if ($PreviewDir) { $cmdArgs += $PreviewDir }
& dotnet @cmdArgs
if ($LASTEXITCODE -ne 0) { throw "icongen lỗi (mã $LASTEXITCODE)" }

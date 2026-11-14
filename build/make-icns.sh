#!/bin/bash
# Sinh asset macOS từ cùng nguồn hình với .ico (build/icongen, chế độ --macos):
#   src/LunaDns.App/Assets/tray-{off,on,error}-mac.png   icon template cho thanh menu (36 px = 18 pt @2x)
#   src/LunaDns.App/Assets/LunaDNS.icns                  icon .app (iconutil, 16…1024 px)
# Chỉ cần chạy lại khi đổi hình; build/publish-macos.sh dùng file .icns đã commit.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
dotnet run --project "$root/build/icongen" -- --macos "$root" "$work/LunaDNS.iconset"
iconutil -c icns "$work/LunaDNS.iconset" -o "$root/src/LunaDns.App/Assets/LunaDNS.icns"
echo "Đã ghi $root/src/LunaDns.App/Assets/LunaDNS.icns"

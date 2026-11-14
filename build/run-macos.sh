#!/bin/bash
# Chạy giao diện LunaDNS trên macOS từ mã nguồn. Dựng một LunaDNS.app tối thiểu, chưa ký (bản đóng gói thật là Bước 4):
#   LunaDNS.app/Contents/MacOS/LunaDNS          giao diện (self-contained)
#   LunaDNS.app/Contents/MacOS/daemon/lunadns   CLI + dịch vụ nền, nút "Cài dịch vụ nền" tìm ở đây
# Tham số: --build-only để chỉ dựng, in đường dẫn .app.
# Không cần sudo: giao diện chạy quyền user, mọi thay đổi hệ thống đi qua dịch vụ nền.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
case "$(uname -m)" in
  arm64) rid=osx-arm64 ;;
  x86_64) rid=osx-x64 ;;
  *) echo "Kiến trúc lạ: $(uname -m)" >&2; exit 2 ;;
esac
app="$root/artifacts/run/$rid/LunaDNS.app"
out="$app/Contents/MacOS"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/LunaDns.App/LunaDns.App.csproj" | head -1)"

rm -rf "$app"
dotnet publish "$root/src/LunaDns.MacCli" -c Release -r "$rid" --self-contained -o "$out/daemon" -nologo -v quiet
dotnet publish "$root/src/LunaDns.App" -f net8.0 -c Release -r "$rid" --self-contained -o "$out" -nologo -v quiet
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>vn.lunavo.lunadns.app</string>
  <key>CFBundleName</key><string>LunaDNS</string>
  <key>CFBundleDisplayName</key><string>LunaDNS</string>
  <key>CFBundleExecutable</key><string>LunaDNS</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

if [[ "${1:-}" == "--build-only" ]]; then
  echo "$app"
  exit 0
fi
open "$app"

#!/bin/bash
# Hàm dùng chung cho build/run-macos.sh và build/publish-macos.sh (source file này, không chạy trực tiếp).
# build_app <rid> <đường dẫn LunaDNS.app> <version> [tham số dotnet publish thêm...]
# Bố cục:
#   LunaDNS.app/Contents/Info.plist
#   LunaDNS.app/Contents/MacOS/LunaDNS          giao diện (self-contained)
#   LunaDNS.app/Contents/MacOS/daemon/lunadns   CLI + dịch vụ nền; nút "Cài dịch vụ nền" tìm ở đây,
#                                               `lunadns install` chỉ chép thư mục daemon/ vào /Library/PrivilegedHelperTools
#   LunaDNS.app/Contents/Resources/LunaDNS.icns

macos_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

csproj_version() {
  sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$macos_root/src/LunaDns.App/LunaDns.App.csproj" | head -1
}

build_app() {
  local rid="$1" app="$2" version="$3"
  shift 3
  local out="$app/Contents/MacOS"
  rm -rf "$app"
  dotnet publish "$macos_root/src/LunaDns.MacCli" -c Release -r "$rid" --self-contained -o "$out/daemon" -nologo -v quiet "$@"
  dotnet publish "$macos_root/src/LunaDns.App" -f net8.0 -c Release -r "$rid" --self-contained -o "$out" -nologo -v quiet "$@"
  mkdir -p "$app/Contents/Resources"
  cp "$macos_root/src/LunaDns.App/Assets/LunaDNS.icns" "$app/Contents/Resources/LunaDNS.icns"
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>vn.lunavo.lunadns.app</string>
  <key>CFBundleName</key><string>LunaDNS</string>
  <key>CFBundleDisplayName</key><string>LunaDNS</string>
  <key>CFBundleExecutable</key><string>LunaDNS</string>
  <key>CFBundleIconFile</key><string>LunaDNS</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleDevelopmentRegion</key><string>vi</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>© Luna Vo. Giấy phép MIT.</string>
</dict>
</plist>
PLIST
  plutil -lint "$app/Contents/Info.plist" >/dev/null
}

# Ký ad-hoc (không cần Apple Developer ID). Apple Silicon không chạy Mach-O thiếu chữ ký. codesign coi mọi file trong
# Contents/MacOS là code con, nên ký từng file (với .dll/.json chữ ký nằm trong extended attribute, như hướng dẫn đóng gói
# của Avalonia) rồi mới ký bundle, để `codesign --verify --deep --strict` qua. Không thay cho notarize.
sign_adhoc() {
  local app="$1" f
  while IFS= read -r -d '' f; do
    [[ "$f" == "$app/Contents/MacOS/LunaDNS" ]] && continue   # file chính được ký cùng bundle
    codesign --force --sign - --timestamp=none "$f" 2>/dev/null
  done < <(find "$app/Contents/MacOS" -type f -print0)
  codesign --force --sign - --timestamp=none "$app"
  codesign --verify --deep --strict "$app"
}

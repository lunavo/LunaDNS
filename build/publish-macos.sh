#!/bin/bash
# Build LunaDNS macOS bản phát hành (tương đương build/publish.ps1 của Windows). Chạy trên macOS, không cần sudo.
#   artifacts/publish/<rid>/LunaDNS.app        ký ad-hoc
#   artifacts/LunaDNS-<v>-macos-arm64.dmg     Apple Silicon
#   artifacts/LunaDNS-<v>-macos-x64.dmg       Mac Intel
#   artifacts/SHA256SUMS-macos.txt
# Tham số: --version x.y.z (mặc định <Version> trong LunaDns.App.csproj), --rid osx-arm64|osx-x64|all (mặc định all).
# DMG nén ULMO (lzma, cần macOS 10.15+; app đòi 12.0): nhỏ hơn UDZO khoảng 30%.
# Không có bản universal: bản self-contained .NET có runtime/assembly khác nhau theo kiến trúc, lipo không ghép được.
set -euo pipefail
source "$(dirname "$0")/macos-bundle.sh"

version="" rids="all"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --rid) rids="$2"; shift 2 ;;
    *) echo "Tham số lạ: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$version" ]] || version="$(csproj_version)"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version phải dạng x.y.z, nhận được '$version'." >&2; exit 2; }
[[ "$rids" == all ]] && rids="osx-arm64 osx-x64"

artifacts="$macos_root/artifacts"
mkdir -p "$artifacts"
dmgs=()
for rid in $rids; do
  case "$rid" in
    osx-arm64) arch=arm64 ;;
    osx-x64) arch=x64 ;;
    *) echo "RID không hỗ trợ: $rid" >&2; exit 2 ;;
  esac
  staging="$artifacts/publish/$rid"
  app="$staging/LunaDNS.app"
  rm -rf "$staging"
  mkdir -p "$staging"
  echo "== $rid: dotnet publish"
  # DebugType=embedded: không ra .pdb rời (như bản Windows).
  # PublishTrimmed: app ~103 → ~45 MB, daemon 77 → 22 MB. JSON đi qua serializer sinh sẵn (journal, giao thức daemon,
  # settings) vì .NET 8 tắt JSON reflection khi trim; cảnh báo trim là lỗi (TreatWarningsAsErrors).
  build_app "$rid" "$app" "$version" -p:Version="$version" -p:DebugType=embedded -p:PublishTrimmed=true

  # Kiểm hình dạng bản build.
  for f in Contents/MacOS/LunaDNS Contents/MacOS/daemon/lunadns Contents/Resources/LunaDNS.icns Contents/Info.plist; do
    [[ -e "$app/$f" ]] || { echo "Thiếu $f trong LunaDNS.app." >&2; exit 1; }
  done
  stray="$(find "$app" -name '*.pdb')"
  [[ -z "$stray" ]] || { echo "Còn .pdb rời: $stray" >&2; exit 1; }
  want=$([[ $arch == arm64 ]] && echo arm64 || echo x86_64)
  for exe in "$app/Contents/MacOS/LunaDNS" "$app/Contents/MacOS/daemon/lunadns"; do
    lipo -archs "$exe" | grep -qx "$want" || { echo "$exe không phải $want: $(lipo -archs "$exe")" >&2; exit 1; }
  done

  echo "== $rid: ký ad-hoc"
  sign_adhoc "$app"

  echo "== $rid: tạo .dmg"
  ln -s /Applications "$staging/Applications"
  sed "s/{VERSION}/$version/g" "$macos_root/build/HUONG-DAN-macOS.txt" > "$staging/HUONG-DAN.txt"
  dmg="$artifacts/LunaDNS-$version-macos-$arch.dmg"
  rm -f "$dmg"
  hdiutil create -quiet -volname "LunaDNS $version" -srcfolder "$staging" -fs HFS+ -format ULMO -ov "$dmg"
  rm "$staging/Applications" "$staging/HUONG-DAN.txt"
  hdiutil verify -quiet "$dmg"
  du -h "$dmg"
  dmgs+=("$(basename "$dmg")")
done

(cd "$artifacts" && shasum -a 256 "${dmgs[@]}" > SHA256SUMS-macos.txt && cat SHA256SUMS-macos.txt)

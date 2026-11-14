#!/bin/bash
# Chạy giao diện LunaDNS trên macOS từ mã nguồn: dựng artifacts/run/<rid>/LunaDNS.app (chưa ký, chỉ chạy trên máy build) rồi mở.
# Bản phát hành (.dmg, ký ad-hoc) dùng build/publish-macos.sh. Bố cục .app xem build/macos-bundle.sh.
# Tham số: --build-only để chỉ dựng, in đường dẫn .app.
# Không cần sudo: giao diện chạy quyền user, mọi thay đổi hệ thống đi qua dịch vụ nền.
set -euo pipefail
source "$(dirname "$0")/macos-bundle.sh"

case "$(uname -m)" in
  arm64) rid=osx-arm64 ;;
  x86_64) rid=osx-x64 ;;
  *) echo "Kiến trúc lạ: $(uname -m)" >&2; exit 2 ;;
esac
app="$macos_root/artifacts/run/$rid/LunaDNS.app"
build_app "$rid" "$app" "$(csproj_version)"

if [[ "${1:-}" == "--build-only" ]]; then
  echo "$app"
  exit 0
fi
open "$app"

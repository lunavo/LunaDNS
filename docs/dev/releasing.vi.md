# Phát hành

<sub>[English](releasing.md)</sub>

Bản phát hành do GitHub Actions dựng từ tag phiên bản và nằm ở dạng **nháp (Draft)**. Chưa ai thấy cho tới khi người duy trì bấm phát hành bản nháp.

## Các bước

1. **Tăng phiên bản** trong `src/LunaDns.App/LunaDns.App.csproj` (`<Version>x.y.z</Version>`). Mọi gói đều đọc từ đây.
2. **Viết release notes** ở `docs/release-notes/vx.y.z.md`. File này thành nội dung trang Release. Viết ngắn: thay đổi gì, tải file nào, cảnh báo đã biết (exe chưa ký, SmartScreen, Smart App Control).
3. Commit, rồi gắn tag và đẩy lên:

```bash
git tag vx.y.z
git push origin main vx.y.z
```

4. **Chờ workflow `release`** (`.github/workflows/release.yml`):
   - Job `release-macos` (`macos-latest`): kiểm tag dạng `vX.Y.Z`, build với `-warnaserror`, chạy test Core, Platform.MacOS và App (`net8.0`), chạy `build/publish-macos.sh`.
   - Job `release` (`windows-latest`): kiểm tag khớp `<Version>` và có file release notes, build, chạy test Core, Platform.Windows và App (`net8.0-windows`), chạy `build/publish.ps1`, rồi tạo bản nháp bằng `gh release create --draft`.
   - Phiên bản bắt đầu bằng `0.` tự được đánh dấu **prerelease**.
5. **Thử tay các file trong bản nháp.** Tải về (đăng nhập bằng tài khoản người duy trì mới thấy bản nháp), kiểm `SHA256SUMS.txt`, cài trên máy sạch. Windows Sandbox (`build/sandbox/LunaDNS.wsb`) dùng được cho cả Setup lẫn zip portable.
6. **Phát hành:**

```bash
gh release edit vx.y.z -R lunavo/LunaDNS --draft=false
```

Tuỳ chọn: bỏ nhãn prerelease và đánh dấu bản mới nhất:

```bash
gh release edit vx.y.z -R lunavo/LunaDNS --prerelease=false --latest
```

## Các file

| File | Do đâu sinh |
|---|---|
| `LunaDNS-Setup-x.y.z.exe` | `build/publish.ps1` + Inno Setup (`build/installer/LunaDNS.iss`) |
| `LunaDNS-x.y.z-portable.zip` | `build/publish.ps1`; kèm `build/HUONG-DAN.txt` đã điền `{VERSION}` |
| `SHA256SUMS.txt` | `build/publish.ps1` |
| `LunaDNS-x.y.z-macos-arm64.dmg`, `…-macos-x64.dmg` | `build/publish-macos.sh`; kèm `build/HUONG-DAN-macOS.txt` |
| `SHA256SUMS-macos.txt` | `build/publish-macos.sh` |

GitHub tự thêm `Source code (zip)` và `Source code (tar.gz)` vào mọi bản phát hành. Hai file này không có file chạy. Trang [Bắt đầu nhanh](../guide/00-bat-dau-nhanh.md) đã dặn người dùng đừng chọn.

## Dễ vấp

- **Bản nháp vô hình.** Chỉ người duy trì thấy bản nháp. Người dùng mở trang Releases không thấy gì, và app tự kiểm cập nhật cũng bỏ qua bản nháp. Ai báo "không có bản nào" thì kiểm `gh release list -R lunavo/LunaDNS`.
- **Tag phải khớp `<Version>`**, và phải có `docs/release-notes/vx.y.z.md`, nếu không job Windows dừng trước khi build.
- **Chạy lại cần tag mới**, hoặc xoá tag cũ và bản nháp trước: `gh release create` lỗi nếu release đã có.
- App so với phiên bản cao nhất trong các bản không phải nháp, kể cả prerelease.

## Ký số (chưa làm)

- **Windows:** exe và Setup chưa ký Authenticode, nên SmartScreen cảnh báo và Smart App Control trên Windows 11 chặn hẳn. Workflow có sẵn chỗ cho SignPath: ký `LunaDNS.exe` trước khi Inno Setup đóng gói, rồi ký Setup.
- **macOS:** ký ad-hoc, chưa notarize. Khi có Apple Developer ID: ký bằng `codesign --options runtime` kèm entitlements JIT của .NET, rồi `xcrun notarytool submit --wait` và `xcrun stapler staple` cho từng `.dmg`.

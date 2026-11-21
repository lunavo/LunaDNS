# Đóng góp cho LunaDNS

<sub>[English](CONTRIBUTING.md) · **Tiếng Việt**</sub>

Cảm ơn bạn. Báo lỗi từ người dùng cũng quý như đóng góp mã.

## Báo lỗi

1. Xem [Xử lý sự cố](docs/guide/08-su-co.md) trước.
2. Trong app bấm **Chẩn đoán**, hoặc chạy `LunaDNS.exe --report` trong PowerShell mở bằng Admin. App ghi `LunaDNS-report-<thời gian>.txt` ra Desktop, đã che thông tin cá nhân (tên người dùng, tên máy, IP nội bộ). Đọc lại trước khi gửi.
3. Mở [Issue](https://github.com/lunavo/LunaDNS/issues), ghi: hệ điều hành và phiên bản, phiên bản LunaDNS, file đã cài (Setup, portable, .dmg), bạn đã làm gì, mong đợi gì, thực tế ra sao, kèm báo cáo.

Lỗi bảo mật thì báo riêng cho người duy trì, đừng mở Issue công khai.

## Sửa mã

1. Đọc [Kiến trúc](docs/dev/architecture.vi.md) và [Build và test](docs/dev/building-and-testing.vi.md).
2. Fork, tạo nhánh, sửa kèm test.
3. Trước khi mở pull request:

```powershell
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.App.Tests -f net8.0-windows
dotnet test tests/LunaDns.Platform.Windows.Tests
```

(Trên macOS dùng `-f net8.0` và `tests/LunaDns.Platform.MacOS.Tests`.) Có đụng mã NRPT, hosts, proxy hay resolver thì chạy thêm test Admin trên máy hỏng cũng không sao.

4. Mở PR vào `main`. CI phải xanh cả Windows lẫn macOS.

Các luật giữ an toàn cho máy người dùng:

- Mọi thay đổi hệ thống đi qua `INetworkConfigurator` và journal: lập kế hoạch, ghi journal, rồi mới áp dụng. Không hoàn tác được từ journal thì không phát hành.
- Chỉ đụng thứ LunaDNS tạo (Comment NRPT `LunaDNS …`, dấu `# LunaDNS`). Không sửa luật của chương trình khác.
- Không đổi tên op hay tên tính năng trong journal: mục cũ trên máy người dùng phải còn hoàn tác được.
- Proxy phải tiếp tục từ chối host không thuộc Steam.

## Tài liệu

Hướng dẫn người dùng có bản tiếng Việt (`docs/guide/`) và tiếng Anh (`docs/guide/en/`). Đổi hành vi thì sửa cả hai, hoặc ghi trong PR bản nào còn cần dịch.

## Giấy phép

Đóng góp nghĩa là bạn đồng ý phát hành phần đó theo [giấy phép MIT](LICENSE).

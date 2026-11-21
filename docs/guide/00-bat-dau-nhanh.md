# 0. Bắt đầu nhanh (cho người dùng thường)

<sub>[English](en/00-quick-start.md)</sub>

Trang này dành cho bạn nếu chỉ muốn vào được Steam, không cần biết kỹ thuật. Chi tiết từng phần ở các chương sau.

## LunaDNS làm gì?

Một số nhà mạng làm hỏng đường vào Steam: đăng nhập lỗi, Store hay Community trắng trang. LunaDNS sửa đúng phần đó **chỉ cho tên miền Steam**. Các trang web khác, mạng LAN và game không bị đụng tới. Tắt app bằng "Thoát & khôi phục" là máy về như cũ.

## Tải file nào?

Vào trang [Releases](https://github.com/lunavo/LunaDNS/releases), mở bản mới nhất, kéo xuống mục **Assets**:

| Máy của bạn | Tải file | Ghi chú |
|---|---|---|
| Windows 10/11 (khuyên dùng) | `LunaDNS-Setup-x.y.z.exe` | Trình cài, bấm đúp là cài |
| Windows, không muốn cài | `LunaDNS-x.y.z-portable.zip` | **Không phải trình cài**: giải nén rồi chạy `LunaDNS.exe` |
| Mac chip Apple (M1–M4) | `LunaDNS-x.y.z-macos-arm64.dmg` | |
| Mac chip Intel | `LunaDNS-x.y.z-macos-x64.dmg` | |

**Đừng tải** `Source code (zip)` hay `Source code (tar.gz)`: đó là mã nguồn cho lập trình viên, không có file chạy.

Không cần cài .NET hay gì thêm.

## Windows: cài bằng Setup

1. Bấm đúp `LunaDNS-Setup-x.y.z.exe`.
2. Hiện **"Windows protected your PC"**: bấm **More info**, rồi **Run anyway**. Cảnh báo này hiện vì bản thử chưa được ký số.
3. Hộp **"Do you want to allow this app…"** ghi *Unknown publisher*: bấm **Yes**.
4. Bấm Next đến hết, tích **Mở LunaDNS** ở bước cuối.

## Windows: dùng bản portable (zip)

1. Chuột phải file zip → **Properties** → tích **Unblock** → **OK** (không có ô này thì bỏ qua).
2. Chuột phải file zip → **Extract All…** → chọn thư mục, ví dụ `C:\Tools\LunaDNS`. Phải **giải nén cả thư mục**. Bấm đúp exe ngay trong cửa sổ zip thì app không chạy được vì thiếu các file `.dll` đi kèm.
3. Mở thư mục vừa giải nén, bấm đúp `LunaDNS.exe`, bấm **Yes** khi Windows hỏi quyền Admin.

## Mac

1. Mở file `.dmg`, kéo **LunaDNS** vào **Applications**.
2. Mở LunaDNS. Lần đầu macOS báo không mở được: vào **System Settings → Privacy & Security**, kéo xuống, bấm **Open Anyway**, rồi mở lại.
3. Bấm **Cài dịch vụ nền**, nhập mật khẩu máy.

Chi tiết: [macOS](09-macos.md).

## Dùng

1. Mở app: app tự chạy **Kiểm tra** và cho biết Steam bị chặn kiểu gì.
2. Bấm **Áp dụng đề xuất** (hoặc tự bật **Định tuyến DNS**, và **Proxy SNI** nếu được đề xuất).
3. Mở Steam.

Nút **X** chỉ thu app xuống khay hệ thống (góc phải thanh taskbar); LunaDNS vẫn chạy. Không dùng nữa: chuột phải biểu tượng khay → **Thoát & khôi phục**.

Có bản mới thì app hiện dải báo kèm nút **Tải**. Tải Setup bản mới rồi chạy, không cần gỡ bản cũ.

## Câu hỏi hay gặp

**Tải file zip về mà không cài được?**
File zip là bản portable, không có trình cài. Giải nén cả thư mục rồi bấm đúp `LunaDNS.exe` (xem trên). Muốn cài thật thì tải `LunaDNS-Setup-x.y.z.exe`. Nhớ kiểm lại là bạn không tải nhầm `Source code (zip)`.

**Windows chặn hẳn, không có nút "Run anyway"?**
Windows 11 bật Smart App Control sẽ chặn hẳn exe chưa ký số. Xem [Cài đặt](01-cai-dat.md#smart-app-control-windows-11).

**Không thấy bản nào ở trang Releases?**
Bản phát hành mới đang ở dạng nháp thì chỉ người làm app thấy. Đợi bản được phát hành, hoặc hỏi người gửi link.

**Đã bật mà Kiểm tra vẫn báo "Bị lọc SNI"?**
Bình thường: Kiểm tra đo đường mạng gốc, không đi qua LunaDNS. Cứ mở Steam thử.

**App bị tắt ngang (mất điện, kill), giờ Steam không vào được?**
Mở lại LunaDNS: app tự hoàn tác phần còn sót. Bản Setup thì khởi động lại máy cũng được. Xem [Gỡ và dọn](07-go-va-don-dep.md).

**Có an toàn không?**
App không giải mã kết nối, không cài chứng chỉ, chỉ xử lý tên miền Steam. Mã nguồn mở ở repo này. Kiểm file tải về bằng `SHA256SUMS.txt` ([cách làm](01-cai-dat.md#kiểm-tra-file-tải-về-sha-256)).

**Gỡ thế nào?**
Bản Setup: Settings → Apps → LunaDNS → Uninstall. Bản portable: thoát app bằng "Thoát & khôi phục" rồi xoá thư mục. Chi tiết: [Gỡ và dọn](07-go-va-don-dep.md).

Vẫn lỗi? Xem [Xử lý sự cố](08-su-co.md), hoặc mở [Issue](https://github.com/lunavo/LunaDNS/issues) kèm báo cáo từ nút **Chẩn đoán**.

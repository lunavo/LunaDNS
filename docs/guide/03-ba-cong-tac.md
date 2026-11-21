# 3. Các công tắc

<sub>[English](en/03-switches.md)</sub>

Có hai công tắc: Định tuyến DNS và Proxy SNI. Mỗi công tắc bật/tắt độc lập. Công tắc hiện *"Đang tắt"* hoặc *"Đang bật"* (Proxy SNI hiện thêm cổng và số kết nối: *"Đang bật · cổng 18080 · 33 kết nối"*).

Mọi thay đổi hệ thống được **ghi vào journal trước khi làm**, và hoàn tác theo thứ tự ngược (LIFO). App chỉ động tới thứ do chính nó tạo.

## Bốn khu của cửa sổ

Cửa sổ chính chia theo việc bạn muốn làm, từ trên xuống:

| Khu | Có gì | Xem |
|---|---|---|
| **Kiểm tra** | Kết quả kiểm theo nhóm, **Kiểm tra**, **Áp dụng đề xuất**, **Tìm cấu hình hợp mạng**, **Chẩn đoán**; ngay dưới là mục **Nâng cao** (chỉ Windows) | [Kiểm tra và đề xuất](04-kiem-tra-va-de-xuat.md), [Nâng cao](05-nang-cao.md) |
| **Trạng thái** | Hai công tắc và mục **TÊN MIỀN** | trang này |
| **Cập nhật** | Dòng *"Đang dùng LunaDNS x.y.z"*, **Kiểm tra cập nhật**, **Tự cập nhật danh sách tên miền** | [Cài đặt](01-cai-dat.md#nâng-cấp), [Tự cập nhật danh sách](#tự-cập-nhật-danh-sách) |
| **Gỡ & dọn** | **Tắt hết & khôi phục**, **Gỡ khỏi máy** (bản Setup) hoặc dòng chỉ cách gỡ (bản portable, macOS) | [Tắt](#tắt), [Gỡ và dọn](07-go-va-don-dep.md) |

Các dải báo (thông báo, có bản mới, dịch vụ nền macOS) luôn hiện ở đầu cửa sổ.

## Định tuyến DNS

- Dựng DoH stub tại `127.0.0.1:53`, hỏi Cloudflare (`1.1.1.1`) rồi Google (`8.8.8.8`) làm dự phòng; đổi thứ tự ở [Nâng cao](05-nang-cao.md). Kết nối DoH đi thẳng tới IP, không hỏi DNS hệ thống.
- Thêm **một luật NRPT** tên hiển thị "LunaDNS", Comment bắt đầu bằng `LunaDNS `, gồm các tên miền nhóm **Steam** (`steampowered.com`, `steamcommunity.com`, `steamstatic.com`, `steam-chat.com`, `steamusercontent.com`, `steamgames.com`, `steamcontent.com`, `steamserver.net`, `counter-strike.net`, `dota2.com`, `valvesoftware.com`; mỗi tên có cả dạng domain con), trỏ về `127.0.0.1`.
- Luật của phần mềm khác (VPN, Tailscale…) không bị sửa hay xoá. Nếu một phần mềm khác đã định tuyến DNS cho chính các tên miền này, app không bật và báo *"Một phần mềm khác (VPN, Tailscale…) đang định tuyến DNS cho chính các tên miền này."*
- Lỗi cổng 53 bận: *"Cổng DNS 53 trên 127.0.0.1 đang bị chương trình khác dùng."*

## Proxy SNI

- Mở proxy HTTP CONNECT ở `127.0.0.1`, cổng đầu tiên trống trong **18080–18090**. Proxy chỉ nhận CONNECT tới tên miền Steam ở cổng 443 và phục vụ `/proxy.pac`.
- Đặt `AutoConfigURL` (Internet Settings của người dùng đăng nhập) thành `http://127.0.0.1:<cổng>/proxy.pac`. Chỉ tên miền Steam đi qua proxy.
- Cắt gói TLS ClientHello theo điểm cắt chọn ở Nâng cao (mặc định **sau byte đầu tiên**).
- Máy đã có PAC hoặc proxy thủ công: app hỏi *"Thay cấu hình proxy hiện có?"*. Đồng ý thì cấu hình cũ được trả lại khi tắt Proxy SNI hoặc thoát. Huỷ thì công tắc không đổi. `AutoConfigURL` ở dạng lạ thì app không ghi đè.
- Watchdog: proxy chết sẽ được mở lại đúng cổng cũ; lỗi quá 3 lần trong 1 phút thì tính năng tự tắt và báo.
- Hết cổng: *"Không còn cổng trống cho Proxy SNI."*

## Tên miền

Mục **TÊN MIỀN** liệt kê các nhóm tên miền mà hai công tắc định tuyến. Mỗi nhóm hiện số tên miền; **Xem** mở danh sách kèm cột nguồn (*"Có sẵn"* hoặc *"Bản cập nhật dd/MM"*). Danh sách có ba lớp:

1. **Có sẵn:** đóng kèm app, luôn có.
2. **Bản cập nhật:** tên miền bổ sung do LunaDNS phát hành, có chữ ký số. App chỉ nhận bản có chữ ký đúng; lớp này chỉ **thêm** tên miền vào nhóm có sẵn, không xoá gì. Nhóm có thêm tên miền thì hiện *"N tên miền (M từ bản cập nhật)"*.
3. **Tuỳ chỉnh:** tên miền bạn tự thêm. Bản cập nhật **không bao giờ ghi đè** lớp này. Tên miền đã có trong một nhóm đang bật thì hiện ghi chú xám *"Cũng có trong nhóm …"*.

### Tự cập nhật danh sách

Dòng **Tự cập nhật danh sách tên miền** nằm ở khu **Cập nhật**, có công tắc và nút **Cập nhật ngay**. Khi bật, app kiểm bản mới 30 giây sau khi mở rồi mỗi 24 giờ (lỗi mạng thì thử lại sau 1 giờ). Bản mới được áp vào các công tắc đang bật trước, áp xong mới lưu. Tắt công tắc thì app quay về danh sách có sẵn và gỡ tên miền lớp 2 khỏi luật đang chạy.

| Dòng trạng thái | Nghĩa |
|---|---|
| *Bản dd/MM · kiểm lúc HH:mm* | Đang dùng bản cập nhật phát hành ngày dd/MM. |
| *Chưa tải bản nào* | Chưa có bản cập nhật nào hợp lệ; đang dùng danh sách có sẵn. |
| *Đang dùng danh sách có sẵn* | Công tắc đang tắt. |
| *Đã là bản mới nhất* / *Đã thêm N tên miền* | Kết quả của lần bấm **Cập nhật ngay**. |
| *Không tải được, thử lại sau* | Lỗi mạng; app tự thử lại. |
| *Không kiểm được lúc HH:mm, giữ bản dd/MM* | Lần kiểm lỗi; vẫn giữ bản đã có (hoặc *"dùng danh sách có sẵn"* nếu chưa có). |
| *Chữ ký không hợp lệ, giữ bản cũ* | File tải về hỏng, sai chữ ký hoặc ký bằng khoá lạ; bị bỏ qua. |
| *Cần bản LunaDNS mới hơn để đọc danh sách này* | Bản danh sách dùng định dạng mà bản app này chưa đọc được. |
| *Không lưu được cài đặt, giữ như cũ* | Bật/tắt công tắc nhưng không ghi được settings.json; app trả cấu hình về như trước. |
| *Không áp được bản dd/MM, xem Chẩn đoán* | Áp vào công tắc đang bật bị lỗi; app giữ cấu hình cũ. **Chẩn đoán** có khối "Danh sách tên miền cập nhật". |

## Chế độ PUBG: chưa hỗ trợ

LunaDNS hiện **không hỗ trợ PUBG**; giao diện không có công tắc hay ô kiểm tra nào cho PUBG. Nếu file hosts còn dòng `# LunaDNS` từ bản thử nghiệm cũ, "Tắt hết & khôi phục" hoặc `--cleanup` sẽ hoàn tác.

## Tắt

- Gạt công tắc tắt: hoàn tác phần của tính năng đó. Nếu hoàn tác lỗi, dịch vụ **vẫn chạy**, mục giữ lại trong journal, thông báo *"Một số thay đổi chưa hoàn tác được…"*, bấm tắt lại để thử tiếp.
- **Tắt hết & khôi phục** (khu **Gỡ & dọn**, hoặc menu khay): tắt mọi công tắc và trả cấu hình mạng như trước (*"Đã tắt hết và trả lại cấu hình mạng như trước."*).
- Tính năng gặp lỗi lặp lại sẽ tự dừng và báo *"… gặp lỗi lặp lại nên đã được tắt."*

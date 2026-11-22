# B3 + F4 + B9: DoH chọn máy khoẻ, nối IP so le (thiết kế)

Ngày: 22/11/2026. Nguồn: review đối kháng sau v0.10.0, mục B3 (lỗi, mức Vừa), F4 (tính năng) và B9 (lỗi, mức Thấp).

## Vấn đề

- `DohClient.QueryAsync` luôn thử máy theo đúng thứ tự cấu hình, mỗi máy chờ tối đa 3 giây, và không nhớ máy nào vừa lỗi.
  Nhà mạng chặn ngầm 1.1.1.1:443 (gói bị bỏ, không có RST) thì **mỗi** tên miền chưa có trong cache chậm thêm khoảng
  3 giây trước khi sang Google. Steam tải hàng chục host, nên trang Store tải rất lâu.
- Cùng một lớp `DohClient` phục vụ stub DNS (`DnsQueryHandler`) và bộ phân giải của proxy (`DohHostResolver`).
- Người dùng không thấy máy DoH nào đang trả lời, nên không hiểu vì sao chậm.
- `TcpUpstreamConnector` thử tối đa 3 IP nhưng chung một ngân sách 5 giây của `SplitProxy`/`SniForwarder`.
  IP đầu bị chặn ngầm là hết giờ, trả 502, dù IP thứ hai vào được.

## Quyết định

1. **Chạy đua so le (`StaggeredRace`, Core, internal).** Một hàm dùng chung cho DoH và TCP:
   - Bắt đầu lượt 0. Lượt đang chạy quá `stagger` mà chưa xong thì mở lượt kế, **lượt cũ vẫn chạy tiếp**.
     Lượt nào lỗi (lỗi "dự kiến" do bên gọi định nghĩa) thì mở lượt kế ngay, không chờ.
   - Lượt thành công đầu tiên thắng; các lượt còn lại bị huỷ, kết quả đến muộn được bỏ đi (`discard`, ví dụ đóng socket).
   - Mọi lượt đều lỗi: trả danh sách lỗi theo thứ tự xảy ra, để bên gọi gói thành exception của mình.
   - Bên gọi huỷ (token của bên gọi): `OperationCanceledException` đi thẳng ra. Lỗi "không dự kiến" (lỗi lập trình):
     huỷ các lượt khác rồi ném lại nguyên exception đó.
   - Hẹn giờ qua `TimeProvider`, để test dùng `FakeTimeProvider`, không ngủ thật.
2. **DoH: thứ tự theo sức khoẻ, nhớ trong `Cooldown` = 5 phút.**
   - Mỗi máy có mốc `downUntil`. Một lượt của máy đó lỗi (HTTP lỗi, sai Content-Type, trả lời không khớp, hết 3 giây,
     IO) thì `downUntil = now + Cooldown`.
   - Máy thắng xoá `downUntil` của nó. Các máy **được mở trước máy thắng** mà chưa xong cũng bị đánh dấu như lỗi
     (chậm hơn cả máy mở sau cộng thêm `HedgeDelay`, nên coi là đang bị chặn hoặc nghẽn). Máy mở sau máy thắng thì
     không bị đánh dấu.
   - Thứ tự cho mỗi truy vấn: máy khoẻ trước, theo thứ tự cấu hình; máy đang "nghỉ" xếp sau, cũng theo thứ tự cấu hình.
     Hết `Cooldown` thì máy chính tự lên đầu lại (về máy chính sau thời gian nghỉ). Không bao giờ bỏ hẳn một máy.
   - Truy vấn chạy đua so le với `HedgeDelay` = 500 ms. Mỗi lượt vẫn có timeout riêng 3 giây tính từ lúc nó mở.
   - Kết quả khi 1.1.1.1 và 1.0.0.1 bị chặn ngầm: những truy vấn đầu tiên chậm khoảng 0,5 giây thay vì 3 giây;
     sau khi Google thắng một lần, các truy vấn sau đi thẳng Google. Mỗi 5 phút máy chính được thử lại, mất thêm
     khoảng 0,5 giây cho một lượt truy vấn nếu nó vẫn bị chặn.
   - Xấu nhất (mọi máy chết): 2 × 0,5 + 3 = 4 giây với chuỗi 3 máy, thay vì 9 giây.
   - Log: một dòng Information mỗi khi máy đang trả lời đổi ("DoH đang trả lời: Google"), dòng Warning cũ khi một máy lỗi.
   - `DohClient` có thêm `Activity` (`DohActivity(string Server, bool Fallback)`, null khi chưa có câu trả lời nào):
     máy trả lời gần nhất, `Fallback` = không phải máy đầu của chuỗi cấu hình.
   - Client một máy (`DohLatencyMeter`, ProbeRunner đo từng máy) không đổi hành vi.
3. **Hiện trên thẻ Định tuyến DNS.**
   - `IRunningService` có thêm `DohActivity? Doh` (mặc định null). Stub DNS trả `Activity` của `DohClient` của nó.
   - `FeatureStatus` thêm `DohActivity? Doh = null` (trường tuỳ chọn ở cuối); `FeatureSet.GetStatus` lấy từ dịch vụ DNS.
   - Windows: `LocalFeatureController.Status` dùng `FeatureSet.GetStatus`, nên có luôn.
   - macOS: `DaemonStatus` thêm `DohServer` (chuỗi) và `DohFallback` (bool), đều tuỳ chọn, bỏ khi null.
     Daemon cũ không gửi hai trường này: giao diện coi như chưa biết. Giao diện cũ gặp trường lạ thì bỏ qua (mặc định
     của System.Text.Json). Không thêm lệnh mới, nên không có đường "Lệnh lạ:".
   - Dòng trạng thái của thẻ khi DNS bật: "Đang bật · DoH: Cloudflare", hoặc "Đang bật · DoH: Google (dự phòng)".
     Chưa có câu trả lời nào: "Đang bật" như cũ. Dòng này cập nhật theo poll trạng thái sẵn có.
   - `lunadns status` in thêm dòng "DoH đang trả lời: Google (dự phòng)" khi có.
   - Chỉ hiện DoH của stub DNS. Proxy SNI có `DohClient` riêng, không hiện (ngoài phạm vi).
4. **B9: nối IP so le.** `TcpUpstreamConnector` dùng `StaggeredRace` với `AttemptDelay` = 1 giây trên tối đa 3 IP:
   IP đầu chưa nối xong sau 1 giây thì mở IP kế song song; `SocketException` thì sang IP kế ngay. Socket thắng muộn
   bị đóng. Cả cuộc đua vẫn nằm trong ngân sách 5 giây của bên gọi (huỷ theo token của bên gọi). IP đầu bị chặn ngầm,
   IP hai sống: nối xong sau khoảng 1 giây thay vì 502. Mọi IP lỗi: `IOException` như cũ, `InnerException` là
   `SocketException` cuối.

## Ngoài phạm vi

- `ConnectToBootstrapAsync` của DoH vẫn thử hai IP bootstrap tuần tự trong `ConnectTimeout` 3 giây; chạy đua giữa các máy
  DoH đã che phần lớn trường hợp này.
- Lưu sức khoẻ DoH qua lần khởi động lại dịch vụ, hoặc cho người dùng chỉnh `HedgeDelay`/`Cooldown`.
- Hiện máy DoH của proxy.

## Kiểm

- Test đơn vị với `FakeTimeProvider` cho `StaggeredRace`, `DohClient` (so le, nhớ máy, về máy chính sau nghỉ,
  không đánh dấu máy mở sau), `TcpUpstreamConnector` (IP đầu treo), `FeatureSet.GetStatus`, `DaemonCommands` status,
  tương thích JSON thiếu trường, `DaemonFeatureController`, dòng trạng thái của thẻ.
- Máy thật (macOS): `pfctl` chặn 1.1.1.1 và 1.0.0.1, bật Định tuyến DNS, hỏi các tên miền Steam mới bằng
  `dscacheutil`: phải nhanh (dưới 1 giây sau lần đầu), thẻ hiện "DoH: Google (dự phòng)". Gỡ luật `pfctl` sau khi kiểm.

## Kết quả kiểm máy thật (22/11/2026)

Dịch vụ nền đang dùng Quad9 làm máy chính (do "Tìm cấu hình hợp mạng" chọn), nên luật `pfctl` tạm chặn cả Cloudflare
(1.1.1.1, 1.0.0.1) và Quad9 (9.9.9.9, 149.112.112.112). Hỏi `dig @127.0.0.1 -p 15353 <tên mới>.steampowered.com`:

- Dịch vụ nền cũ: 6 tên mới, mỗi tên 6,25–6,52 giây.
- Dịch vụ nền mới: 6 tên mới, mỗi tên 0,10–0,17 giây; `dscacheutil` 0,33 giây.
- `lunadns status` và thẻ trong app: "DoH đang trả lời: Google (dự phòng)" / "Đang bật · DoH: Google (dự phòng)".

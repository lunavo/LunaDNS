# B5 + F7: Journal hỏng không được để sót thay đổi của LunaDNS (thiết kế)

Ngày: 22/11/2026. Nguồn: review đối kháng sau v0.10.0, mục B5 (lỗi, mức Vừa) và F7 (tính năng).
Thay cho KH4 quyết định 11 và KH5 quyết định 10 ("không dọn luật NRPT mồ côi").

## Vấn đề

- `JsonFileJournalStore.Load` gặp `journal.json` hỏng (JSON dở do `kill -9` lúc đang ghi, rác) thì đổi tên sang
  `.corrupt-<giờ>` và trả journal rỗng. Mọi thay đổi trong file đó không còn ai hoàn tác:
  - macOS: file `/etc/resolver/<tên miền>` mang dấu `# LunaDNS <id>` trỏ mãi về `127.0.0.1:15353` đã chết, nên tên miền
    Steam không phân giải được; PAC `http://127.0.0.1:1808x/proxy.pac` còn bật trên các network service.
  - Windows: luật NRPT có Comment `LunaDNS …` và dòng hosts có `# LunaDNS`.
- Lỗi đọc file không phải JSON (`IOException`, `UnauthorizedAccessException`) thì ném thẳng ra: dịch vụ nền macOS
  (launchd KeepAlive) khởi động lại liên tục, app Windows không mở được.
- Cùng hậu quả khi `journal.json` bị xoá tay.

## Quyết định

1. **`Load` không bao giờ ném.** Lỗi JSON, lỗi IO, lỗi quyền, nội dung `null` hoặc có mục `null`/thiếu `Feature`/`Op`
   đều coi là "đọc không được": ghi log mức Error, thử cách ly file sang `journal.json.corrupt-<yyyyMMddHHmmss>`
   (trùng tên thì thêm `-1`, `-2`…), rồi trả journal rỗng. Cách ly lỗi thì chỉ ghi log và đi tiếp.
2. **Quét dọn dấu vết mồ côi** (`OrphanSweeper`, Core) chạy mỗi lần khởi động, **luôn luôn** (không chỉ khi `Load` lỗi,
   vì journal có thể bị xoá): ngay sau `RollbackAll`, khi đang giữ khoá một-bản-chạy, **trước** khi F1 tự bật lại.
   - macOS: `Engine` (daemon và `lunadns run`), trong ctor.
   - Windows: `LocalFeatureController` (ctor, app đang giữ mutex) và `CleanupCommand.RollbackJournal`
     (`--cleanup`, tác vụ dọn lúc khởi động máy, gỡ cài đặt).
   Lúc đó chưa tính năng nào chạy, nên không thể gỡ nhầm thứ của tính năng đang bật hay sắp tự bật.
3. **Phạm vi phủ (coverage)** = các mục **còn lại** trong journal sau `RollbackAll` (mục hoàn tác lỗi, giữ để thử lại).
   Dấu vết thuộc các mục này không bị quét. Mục có `Op` đã biết mà snapshot không đọc được thì **bỏ qua cả loại đó**
   (không biết mục ấy giữ gì). `Op` lạ (bản mới hơn ghi) không ảnh hưởng.
4. **Chỉ đụng dấu vết mang đúng dấu LunaDNS, không bao giờ đụng mục lạ:**

   | Loại | Mồ côi khi | Xử lý |
   |---|---|---|
   | `/etc/resolver/*` (macOS) | file thường (không phải symlink), dòng đầu `# LunaDNS <id>` với `<id>` không rỗng, dấu không thuộc mục nào | xoá file |
   | PAC (macOS) | network service (cả service đang tắt) bật auto-proxy với URL **đúng** `http://127.0.0.1:<18080–18090>/proxy.pac`, URL không thuộc mục nào | `-setautoproxystate <service> off` |
   | hosts (cả hai) | dòng đúng dạng LunaDNS ghi: `<IP loopback> <host> # LunaDNS`, không thuộc mục nào | gỡ dòng, phần còn lại giữ nguyên từng byte |
   | NRPT (Windows) | Comment bắt đầu `LunaDNS ` và có phần sau, Comment không thuộc mục nào | xoá luật |

   - **PAC trả về "tắt"**: URL cũ của người dùng (nếu có) nằm trong journal đã mất nên không biết; tắt auto-proxy là
     trạng thái an toàn (máy đi thẳng). URL của LunaDNS nằm lại ở trạng thái tắt, như khi hoàn tác service vốn chưa có URL.
   - **PAC trên Windows không quét**: AutoConfigURL nằm trong HKU của từng người dùng, tác vụ dọn chạy bằng SYSTEM nên
     không biết người nào. WinINet gặp PAC không tải được thì đi thẳng. Để việc sau.
   - File hosts UTF-16 thì bỏ qua (LunaDNS không bao giờ ghi vào loại file đó).
5. **Lỗi quét không chặn khởi động.** Mỗi loại chạy riêng; loại nào lỗi thì ghi log và các loại khác vẫn chạy. Kết quả
   quét không ảnh hưởng quyết định tự bật lại của F1 (bật lại thì Apply tự ghi đè file/luật mang dấu LunaDNS).
   `--cleanup` trả mã 1 nếu quét có lỗi, như khi hoàn tác có lỗi.
6. Quét có gỡ resolver/NRPT/hosts thì xả cache DNS một lần.
7. **Định dạng journal, tên `Op` và tên tính năng không đổi.** Mục cũ trên máy người dùng vẫn hoàn tác được.

## Ngoài phạm vi

- Quét PAC trên Windows (mục 4).
- Báo kết quả quét lên giao diện: chỉ ghi log (Chẩn đoán đã đính kèm log).

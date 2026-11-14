# LunaDNS KH6: Cải thiện UI/UX và bộ icon

> **Trạng thái: PO ĐÃ DUYỆT ngày 13/11/2026, D1–D7 chốt theo cột "Đề xuất".** Bước chi tiết (code + lệnh verify) viết ngày 14/11/2026 ở **mục 7**, chờ PO duyệt rồi mới thực thi.
>
> **14/11/2026: PO cho làm KH6 khi KH1–KH5 chưa commit** (bỏ điều kiện commit bên dưới). Vì vậy các bước không có `git commit`; muốn xem riêng phần KH6 thì so với danh sách file ở mục 7.0.
>
> **Điều kiện bắt đầu:** KH5 T8 (PO test tay) xong và KH1–KH5 đã commit. KH6 sửa đúng các file T8 đang kiểm (`MainWindow.axaml`, `MainViewModel.cs`, `Strings.resx`, `App.axaml.cs`), nên không chạy song song.
>
> **Cập nhật 13/11/2026 (PO chốt sau T8, ghi ở cuối plan KH5, "Xem lại quyết định sau T8", quyết định 3): PUBG ra khỏi phạm vi v1.** Giao diện không còn công tắc Chế độ PUBG, ô "Kiểm tra cả PUBG", chữ "PUBG" ở tiêu đề phụ, và không có icon PUBG. Code Core (`FeatureDomains.PubgHosts`, Prober nhóm PUBG, `Feature.PubgMode`) giữ nguyên, chỉ ẩn trên giao diện. Việc ẩn là task U0.

**Mục tiêu:** người dùng nhìn vào cửa sổ hoặc icon khay là biết ngay ba điều: mạng có bị chặn không, LunaDNS đang làm gì, và có lỗi nào cần xử lý không.

**Phạm vi:** `src/LunaDns.App/` (XAML, ViewModel, resx, Assets) và một phần nhỏ ở `src/LunaDns.Core/Features/ProbeSummary.cs` cho UX-1. Không đổi hành vi mạng, không thêm package NuGet.

---

## 1. Đánh giá giao diện hiện tại

Đọc từ `Views/MainWindow.axaml` (90 dòng), `App.axaml`, `ViewModels/*`, `Strings.resx`, `Assets/make-icon.ps1`.

| # | Vấn đề | Chỗ trong code | Mức |
|---|---|---|---|
| P1 | Không có trạng thái tổng. Muốn biết app đang bật gì phải đọc cả ba thẻ. | `MainWindow.axaml` phần header chỉ có tên + mô tả | Cao |
| P2 | **UX-1:** sau khi bật biện pháp, Kiểm tra vẫn ghi `Steam: Bị lọc SNI`, người dùng tưởng app không có tác dụng. | `MainViewModel.ShowSummary` dòng 201–203 chỉ in `group.Worst` | Cao |
| P3 | Kết quả kiểm tra là các dòng chữ trơn, cùng màu. "Bình thường" và "Không vào được" trông giống nhau. | `ProbeLines` là `ObservableCollection<string>` | Cao |
| P4 | Thông báo (`Message`) là một `TextBlock` không phân mức. Lỗi "chưa hoàn tác hết" trông y như "đã lưu báo cáo", lại nằm giữa trang nên dễ sót. | `MainWindow.axaml` dòng 59 | Cao |
| P5 | Thẻ tính năng không có icon, không có trạng thái lỗi riêng, không có trạng thái "đang xử lý". Toggle tự lật ngay khi bấm rồi mới được đẩy lại giá trị thật. | `FeatureViewModel`, DataTemplate | Vừa |
| P6 | Icon khay chỉ có một hình, tooltip cố định "LunaDNS đang chạy". Không phân biệt đang bật / đang tắt / lỗi. | `App.axaml.cs` `CreateTray` | Vừa |
| P7 | `lunadns.ico` chỉ chứa một khung 256×256. Ở khay (16–24 px) Windows tự thu nhỏ nên trăng lưỡi liềm bị nhoè. | `Assets/make-icon.ps1` | Vừa |
| P8 | Không có màu thương hiệu: dùng `FluentTheme` mặc định, màu nhấn lấy theo Windows. Theme đã đi theo hệ thống (`RequestedThemeVariant="Default"`) nhưng không có cách chọn tay. | `App.axaml` | Vừa |
| P9 | Mục "Cài đặt" là một Expander chỉ chứa đúng một nút "Chẩn đoán". | `MainWindow.axaml` dòng 82–84 | Thấp |
| P10 | ~~Hàng nút + checkbox PUBG có nguy cơ bị cắt ở 460 px.~~ Hết sau khi bỏ checkbox PUBG (U0). | — | — |
| P11 | "Tắt hết & khôi phục" luôn bấm được kể cả khi không có gì đang bật. | nút dòng 61 | Thấp |
| P12 | Thuật ngữ khó: "Proxy SNI", "Định tuyến DNS", "Điểm cắt gói". Mô tả có giải thích nhưng tiêu đề thì không. | `Strings.resx` | Thấp |

Điểm đang tốt, giữ nguyên: một cột cuộn được, mỗi công tắc có mô tả một dòng, chuỗi tập trung ở resx.

## 2. Về infographic PO gửi và các tool tham chiếu

**Infographic (nền kem, khối đỏ đậm, số 01–06, icon tròn nét mảnh).** Lấy được ba thứ:

- **Icon tròn nét mảnh trên nền đặc**: dùng làm huy hiệu icon cho từng thẻ tính năng. Đây là thứ đáng lấy nhất.
- **Nền kem ấm**: dùng cho theme sáng thay cho trắng tinh.
- **Mỗi khối một ý, tiêu đề đậm + một dòng phụ**: thẻ hiện tại đã theo đúng kiểu này.

Không nên lấy:

- **Đỏ đậm làm màu chính.** Trong app mạng, đỏ nghĩa là "bị chặn / lỗi". Nếu công tắc đang bật cũng đỏ thì người dùng không phân biệt được "đang hoạt động" với "đang hỏng", và icon khay đỏ sẽ bị hiểu là có sự cố.
- **Bố cục hai cột đối xứng, số 01–06.** Đó là bố cục trình bày sáu ý ngang hàng. App chỉ có hai công tắc và một luồng chính, đánh số sẽ gợi ý sai rằng phải bật theo thứ tự.

**Các tool tương tự** (nhận xét chung về hướng thiết kế, không chép giao diện):

| Tool | Điểm học được | Điểm tránh |
|---|---|---|
| Cloudflare WARP / 1.1.1.1 | Một trạng thái lớn ở trên cùng, một câu nói rõ "đang bảo vệ" hay không. Icon khay đổi theo trạng thái. | Giấu hết chi tiết; LunaDNS cần cho thấy từng biện pháp vì chúng sửa cấu hình máy. |
| NextDNS | Tối giản, chữ rõ, ít màu. | — |
| Simple DNSCrypt | Thẻ có toggle + mô tả cho từng dịch vụ, mục nâng cao tách riêng. | Nhiều tab; LunaDNS ít chức năng, một trang là đủ. |
| YogaDNS | Hiện log/kết quả theo từng tên miền cho người rành. | Bảng kỹ thuật dày đặc ở màn hình chính. |
| GoodbyeDPI GUI | Nút bật/tắt lớn + preset; người dùng không cần hiểu tham số. | Không báo biện pháp có tác dụng hay không. |

Kết luận: **trạng thái tổng ở trên, một hành động chính (Kiểm tra → Áp dụng đề xuất), hai thẻ có icon và màu trạng thái, chi tiết kỹ thuật giấu trong Nâng cao.**

## 3. Đề xuất thiết kế

### 3.1 Bố cục (vẫn một cột, 560 px)

```
┌──────────────────────────────────────────────┐
│ (☾) LunaDNS                  [● Đang bật 2/2] │  header + huy hiệu trạng thái tổng
│     Sửa lỗi vào Steam khi nhà mạng chặn…      │
├──────────────────────────────────────────────┤
│ ⚠ Chưa hoàn tác hết thay đổi…           [×]  │  thanh thông báo theo mức (chỉ hiện khi có)
├──────────────────────────────────────────────┤
│ KIỂM TRA MẠNG              lúc 14:02:11       │
│ ✓ Steam   Bị lọc SNI · đang né bằng Proxy SNI │  mỗi nhóm một dòng: icon + màu + hậu tố
│ [Kiểm tra]  [Áp dụng đề xuất]                 │
├──────────────────────────────────────────────┤
│ (◎) Định tuyến DNS                     [━●]  │  huy hiệu icon tròn, viền trái theo trạng thái
│     Hỏi tên miền Steam qua DNS mã hoá…        │
│     ● Đang bật                                │
│ (✂) Proxy SNI                          [━●]  │
│     ● Đang bật · cổng 18080 · 3 kết nối       │
├──────────────────────────────────────────────┤
│ [↶ Tắt hết & khôi phục]        Chẩn đoán ↗   │  nút khôi phục mờ khi không có gì bật
│ ▸ Nâng cao                                    │
└──────────────────────────────────────────────┘
```

Thay đổi so với hiện tại: thêm huy hiệu trạng thái tổng; thông báo chuyển lên ngay dưới header; dòng kết quả có icon/màu/hậu tố; thẻ có huy hiệu icon; bỏ Expander "Cài đặt" (nút Chẩn đoán xuống chân trang); bỏ mọi thứ về PUBG (U0).

### 3.2 Trạng thái

**Trạng thái tổng** (header, icon khay, tooltip dùng chung một nguồn):

| Trạng thái | Điều kiện | Chữ | Màu / hình |
|---|---|---|---|
| Tắt | không công tắc nào bật | "Đang tắt" | xám, chấm rỗng |
| Bật | ≥1 công tắc bật, không lỗi | "Đang bật N/2" | xanh lá, chấm đặc |
| Đang xử lý | đang bật/tắt/kiểm tra | "Đang xử lý…" | màu nhấn, vòng quay |
| Lỗi | watchdog vừa tắt một tính năng, hoặc còn thay đổi chưa hoàn tác | "Cần xử lý" | đỏ, dấu ! |

**Trạng thái thẻ:** Tắt / Đang bật / Đang xử lý (toggle bị khoá, có vòng quay) / Lỗi (dòng trạng thái đỏ, ghi lý do ngắn từ `ErrorMessages`).

**Dòng kết quả kiểm tra (gồm sửa UX-1):**

| Kết luận Prober | Biện pháp đang bật | Hiển thị | Icon/màu |
|---|---|---|---|
| Bình thường | — | "Bình thường" | ✓ xanh lá |
| DNS bị chặn | chưa | "DNS bị chặn" | ! hổ phách |
| DNS bị chặn | Định tuyến DNS | "DNS bị chặn · đang né bằng Định tuyến DNS" | ✓ xanh lá |
| Bị lọc SNI | chưa đủ | "Bị lọc SNI · còn thiếu Proxy SNI" | ! hổ phách |
| Bị lọc SNI | DNS + Proxy | "Bị lọc SNI · đang né bằng Proxy SNI" | ✓ xanh lá |
| Không vào được | bất kỳ | "Không vào được" | ✕ đỏ |

Trạng thái không chỉ dựa vào màu: mỗi mức có hình icon riêng (✓ ! ✕ ○) để người mù màu vẫn phân biệt được.

Lưu ý về câu chữ (quyết định D4): Prober đo đường mạng gốc, không đo qua biện pháp. Vì vậy app chỉ **biết** biện pháp đang bật, chưa **đo** được là đã vào được. "Đang né bằng …" đúng với những gì app biết; "đã khắc phục bằng …" là lời khẳng định mà app chưa kiểm chứng.

### 3.3 Màu và theme

Giữ `FluentTheme`, thêm một `ResourceDictionary` có `ThemeDictionaries` Light/Dark. Màu nhấn lấy từ icon hiện tại (xanh `#2563EB`).

| Token | Sáng | Tối |
|---|---|---|
| Nền cửa sổ | `#FAF7F2` (kem, lấy ý từ infographic) | `#0F172A` |
| Nền thẻ | `#FFFFFF` | `#1E293B` |
| Viền thẻ | `#E7E0D6` | `#334155` |
| Chữ chính | `#1F2937` | `#F1F5F9` |
| Chữ phụ | `#6B7280` | `#94A3B8` |
| Nhấn (nút chính, toggle bật) | `#2563EB` | `#3B82F6` |
| Tốt | `#15803D` | `#22C55E` |
| Cảnh báo | `#B45309` | `#F59E0B` |
| Lỗi | `#B91C1C` | `#EF4444` |
| Tắt/trung tính | `#6B7280` | `#64748B` |

Các mã màu trên là đề xuất, **chưa đo độ tương phản**; task U3 có bước đo (chữ ≥ 4,5:1, icon/viền ≥ 3:1) và chỉnh nếu trượt.

Theme mặc định vẫn theo Windows. Có thêm lựa chọn tay hay không: quyết định D3.

### 3.4 Bộ icon

**Icon app (`lunadns.ico`):** giữ trăng lưỡi liềm trên nền tròn xanh, thêm một chấm nhỏ ở lõm trăng (gợi "điểm phân giải"). File ICO chứa đủ khung 16, 20, 24, 32, 48, 256 px; khung 16–24 vẽ riêng (trăng dày hơn, bỏ chấm) thay vì thu nhỏ từ 256.

**Icon khay theo trạng thái** (4 file ICO, mỗi file 16/20/24/32):

| Trạng thái | Hình | Tooltip |
|---|---|---|
| Tắt | trăng xám, không nền | "LunaDNS: đang tắt" |
| Bật | trăng trắng trên nền tròn xanh | "LunaDNS: đang bật DNS, Proxy SNI" |
| Lỗi | như Bật + huy hiệu đỏ có dấu ! ở góc dưới phải | "LunaDNS: cần xử lý, mở để xem" |
| Đang xử lý | (tuỳ chọn) dùng lại icon Bật, không làm hoạt hình | — |

Khác nhau cả về hình (có nền / không nền / có huy hiệu), không chỉ về màu, vì khay Windows nhỏ và có người dùng theme tối/sáng khác nhau.

**Icon tính năng** (nét mảnh, đặt trong huy hiệu tròn 36 px, lấy ý từ infographic):

| Chỗ dùng | Hình |
|---|---|
| Định tuyến DNS | quả địa cầu có mũi tên rẽ |
| Proxy SNI | cây kéo (cắt gói) |
| Kiểm tra | ống nghe / radar |
| Tắt hết & khôi phục | mũi tên quay ngược |
| Nâng cao | thanh trượt |
| Chẩn đoán | bảng kẹp giấy |
| Trạng thái | ✓ tròn, ! tam giác, ✕ tròn, ○ |

Nguồn (quyết định D5): chép path data của bộ icon mã nguồn mở vào `Assets/Icons.axaml` dưới dạng `StreamGeometry`, không thêm package. Ghi giấy phép vào `docs/THIRD-PARTY-NOTICES.md`.

## 4. Quyết định (PO đã chốt cả 7 theo cột "Đề xuất" ngày 13/11/2026)

| # | Câu hỏi | Đề xuất | Phương án khác |
|---|---|---|---|
| D1 | Màu thương hiệu | **Giữ xanh `#2563EB`** (khớp icon trăng hiện có), theme sáng dùng nền kem | Đỏ rượu `#8B1A1A` như infographic; khi đó "đang bật" phải dùng xanh lá và lỗi phải dựa nhiều vào hình icon |
| D2 | Đánh số 01/02/03 trên thẻ | **Không** | Có, làm nhãn trang trí |
| D3 | Chọn theme tay (Hệ thống / Sáng / Tối) | **Chưa làm**, chỉ theo Windows; thêm sau nếu có người hỏi | Làm luôn: thêm một trường vào `settings.json` và một ComboBox trong Nâng cao |
| D4 | Câu hậu tố UX-1 | **"đang né bằng …"** (đúng với cái app biết) | "đã khắc phục bằng …" như đề bài; hoặc làm thêm phép đo qua biện pháp rồi mới ghi "đã khắc phục" (task riêng, đụng Prober) |
| D5 | Nguồn icon nét mảnh | **Fluent UI System Icons (MIT)**, hợp với FluentTheme | Lucide (ISC), nét mảnh giống infographic hơn; hoặc tự vẽ |
| D6 | Menu khay thêm mục "Tắt hết & khôi phục" | **Có** | Giữ hai mục Mở / Thoát |
| D7 | Đổi tên hiển thị cho dễ hiểu | **Giữ tên, sửa mô tả**: thêm một cụm thường ngày vào đầu mô tả | Đổi hẳn: "DNS mã hoá", "Vượt lọc SNI" (phải sửa cả tài liệu) |

## 5. Danh sách task

Thứ tự ưu tiên: U0 (ẩn PUBG) → U1–U2 (sửa lỗi UX-1) → U3–U8 (giao diện) → U9–U10 (icon) → U11 (kiểm tay). Lệnh verify chung: `dotnet build LunaDns.sln -warnaserror` và `dotnet test LunaDns.sln` phải xanh như mốc KH5 (Core 356+4, Platform 37+4, App 29) cộng số test mới.

Theo KH5 quyết định 13, ViewModel không có unit test. Vì vậy logic mới đặt ở Core hoặc ở lớp thuần trong App để test được; XAML kiểm bằng tay ở U11.

| Task | Việc | File | Xong khi |
|---|---|---|---|
| **U0** | Ẩn PUBG trên giao diện: `Features` chỉ còn Định tuyến DNS và Proxy SNI; bỏ checkbox "Kiểm tra cả PUBG" (Kiểm tra chỉ chạy nhóm Steam) và dòng gợi ý bật Chế độ PUBG; sửa `HeaderSubtitle`; gỡ các chuỗi resx PUBG không còn dùng và cập nhật test đếm chuỗi. Không đụng Core. "Tắt hết & khôi phục", thoát app và `--cleanup` vẫn hoàn tác dòng hosts PUBG nếu còn sót từ bản trước. | `MainViewModel.cs`, `MainWindow.axaml`, `Strings.resx`, `Strings.cs`, test Strings | Không còn chữ "PUBG" trên cửa sổ, hộp thoại, khay; test Core không đổi |
| **U1** | UX-1 phần Core: thêm cách hỏi "nhóm này đã được biện pháp nào che chưa" (`Chưa cần` / `Còn thiếu` / `Đang né` + danh sách biện pháp), dùng lại bảng của `ProbeAdvisor`. | `Core/Features/ProbeSummary.cs`, test tương ứng | Test phủ đủ 6 dòng bảng ở 3.2 |
| **U2** | UX-1 phần App: `ProbeLines` đổi từ `string` sang `ProbeLineViewModel` (nhóm, chữ kết luận, hậu tố, mức). Thêm chuỗi resx cho hậu tố. | `MainViewModel.cs`, `ViewModels/ProbeLineViewModel.cs` (mới), `Strings.resx`, `Strings.cs`, test Strings | Bật DNS + Proxy rồi Kiểm tra: dòng Steam có hậu tố và mức "tốt"; test đếm chuỗi resx cập nhật |
| **U3** | Bảng màu: `Styles/Theme.axaml` với `ThemeDictionaries` Light/Dark, style cho thẻ, huy hiệu, dòng trạng thái; gộp vào `App.axaml`. Đo tương phản từng cặp chữ/nền. | `Styles/Theme.axaml` (mới), `App.axaml`, `.csproj` | Đổi theme Windows thì app đổi theo; bảng tương phản ghi vào plan |
| **U4** | Bộ icon nét mảnh dạng `StreamGeometry` + control huy hiệu tròn. | `Assets/Icons.axaml` (mới), `docs/THIRD-PARTY-NOTICES.md` | 6 icon tính năng + 4 icon trạng thái hiện đúng ở cả hai theme |
| **U5** | Trạng thái tổng: lớp thuần `OverallState` tính từ `FeatureStatus` + cờ lỗi; header hiện huy hiệu. | `OverallState.cs` (mới, có test), `MainViewModel.cs`, `MainWindow.axaml`, resx | Test 4 trạng thái ở 3.2; header đổi khi bật/tắt |
| **U6** | Thẻ tính năng: huy hiệu icon, viền trái và dòng trạng thái theo màu, khoá toggle + vòng quay khi đang xử lý, dòng lỗi riêng trên thẻ. | `FeatureViewModel.cs`, `MainWindow.axaml`, resx | Bấm toggle: bị khoá tới khi xong; cổng 53 bận thì lỗi hiện ngay trên thẻ DNS |
| **U7** | Thanh thông báo theo mức (thông tin / thành công / cảnh báo / lỗi), có nút đóng, nằm dưới header. `Message` kèm mức. | `MainViewModel.cs`, `MainWindow.axaml` | "Đã lưu báo cáo" hiện kiểu thông tin; "Chưa hoàn tác hết" hiện kiểu lỗi |
| **U8** | Dọn bố cục: bỏ Expander Cài đặt, Chẩn đoán xuống chân trang; "Tắt hết & khôi phục" mờ khi không có gì bật; icon cho các nút. | `MainWindow.axaml`, `MainViewModel.cs` | Ở 460 px không có gì bị cắt |
| **U9** | Icon app nhiều khung: `make-icon.ps1` sinh ICO 16/20/24/32/48/256, khung nhỏ vẽ riêng. | `Assets/make-icon.ps1`, `Assets/lunadns.ico` | Mở ICO thấy đủ 6 khung; icon ở taskbar và Alt+Tab nét |
| **U10** | Icon khay theo trạng thái + tooltip theo trạng thái (+ mục menu theo D6). Dùng `OverallState` của U5. | `make-icon.ps1`, `Assets/tray-*.ico` (mới), `App.axaml.cs`, `.csproj`, resx | Bật một công tắc: icon khay và tooltip đổi trong 1 giây; giả lập watchdog tắt tính năng: icon lỗi |
| **U11** | Kiểm tay và sửa lặt vặt: theme sáng/tối, DPI 100/150 %, bề rộng 460, đi bằng phím Tab, Narrator đọc được tên toggle (`AutomationProperties.Name`), chế độ tương phản cao của Windows. | checklist trong plan | PO chạy hết checklist, không còn mục đỏ |

Nếu D3 = làm luôn thì thêm **U12** (chọn theme tay). Nếu D4 = đo qua biện pháp thì tách thành plan riêng vì đụng `Prober`.

## 6. Ngoài phạm vi

- Hoạt hình, hiệu ứng chuyển cảnh, cửa sổ không viền tự vẽ.
- Bản tiếng Anh (`Strings.en.resx`).
- Trang lịch sử/log trong app.
- Đổi hành vi Prober hoặc các tính năng mạng.
- Mọi thứ về PUBG trên giao diện (ra khỏi phạm vi v1); gỡ PUBG khỏi README và `docs/guide` là việc của task khác.

---

## 7. Bước thực thi chi tiết (viết 14/11/2026)

> **Cho người thực thi:** làm tuần tự theo thứ tự ở 7.0, ngay trong phiên chính (các task cùng sửa `MainWindow.axaml`, `MainViewModel.cs`, `Strings.resx`, nên không giao song song). Mỗi bước có ô `- [ ]`. **Không commit** (PO chốt 14/11).

**Mục tiêu:** cửa sổ và icon khay cho biết ngay mạng có bị chặn không, LunaDNS đang làm gì, có lỗi cần xử lý không.

**Kiến trúc:** logic mới nằm ở lớp thuần để test được: `ProbeGroupSummary.Coverage` (Core), `ProbeLineViewModel` và `OverallState` (App). Màu, icon và style nằm ở `Styles/Theme.axaml` + `Assets/Icons.axaml`. ViewModel chỉ nối các lớp này và bật class style qua `ToneConverters`.

**Công nghệ:** .NET 8, Avalonia 11.3.23 (FluentTheme), xUnit 2.5.3, Windows PowerShell 5.1 + System.Drawing để sinh ICO.

### Ràng buộc chung

- Không thêm package NuGet. Không đổi hành vi mạng. Core chỉ đổi `ProbeSummary.cs` và `FeatureStatus.cs`.
- Màu nhấn `#2563EB`. Theme theo Windows, không có chọn tay (D3). Không đánh số thẻ (D2).
- Hậu tố UX-1 dùng đúng chữ **"đang né bằng …"** / **"còn thiếu …"** (D4).
- Icon: path data Fluent UI System Icons, bản 24 regular, MIT (D5). Giấy phép ở `docs/THIRD-PARTY-NOTICES.md`.
- Menu khay có "Tắt hết & khôi phục" (D6). Giữ tên tính năng, chỉ sửa mô tả (D7).
- Không chữ "PUBG" trên cửa sổ, hộp thoại, khay (U0). Code Core về PUBG giữ nguyên.
- Mọi trạng thái có hình icon riêng, không chỉ khác màu.

**Lệnh verify chung** (PowerShell, từ `D:\Projects\lunadns`):

```powershell
$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"
dotnet build LunaDns.sln -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.Platform.Windows.Tests
dotnet test tests/LunaDns.App.Tests
```

Mốc trước KH6: Core 356 + 4 skipped, Platform 37 + 4 skipped, App 29. **Đích sau KH6: Core 364 + 4, Platform 37 + 4, App 42.**

### 7.0 Thứ tự và file

Thứ tự: **U0 → U1 → U3 → U4 → U2 → U5 → U6–U8 → U9 → U10 → U11.** U3 và U4 lên trước U2 vì dòng kết quả cần style và icon trạng thái. U6, U7, U8 gộp làm một task vì cùng viết lại `MainWindow.axaml` và `MainViewModel.cs`; reviewer không tách duyệt riêng được.

| File | Task | Việc |
|---|---|---|
| `src/LunaDns.Core/Features/FeatureStatus.cs` | U1 | thêm `Remedies` |
| `src/LunaDns.Core/Features/ProbeSummary.cs` | U1 | thêm `ProbeCoverageState`, `ProbeCoverage`, `ProbeGroupSummary.Coverage` |
| `tests/LunaDns.Core.Tests/Features/ProbeSummaryTests.cs` | U1 | +8 test |
| `src/LunaDns.App/ProbeRunner.cs` | U0 | chỉ kiểm nhóm Steam |
| `src/LunaDns.App/Strings.cs`, `Strings.resx` | U0, U2, U5, U6–U8, U10 | thêm/bớt chuỗi |
| `tests/LunaDns.App.Tests/StringsTests.cs` | U0, U2, U5, U6–U8, U10 | số chuỗi tĩnh 50 → 46 → 49 → 57 → 59 → 58 |
| `src/LunaDns.App/ViewModels/Tone.cs` (mới) | U3 | enum mức hiển thị |
| `src/LunaDns.App/Views/ToneConverters.cs` (mới) | U3 | Tone → bool cho `Classes.x` |
| `src/LunaDns.App/Styles/Theme.axaml` (mới) | U3, U4 | màu Light/Dark, style thẻ/huy hiệu/thông báo/icon |
| `src/LunaDns.App/App.axaml` | U3 | palette Fluent + StyleInclude |
| `src/LunaDns.App/Assets/Icons.axaml` (mới) | U4 | 12 `StreamGeometry` |
| `docs/THIRD-PARTY-NOTICES.md` (mới) | U4 | MIT của Fluent UI System Icons |
| `src/LunaDns.App/ViewModels/ProbeLineViewModel.cs` (mới) | U2 | một dòng kết quả |
| `tests/LunaDns.App.Tests/ProbeLineViewModelTests.cs` (mới) | U2 | +7 test |
| `src/LunaDns.App/OverallState.cs` (mới) | U5 | trạng thái tổng |
| `tests/LunaDns.App.Tests/OverallStateTests.cs` (mới) | U5 | +6 test |
| `src/LunaDns.App/ViewModels/MainViewModel.cs` | U0, U2, U6–U8 | viết lại bản cuối ở U6–U8 |
| `src/LunaDns.App/ViewModels/FeatureViewModel.cs` | U0, U6–U8 | viết lại bản cuối ở U6–U8 |
| `src/LunaDns.App/Views/MainWindow.axaml` | U0, U2, U3, U6–U8 | viết lại bản cuối ở U6–U8 |
| `src/LunaDns.App/Assets/make-icon.ps1` | U9, U10 | nhiều khung + 3 icon khay |
| `src/LunaDns.App/Assets/{lunadns,tray-off,tray-on,tray-error}.ico` | U9, U10 | sinh bằng script |
| `src/LunaDns.App/LunaDns.App.csproj` | U10 | `Assets\*.ico` |
| `src/LunaDns.App/App.axaml.cs` | U10 | icon/tooltip khay theo trạng thái, mục Tắt hết |

---

### Task U0: Ẩn PUBG trên giao diện

**Files:** sửa `src/LunaDns.App/ViewModels/MainViewModel.cs`, `ViewModels/FeatureViewModel.cs`, `Views/MainWindow.axaml`, `ProbeRunner.cs`, `Strings.cs`, `Strings.resx`, `tests/LunaDns.App.Tests/StringsTests.cs`.

**Interfaces:** Produces: `MainViewModel.Features` chỉ còn 2 phần tử (DnsRouting, SniProxy); `Strings` còn 46 chuỗi tĩnh.

- [ ] **Bước 1: Sửa test trước (đỏ)** trong `StringsTests.cs`: `Assert.Equal(50, properties.Count);` → `Assert.Equal(46, properties.Count);`, và bỏ dòng `AssertResolved(Strings.GroupName(DomainGroup.Pubg));`.

- [ ] **Bước 2: Chạy, thấy đỏ**: `dotnet test tests/LunaDns.App.Tests --filter Every_static_string_resolves` → FAIL `Expected: 46, Actual: 50`.

- [ ] **Bước 3: Gỡ chuỗi.** Trong `Strings.cs` xoá 4 dòng `SuggestPubgMode`, `CheckPubgLabel`, `PubgWarningTitle`, `PubgWarningText`. Trong `Strings.resx` xoá 5 mục `<data>`: `SuggestPubgMode`, `CheckPubgLabel`, `PubgWarningTitle`, `PubgWarningText`, `Group_Pubg`. Sửa giá trị hai mục:

```xml
  <data name="HeaderSubtitle" xml:space="preserve"><value>Sửa lỗi vào Steam khi nhà mạng chặn DNS hoặc lọc SNI.</value></data>
  <data name="NotCheckedYet" xml:space="preserve"><value>Chưa kiểm tra. Bấm "Kiểm tra" để xem Steam có bị chặn không.</value></data>
```

Giữ `Feature_PubgMode`, `FeatureDesc_PubgMode`, `Error_PubgPortBusy`: test duyệt mọi giá trị enum `Feature`/`FeatureError` nên vẫn cần, và giao diện không còn đường nào hiện ra chúng.

- [ ] **Bước 4: Gỡ PUBG khỏi ViewModel.** Trong `MainViewModel.cs`:
  - Xoá dòng `new FeatureViewModel(Feature.PubgMode, ToggleAsync, OnError),`.
  - Xoá cả thuộc tính `CheckPubg` (dòng 88–93) và dòng `OnPropertyChanged(nameof(CheckPubg));` trong `SaveSettings`. Đặt comment trên `SplitModeIndex`: `// Mục Nâng cao: ghi settings.json ngay; dịch vụ đang chạy giữ cấu hình cũ tới lần bật sau.`
  - Trong `ToggleAsync` xoá dòng `if (feature == Feature.PubgMode && !await AcceptPubgWarningAsync()) return;`.
  - Xoá cả hàm `AcceptPubgWarningAsync`.
  - Trong `ShowSummary` xoá dòng `if (_summary.SuggestPubgMode(status)) ProbeLines.Add(Strings.SuggestPubgMode);`.
  - Comment của `ApplyRecommendedAsync`: `/// <summary>Chỉ bật biện pháp cho Steam. DNS trước, proxy sau.</summary>`.

  Trong `FeatureViewModel.Update`, thay khối `switch` bằng:

```csharp
        _isOn = Feature == Feature.DnsRouting ? status.DnsRouting : status.SniProxy;
```

- [ ] **Bước 5: Kiểm tra chỉ nhóm Steam.** Trong `ProbeRunner.cs`, dòng cuối của `RunAsync`:

```csharp
        // KH6 U0: PUBG ra khỏi v1, chỉ kiểm nhóm Steam; settings.CheckPubg bị bỏ qua.
        return await prober.RunAsync(ProbeTargets.For(includePubg: false), cancellationToken);
```

- [ ] **Bước 6: XAML.** Trong `MainWindow.axaml` xoá dòng `<CheckBox Content="{x:Static app:Strings.CheckPubgLabel}" IsChecked="{Binding CheckPubg}" />`; comment `Ba công tắc` → `Hai công tắc`.

- [ ] **Bước 7: Verify.** Chạy lệnh verify chung → build 0 cảnh báo, Core 356 + 4, Platform 37 + 4, App 29. Rồi:

```powershell
Get-ChildItem src/LunaDns.App -Recurse -Include *.cs,*.axaml | Select-String -Pattern 'PUBG|Pubg' | Select-Object Path, LineNumber, Line
Select-String -Path src/LunaDns.App/Strings.resx -Pattern 'PUBG'
```

Mong đợi: `.cs`/`.axaml` chỉ còn comment ở `ProbeRunner.cs` và `Strings.cs`/`ErrorMessages.cs` không có `Pubg`. Trong resx, `PUBG` chỉ còn ở `Feature_PubgMode`, `FeatureDesc_PubgMode`, `Error_PubgPortBusy`. Mục nào khác còn chữ PUBG thì sửa câu cho bỏ PUBG.

---

### Task U1: Core — nhóm đã được biện pháp che chưa (UX-1)

**Files:** sửa `src/LunaDns.Core/Features/FeatureStatus.cs`, `src/LunaDns.Core/Features/ProbeSummary.cs`; test `tests/LunaDns.Core.Tests/Features/ProbeSummaryTests.cs`.

**Interfaces:** Produces:
- `ProbeRemedies FeatureStatus.Remedies { get; }`
- `enum ProbeCoverageState { NotNeeded, Missing, Covered, Unreachable }`
- `sealed record ProbeCoverage(ProbeCoverageState State, ProbeRemedies Missing, ProbeRemedies Covering)`
- `ProbeCoverage ProbeGroupSummary.Coverage(FeatureStatus status)`

- [ ] **Bước 1: Viết test (đỏ).** Thêm vào cuối lớp `ProbeSummaryTests`:

```csharp
    [Fact]
    public void Status_remedies_follow_the_two_switches()
    {
        Assert.Equal(ProbeRemedies.None, AllOff.Remedies);
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, new FeatureStatus(true, true, true, 18080, 0).Remedies);
    }

    /// <summary>Bảng "Dòng kết quả kiểm tra" ở KH6 mục 3.2, thêm dòng SNI khi cả hai biện pháp còn tắt.</summary>
    [Theory]
    [InlineData(ProbeVerdict.Ok, false, false, ProbeCoverageState.NotNeeded, ProbeRemedies.None, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.DnsPoisoned, false, false, ProbeCoverageState.Missing, ProbeRemedies.DnsRouting, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.DnsPoisoned, true, false, ProbeCoverageState.Covered, ProbeRemedies.None, ProbeRemedies.DnsRouting)]
    [InlineData(ProbeVerdict.SniFiltered, false, false, ProbeCoverageState.Missing, ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.SniFiltered, true, false, ProbeCoverageState.Missing, ProbeRemedies.SniProxy, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.SniFiltered, true, true, ProbeCoverageState.Covered, ProbeRemedies.None, ProbeRemedies.SniProxy)]
    [InlineData(ProbeVerdict.Unreachable, true, true, ProbeCoverageState.Unreachable, ProbeRemedies.None, ProbeRemedies.None)]
    public void Coverage_follows_kh6_table(
        ProbeVerdict verdict, bool dns, bool sni, ProbeCoverageState state, ProbeRemedies missing, ProbeRemedies covering)
    {
        var group = new ProbeGroupSummary(DomainGroup.Steam,
            [Result("store.steampowered.com", verdict), Result("steamcommunity.com", ProbeVerdict.Ok)]);

        Assert.Equal(new ProbeCoverage(state, missing, covering), group.Coverage(new FeatureStatus(dns, sni, false, null, 0)));
    }
```

  Không cần test riêng cho "Unreachable thắng host khác": dòng Unreachable trên đã có host thứ hai `Ok`, và `Worst` lấy verdict nặng nhất (đã có test `Worst_is_the_heaviest_verdict`).

- [ ] **Bước 2: Chạy, thấy đỏ**: `dotnet test tests/LunaDns.Core.Tests --filter ProbeSummaryTests` → lỗi biên dịch `'FeatureStatus' does not contain a definition for 'Remedies'`, `ProbeCoverage` không tồn tại.

- [ ] **Bước 3: Code.** `FeatureStatus.cs` thành:

```csharp
using LunaDns.Core.Probing;

namespace LunaDns.Core.Features;

/// <summary>Khu vực trạng thái (spec 4.8): đang bật gì, cổng proxy, số kết nối.</summary>
public sealed record FeatureStatus(bool DnsRouting, bool SniProxy, bool PubgMode, int? ProxyPort, int ProxyConnections)
{
    /// <summary>Biện pháp của bảng spec 4.1 đang bật. Chế độ PUBG không thuộc bảng này.</summary>
    public ProbeRemedies Remedies =>
        (DnsRouting ? ProbeRemedies.DnsRouting : ProbeRemedies.None) | (SniProxy ? ProbeRemedies.SniProxy : ProbeRemedies.None);
}
```

  Trong `ProbeSummary.cs`, thay `ProbeGroupSummary` bằng bản dưới và thêm hai kiểu mới ngay trên nó:

```csharp
/// <summary>Nhóm đã được biện pháp đang bật che chưa (KH6 U1, sửa UX-1).</summary>
public enum ProbeCoverageState
{
    /// <summary>Nhóm bình thường, không cần biện pháp nào.</summary>
    NotNeeded,
    /// <summary>Còn biện pháp đề xuất đang tắt (ghi ở Missing).</summary>
    Missing,
    /// <summary>Mọi biện pháp đề xuất đang bật. Prober đo đường mạng gốc, nên đây là "biết", chưa "đo" (KH6 D4).</summary>
    Covered,
    /// <summary>Có host không vào được kể cả khi cắt gói; biện pháp không giúp được.</summary>
    Unreachable,
}

/// <summary>Missing: biện pháp đề xuất còn tắt. Covering: biện pháp ghi ở hậu tố "đang né bằng …" (bảng KH6 3.2).</summary>
public sealed record ProbeCoverage(ProbeCoverageState State, ProbeRemedies Missing, ProbeRemedies Covering);

public sealed record ProbeGroupSummary(DomainGroup Group, IReadOnlyList<ProbeResult> Results)
{
    /// <summary>Verdict nặng nhất của nhóm, theo thứ tự Ok &lt; DnsPoisoned &lt; SniFiltered &lt; Unreachable.</summary>
    public ProbeVerdict Worst => Results.Count == 0 ? ProbeVerdict.Ok : Results.Max(r => r.Verdict);

    /// <summary>
    /// Đối chiếu biện pháp đề xuất (ProbeAdvisor) với biện pháp đang bật. Khi đã che đủ, Covering là biện pháp ứng với
    /// verdict nặng nhất: SNI thì Proxy SNI, DNS thì Định tuyến DNS (đúng câu chữ bảng KH6 3.2).
    /// </summary>
    public ProbeCoverage Coverage(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (Worst == ProbeVerdict.Unreachable) return new(ProbeCoverageState.Unreachable, ProbeRemedies.None, ProbeRemedies.None);
        var required = ProbeAdvisor.Recommend(Results);
        if (required == ProbeRemedies.None) return new(ProbeCoverageState.NotNeeded, ProbeRemedies.None, ProbeRemedies.None);
        var missing = required & ~status.Remedies;
        if (missing != ProbeRemedies.None) return new(ProbeCoverageState.Missing, missing, ProbeRemedies.None);
        var covering = Worst == ProbeVerdict.SniFiltered ? ProbeRemedies.SniProxy : ProbeRemedies.DnsRouting;
        return new(ProbeCoverageState.Covered, ProbeRemedies.None, covering);
    }
}
```

  Trong `PendingSteamRemedies`, thay hai dòng `if (status.DnsRouting) …` và `if (status.SniProxy) …` cùng `return remedies;` bằng `return remedies & ~status.Remedies;`.

- [ ] **Bước 4: Chạy, thấy xanh**: `dotnet test tests/LunaDns.Core.Tests` → 364 passed, 4 skipped (356 + 1 Fact + 7 InlineData).

- [ ] **Bước 5: Build cả solution** `dotnet build LunaDns.sln -warnaserror` → 0 cảnh báo.

---

### Task U3: Bảng màu, theme và Tone

**Files:** tạo `src/LunaDns.App/ViewModels/Tone.cs`, `src/LunaDns.App/Views/ToneConverters.cs`, `src/LunaDns.App/Styles/Theme.axaml`; sửa `src/LunaDns.App/App.axaml`, `src/LunaDns.App/Views/MainWindow.axaml`.

**Interfaces:** Produces:
- `internal enum Tone { Neutral, Info, Good, Warning, Error, Busy }` (namespace `LunaDns.App.ViewModels`)
- `ToneConverters.IsInfo/IsGood/IsWarning/IsError/IsBusy` (`IValueConverter`, namespace `LunaDns.App.Views`)
- Brush: `Brush.Window`, `Brush.Card`, `Brush.CardBorder`, `Brush.TextSecondary`, `Brush.Info`, `Brush.Good`, `Brush.Warning`, `Brush.Error`, `Brush.Neutral`, `Brush.InfoSoft`, `Brush.GoodSoft`, `Brush.WarningSoft`, `Brush.ErrorSoft`
- Class style: `Border.card`, `Border.strip`, `Border.pill`, `Border.banner`, `TextBlock.secondary`, `TextBlock.section`, `TextBlock.tone` (+ `.info/.good/.warning/.error/.busy`)

**Tương phản đã đo 14/11** (WCAG 2.1, script ở bước 5). Ba màu đề xuất ở 3.3 trượt ở theme tối nên đã đổi:

| Cặp | Sáng | Tối | Ngưỡng |
|---|---|---|---|
| Chữ chính / nền thẻ | 14,68 | 13,35 | 4,5 |
| Chữ phụ / nền thẻ | 4,83 | 5,71 | 4,5 |
| Nhấn (chữ/icon) / nền thẻ | 5,17 | 5,75 (`#60A5FA`, thay `#3B82F6` = 3,98) | 4,5 |
| Tốt / nền thẻ | 5,02 | 6,42 | 4,5 |
| Cảnh báo / nền thẻ | 5,02 | 6,81 | 4,5 |
| Lỗi / nền thẻ | 6,47 | 5,29 (`#F87171`, thay `#EF4444` = 3,89) | 4,5 |
| Trung tính / nền thẻ | 4,83 | 5,71 (`#94A3B8`, thay `#64748B` = 3,07) | 4,5 |
| Chữ trắng / nút nhấn `#2563EB` | 5,17 | 5,17 (giữ `#2563EB` cho nút ở cả hai theme) | 4,5 |
| Icon tốt / nền thông báo tốt | 4,57 | 4,00 | 3 |
| Icon cảnh báo / nền cảnh báo | 4,51 | 6,97 | 3 |
| Icon lỗi / nền lỗi | 5,30 | 5,84 | 3 |
| Icon thông tin / nền thông tin | 4,24 | 5,78 | 3 |

Viền thẻ (1,31 / 1,41) chỉ để trang trí: thẻ đã khác nền cửa sổ, nên không cần đạt 3:1.

- [ ] **Bước 1: `Tone.cs`**

```csharp
namespace LunaDns.App.ViewModels;

/// <summary>Mức hiển thị dùng chung cho dòng kiểm tra, thẻ, thông báo và trạng thái tổng (KH6 3.2). Màu và hình icon nằm ở Styles/Theme.axaml.</summary>
internal enum Tone
{
    Neutral,
    Info,
    Good,
    Warning,
    Error,
    Busy,
}
```

- [ ] **Bước 2: `ToneConverters.cs`**

```csharp
using Avalonia.Data.Converters;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Views;

/// <summary>Bật class style theo Tone, ví dụ Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}". Neutral là style gốc, không cần class.</summary>
internal static class ToneConverters
{
    public static readonly IValueConverter IsInfo = Is(Tone.Info);
    public static readonly IValueConverter IsGood = Is(Tone.Good);
    public static readonly IValueConverter IsWarning = Is(Tone.Warning);
    public static readonly IValueConverter IsError = Is(Tone.Error);
    public static readonly IValueConverter IsBusy = Is(Tone.Busy);

    private static FuncValueConverter<Tone, bool> Is(Tone tone) => new(value => value == tone);
}
```

- [ ] **Bước 3: `Styles/Theme.axaml`**

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- Bảng màu KH6 3.3, đã đo tương phản (bảng ở plan KH6, Task U3). Màu nhấn của control Fluent đặt ở App.axaml. -->
  <Styles.Resources>
    <ResourceDictionary>
      <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Light">
          <SolidColorBrush x:Key="Brush.Window" Color="#FAF7F2" />
          <SolidColorBrush x:Key="Brush.Card" Color="#FFFFFF" />
          <SolidColorBrush x:Key="Brush.CardBorder" Color="#E7E0D6" />
          <SolidColorBrush x:Key="Brush.TextSecondary" Color="#6B7280" />
          <SolidColorBrush x:Key="Brush.Info" Color="#2563EB" />
          <SolidColorBrush x:Key="Brush.Good" Color="#15803D" />
          <SolidColorBrush x:Key="Brush.Warning" Color="#B45309" />
          <SolidColorBrush x:Key="Brush.Error" Color="#B91C1C" />
          <SolidColorBrush x:Key="Brush.Neutral" Color="#6B7280" />
          <SolidColorBrush x:Key="Brush.InfoSoft" Color="#DBEAFE" />
          <SolidColorBrush x:Key="Brush.GoodSoft" Color="#DCFCE7" />
          <SolidColorBrush x:Key="Brush.WarningSoft" Color="#FEF3C7" />
          <SolidColorBrush x:Key="Brush.ErrorSoft" Color="#FEE2E2" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
          <SolidColorBrush x:Key="Brush.Window" Color="#0F172A" />
          <SolidColorBrush x:Key="Brush.Card" Color="#1E293B" />
          <SolidColorBrush x:Key="Brush.CardBorder" Color="#334155" />
          <SolidColorBrush x:Key="Brush.TextSecondary" Color="#94A3B8" />
          <SolidColorBrush x:Key="Brush.Info" Color="#60A5FA" />
          <SolidColorBrush x:Key="Brush.Good" Color="#22C55E" />
          <SolidColorBrush x:Key="Brush.Warning" Color="#F59E0B" />
          <SolidColorBrush x:Key="Brush.Error" Color="#F87171" />
          <SolidColorBrush x:Key="Brush.Neutral" Color="#94A3B8" />
          <SolidColorBrush x:Key="Brush.InfoSoft" Color="#172554" />
          <SolidColorBrush x:Key="Brush.GoodSoft" Color="#14532D" />
          <SolidColorBrush x:Key="Brush.WarningSoft" Color="#451A03" />
          <SolidColorBrush x:Key="Brush.ErrorSoft" Color="#450A0A" />
        </ResourceDictionary>
      </ResourceDictionary.ThemeDictionaries>
    </ResourceDictionary>
  </Styles.Resources>

  <!-- Thẻ: nền riêng, bo góc. Dải màu bên trái (Border.strip) đổi theo Tone. -->
  <Style Selector="Border.card">
    <Setter Property="Background" Value="{DynamicResource Brush.Card}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.CardBorder}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="16" />
  </Style>
  <Style Selector="Border.strip">
    <Setter Property="Background" Value="{DynamicResource Brush.Neutral}" />
    <Setter Property="CornerRadius" Value="8,0,0,8" />
  </Style>
  <Style Selector="Border.strip.good"><Setter Property="Background" Value="{DynamicResource Brush.Good}" /></Style>
  <Style Selector="Border.strip.busy"><Setter Property="Background" Value="{DynamicResource Brush.Info}" /></Style>
  <Style Selector="Border.strip.error"><Setter Property="Background" Value="{DynamicResource Brush.Error}" /></Style>

  <!-- Huy hiệu trạng thái tổng ở header -->
  <Style Selector="Border.pill">
    <Setter Property="CornerRadius" Value="12" />
    <Setter Property="Padding" Value="10,4" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Neutral}" />
  </Style>
  <Style Selector="Border.pill.good">
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Good}" />
    <Setter Property="Background" Value="{DynamicResource Brush.GoodSoft}" />
  </Style>
  <Style Selector="Border.pill.busy">
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Info}" />
    <Setter Property="Background" Value="{DynamicResource Brush.InfoSoft}" />
  </Style>
  <Style Selector="Border.pill.error">
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Error}" />
    <Setter Property="Background" Value="{DynamicResource Brush.ErrorSoft}" />
  </Style>

  <!-- Thanh thông báo theo mức (U7) -->
  <Style Selector="Border.banner">
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="12,8" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Info}" />
    <Setter Property="Background" Value="{DynamicResource Brush.InfoSoft}" />
  </Style>
  <Style Selector="Border.banner.good">
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Good}" />
    <Setter Property="Background" Value="{DynamicResource Brush.GoodSoft}" />
  </Style>
  <Style Selector="Border.banner.warning">
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Warning}" />
    <Setter Property="Background" Value="{DynamicResource Brush.WarningSoft}" />
  </Style>
  <Style Selector="Border.banner.error">
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Error}" />
    <Setter Property="Background" Value="{DynamicResource Brush.ErrorSoft}" />
  </Style>

  <!-- Chữ -->
  <Style Selector="TextBlock.secondary"><Setter Property="Foreground" Value="{DynamicResource Brush.TextSecondary}" /></Style>
  <Style Selector="TextBlock.section">
    <Setter Property="FontSize" Value="12" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.TextSecondary}" />
  </Style>
  <Style Selector="TextBlock.tone"><Setter Property="Foreground" Value="{DynamicResource Brush.Neutral}" /></Style>
  <Style Selector="TextBlock.tone.info"><Setter Property="Foreground" Value="{DynamicResource Brush.Info}" /></Style>
  <Style Selector="TextBlock.tone.good"><Setter Property="Foreground" Value="{DynamicResource Brush.Good}" /></Style>
  <Style Selector="TextBlock.tone.warning"><Setter Property="Foreground" Value="{DynamicResource Brush.Warning}" /></Style>
  <Style Selector="TextBlock.tone.error"><Setter Property="Foreground" Value="{DynamicResource Brush.Error}" /></Style>
  <Style Selector="TextBlock.tone.busy"><Setter Property="Foreground" Value="{DynamicResource Brush.Info}" /></Style>
</Styles>
```

- [ ] **Bước 4: `App.axaml`** thành:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LunaDns.App.App"
             x:ClassModifier="internal"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <!-- KH6 D1: màu nhấn #2563EB cho cả hai theme (chữ trắng trên nút đạt 5,17:1). Theme theo Windows (D3). -->
    <FluentTheme>
      <FluentTheme.Palettes>
        <ColorPaletteResources x:Key="Light" Accent="#FF2563EB" />
        <ColorPaletteResources x:Key="Dark" Accent="#FF2563EB" />
      </FluentTheme.Palettes>
    </FluentTheme>
    <StyleInclude Source="avares://LunaDNS/Styles/Theme.axaml" />
  </Application.Styles>
</Application>
```

  Trong `MainWindow.axaml`, thêm thuộc tính `Background="{DynamicResource Brush.Window}"` vào thẻ `<Window>`, và đổi hai `<Border … BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}">` thành `<Border Classes="card">` (bỏ `Padding`, `CornerRadius`, `BorderThickness`, `BorderBrush`; thẻ tính năng giữ `Margin="0,0,0,8"`).

- [ ] **Bước 5: Đo lại tương phản** bằng script (Python có sẵn trên máy):

```powershell
python -I -c "
def L(h):
    c=[int(h[i:i+2],16)/255 for i in (1,3,5)]
    c=[x/12.92 if x<=0.03928 else ((x+0.055)/1.055)**2.4 for x in c]
    return 0.2126*c[0]+0.7152*c[1]+0.0722*c[2]
def r(a,b):
    a,b=sorted((L(a),L(b)),reverse=True); return (a+0.05)/(b+0.05)
pairs=[('#1F2937','#FFFFFF'),('#6B7280','#FFFFFF'),('#2563EB','#FFFFFF'),('#15803D','#FFFFFF'),('#B45309','#FFFFFF'),('#B91C1C','#FFFFFF'),
       ('#F1F5F9','#1E293B'),('#94A3B8','#1E293B'),('#60A5FA','#1E293B'),('#22C55E','#1E293B'),('#F59E0B','#1E293B'),('#F87171','#1E293B'),
       ('#FFFFFF','#2563EB'),('#15803D','#DCFCE7'),('#B45309','#FEF3C7'),('#B91C1C','#FEE2E2'),('#2563EB','#DBEAFE'),
       ('#22C55E','#14532D'),('#F59E0B','#451A03'),('#F87171','#450A0A'),('#60A5FA','#172554')]
for a,b in pairs: print(a,b,'%.2f'%r(a,b))
"
```

  Mong đợi: 13 cặp chữ đầu ≥ 4,5; 8 cặp icon/nền thông báo cuối ≥ 3. Ghi số của `#2563EB / #DBEAFE` vào bảng trên. Cặp nào trượt thì chỉnh mã màu trong `Theme.axaml` và bảng.

- [ ] **Bước 6: Verify** bằng lệnh verify chung (số test như sau U1). Chạy `src/LunaDns.App/bin/Debug/net8.0-windows/LunaDNS.exe` (Admin): nền kem ở theme sáng. Đổi Windows sang theme tối (Settings → Personalization → Colors) thì app đổi theo, không cần mở lại.

---

### Task U4: Bộ icon nét mảnh

**Files:** tạo `src/LunaDns.App/Assets/Icons.axaml`, `docs/THIRD-PARTY-NOTICES.md`; sửa `src/LunaDns.App/Styles/Theme.axaml`.

**Interfaces:** Consumes: class `Tone` từ U3. Produces: geometry `Icon.Dns`, `Icon.Proxy`, `Icon.Check`, `Icon.Restore`, `Icon.Advanced`, `Icon.Diagnostics`, `Icon.Good`, `Icon.Warning`, `Icon.Error`, `Icon.Neutral`, `Icon.Info`, `Icon.Close`; style `PathIcon.tone` (+ `.info/.good/.warning/.error/.busy`), `PathIcon.button`, `PathIcon.feature` (+ `.dns`), `Border.badge`.

Ánh xạ icon (Fluent, 24 regular): Định tuyến DNS = `globe`, Proxy SNI = `cut`, Kiểm tra = `stethoscope`, Tắt hết = `arrow_undo`, Nâng cao = `options`, Chẩn đoán = `clipboard_pulse`, trạng thái = `checkmark_circle` / `warning` / `dismiss_circle` / `circle`, thông tin = `info`, đóng thông báo = `dismiss`. Fluent không có "địa cầu có mũi tên rẽ"; dùng `globe`. Mỗi SVG có đúng một `<path>`, viewBox 24×24, không có `fill-rule` (tức nonzero), nên geometry phải có tiền tố `F1`.

- [ ] **Bước 1: Sinh `Icons.axaml` và lấy giấy phép** (Git Bash; chỉ đọc dữ liệu SVG/văn bản, không chạy gì tải về):

```bash
set -e
out=src/LunaDns.App/Assets/Icons.axaml
tmp=$(mktemp -d)
base=https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets
{
  echo '<!-- Path data chép từ Fluent UI System Icons (MIT, © Microsoft), bản 24 regular. Giấy phép: docs/THIRD-PARTY-NOTICES.md. -->'
  echo '<ResourceDictionary xmlns="https://github.com/avaloniaui"'
  echo '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">'
  for pair in "Globe:globe:Icon.Dns" "Cut:cut:Icon.Proxy" "Stethoscope:stethoscope:Icon.Check" "Arrow Undo:arrow_undo:Icon.Restore" \
              "Options:options:Icon.Advanced" "Clipboard Pulse:clipboard_pulse:Icon.Diagnostics" "Checkmark Circle:checkmark_circle:Icon.Good" \
              "Warning:warning:Icon.Warning" "Dismiss Circle:dismiss_circle:Icon.Error" "Circle:circle:Icon.Neutral" "Info:info:Icon.Info" \
              "Dismiss:dismiss:Icon.Close"; do
    IFS=: read -r dir name key <<< "$pair"
    curl -sf -o "$tmp/$name.svg" "$base/${dir// /%20}/SVG/ic_fluent_${name}_24_regular.svg"
    test "$(grep -o '<path' "$tmp/$name.svg" | wc -l)" -eq 1
    d=$(grep -o ' d="[^"]*"' "$tmp/$name.svg" | sed 's/ d="//;s/"$//')
    echo "  <!-- ic_fluent_${name}_24_regular -->"
    echo "  <StreamGeometry x:Key=\"$key\">F1 $d</StreamGeometry>"
  done
  echo '</ResourceDictionary>'
} > "$out"
curl -sf -o "$tmp/LICENSE" https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/LICENSE
{
  echo '# Thành phần bên thứ ba'
  echo
  echo '## Fluent UI System Icons'
  echo
  echo 'Path data của 12 icon trong `src/LunaDns.App/Assets/Icons.axaml` chép từ <https://github.com/microsoft/fluentui-system-icons> (bản 24 regular).'
  echo
  echo '```text'
  cat "$tmp/LICENSE"
  echo '```'
} > docs/THIRD-PARTY-NOTICES.md
grep -c '<StreamGeometry' "$out"
```

  Mong đợi: in `12`; `docs/THIRD-PARTY-NOTICES.md` có dòng `MIT License` và `Copyright (c) 2020 Microsoft Corporation`.

- [ ] **Bước 2: Nạp icon và thêm style.** Trong `Theme.axaml`, ngay sau `<ResourceDictionary>` mở đầu (trước `ThemeDictionaries`), thêm:

```xml
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://LunaDNS/Assets/Icons.axaml" />
      </ResourceDictionary.MergedDictionaries>
```

  Và thêm trước `</Styles>`:

```xml
  <!-- Icon trạng thái: mỗi mức một hình (✓ ! ✕ ○), không chỉ khác màu -->
  <Style Selector="PathIcon.tone">
    <Setter Property="Width" Value="16" />
    <Setter Property="Height" Value="16" />
    <Setter Property="Data" Value="{DynamicResource Icon.Neutral}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.Neutral}" />
  </Style>
  <Style Selector="PathIcon.tone.info">
    <Setter Property="Data" Value="{DynamicResource Icon.Info}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.Info}" />
  </Style>
  <Style Selector="PathIcon.tone.good">
    <Setter Property="Data" Value="{DynamicResource Icon.Good}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.Good}" />
  </Style>
  <Style Selector="PathIcon.tone.warning">
    <Setter Property="Data" Value="{DynamicResource Icon.Warning}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.Warning}" />
  </Style>
  <Style Selector="PathIcon.tone.error">
    <Setter Property="Data" Value="{DynamicResource Icon.Error}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.Error}" />
  </Style>
  <Style Selector="PathIcon.tone.busy">
    <Setter Property="Foreground" Value="{DynamicResource Brush.Info}" />
  </Style>

  <!-- Icon trong nút -->
  <Style Selector="PathIcon.button">
    <Setter Property="Width" Value="16" />
    <Setter Property="Height" Value="16" />
  </Style>

  <!-- Huy hiệu tròn 36 px của thẻ tính năng (lấy ý từ infographic) -->
  <Style Selector="Border.badge">
    <Setter Property="Width" Value="36" />
    <Setter Property="Height" Value="36" />
    <Setter Property="CornerRadius" Value="18" />
    <Setter Property="Background" Value="{DynamicResource Brush.InfoSoft}" />
  </Style>
  <Style Selector="PathIcon.feature">
    <Setter Property="Width" Value="20" />
    <Setter Property="Height" Value="20" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.Info}" />
    <Setter Property="Data" Value="{DynamicResource Icon.Proxy}" />
  </Style>
  <Style Selector="PathIcon.feature.dns"><Setter Property="Data" Value="{DynamicResource Icon.Dns}" /></Style>
```

- [ ] **Bước 3: Verify** bằng lệnh verify chung → build 0 cảnh báo (lỗi `ResourceInclude` hay path data sai lộ ở bước biên dịch XAML), số test như sau U1. Hình icon kiểm ở U11.

---

### Task U2: Dòng kết quả có icon, màu và hậu tố (UX-1)

**Files:** tạo `src/LunaDns.App/ViewModels/ProbeLineViewModel.cs`, `tests/LunaDns.App.Tests/ProbeLineViewModelTests.cs`; sửa `MainViewModel.cs`, `MainWindow.axaml`, `Strings.cs`, `Strings.resx`, `StringsTests.cs`.

**Interfaces:** Consumes: `ProbeGroupSummary.Coverage` (U1), `Tone` (U3), `PathIcon.tone` (U4). Produces: `internal sealed record ProbeLineViewModel(string Group, string Detail, Tone Tone)` với `string AutomationName` và `static ProbeLineViewModel From(ProbeGroupSummary group, FeatureStatus status)`; `MainViewModel.ProbeLines : ObservableCollection<ProbeLineViewModel>`, `MainViewModel.ProbeHint : string`, `MainViewModel.HasProbeHint : bool`.

- [ ] **Bước 1: Viết test (đỏ).** `tests/LunaDns.App.Tests/ProbeLineViewModelTests.cs`:

```csharp
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.App.Tests;

/// <summary>Bảng "Dòng kết quả kiểm tra" ở KH6 mục 3.2. Tone là kiểu internal nên tham số test là tên của nó.</summary>
public sealed class ProbeLineViewModelTests
{
    [Theory]
    [InlineData(ProbeVerdict.Ok, false, false, "Bình thường", "Good")]
    [InlineData(ProbeVerdict.DnsPoisoned, false, false, "DNS bị chặn · còn thiếu Định tuyến DNS", "Warning")]
    [InlineData(ProbeVerdict.DnsPoisoned, true, false, "DNS bị chặn · đang né bằng Định tuyến DNS", "Good")]
    [InlineData(ProbeVerdict.SniFiltered, false, false, "Bị lọc SNI · còn thiếu Định tuyến DNS, Proxy SNI", "Warning")]
    [InlineData(ProbeVerdict.SniFiltered, true, false, "Bị lọc SNI · còn thiếu Proxy SNI", "Warning")]
    [InlineData(ProbeVerdict.SniFiltered, true, true, "Bị lọc SNI · đang né bằng Proxy SNI", "Good")]
    [InlineData(ProbeVerdict.Unreachable, true, true, "Không vào được", "Error")]
    public void Line_follows_kh6_table(ProbeVerdict verdict, bool dns, bool sni, string detail, string tone)
    {
        var group = new ProbeGroupSummary(DomainGroup.Steam, [new ProbeResult("store.steampowered.com", verdict, [], [], false)]);

        var line = ProbeLineViewModel.From(group, new FeatureStatus(dns, sni, false, null, 0));

        Assert.Equal("Steam", line.Group);
        Assert.Equal(detail, line.Detail);
        Assert.Equal(tone, line.Tone.ToString());
        Assert.Equal($"Steam: {detail}", line.AutomationName);
    }
}
```

  Trong `StringsTests.cs`: `Assert.Equal(46, …)` → `Assert.Equal(49, …)`.

- [ ] **Bước 2: Chạy, thấy đỏ**: `dotnet test tests/LunaDns.App.Tests` → lỗi biên dịch `ProbeLineViewModel` không tồn tại.

- [ ] **Bước 3: Chuỗi.** `Strings.cs`: xoá dòng `GroupLineFormat`; thêm sau dòng `CheckFailed`:

```csharp
    public static string CheckSectionTitle => Get(nameof(CheckSectionTitle));
    public static string ProbeDetailFormat => Get(nameof(ProbeDetailFormat));
    public static string ProbeCoveredFormat => Get(nameof(ProbeCoveredFormat));
    public static string ProbeMissingFormat => Get(nameof(ProbeMissingFormat));
```

  `Strings.resx`: xoá mục `GroupLineFormat`; thêm sau mục `CheckFailed`:

```xml
  <data name="CheckSectionTitle" xml:space="preserve"><value>KIỂM TRA MẠNG</value></data>
  <data name="ProbeDetailFormat" xml:space="preserve"><value>{0} · {1}</value></data>
  <data name="ProbeCoveredFormat" xml:space="preserve"><value>đang né bằng {0}</value></data>
  <data name="ProbeMissingFormat" xml:space="preserve"><value>còn thiếu {0}</value></data>
```

- [ ] **Bước 4: `ProbeLineViewModel.cs`**

```csharp
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Một dòng kết quả "Kiểm tra" (KH6 3.2, sửa UX-1): kết luận của Prober kèm hậu tố về biện pháp đang bật.
/// "Đang né bằng …" chứ không "đã khắc phục": Prober đo đường gốc, app chỉ biết biện pháp đang bật (D4).
/// </summary>
internal sealed record ProbeLineViewModel(string Group, string Detail, Tone Tone)
{
    public string AutomationName => $"{Group}: {Detail}";

    public static ProbeLineViewModel From(ProbeGroupSummary group, FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(group);
        var coverage = group.Coverage(status);
        var (suffix, tone) = coverage.State switch
        {
            ProbeCoverageState.Covered => (Strings.Format(nameof(Strings.ProbeCoveredFormat), Names(coverage.Covering)), Tone.Good),
            ProbeCoverageState.Missing => (Strings.Format(nameof(Strings.ProbeMissingFormat), Names(coverage.Missing)), Tone.Warning),
            ProbeCoverageState.Unreachable => (string.Empty, Tone.Error),
            _ => (string.Empty, Tone.Good),
        };
        var verdict = Strings.Verdict(group.Worst);
        var detail = suffix.Length == 0 ? verdict : Strings.Format(nameof(Strings.ProbeDetailFormat), verdict, suffix);
        return new ProbeLineViewModel(Strings.GroupName(group.Group), detail, tone);
    }

    private static string Names(ProbeRemedies remedies)
    {
        var names = new List<string>(2);
        if (remedies.HasFlag(ProbeRemedies.DnsRouting)) names.Add(Strings.FeatureTitle(Feature.DnsRouting));
        if (remedies.HasFlag(ProbeRemedies.SniProxy)) names.Add(Strings.FeatureTitle(Feature.SniProxy));
        return string.Join(", ", names);
    }
}
```

- [ ] **Bước 5: Nối vào `MainViewModel.cs`.**
  - `public ObservableCollection<string> ProbeLines` → `public ObservableCollection<ProbeLineViewModel> ProbeLines`.
  - Thêm field `private string _probeHint = string.Empty;` và thuộc tính:

```csharp
    /// <summary>Một câu dưới các dòng kết quả: "không thấy bị chặn" hoặc gợi ý khi có máy chủ không vào được.</summary>
    public string ProbeHint
    {
        get => _probeHint;
        private set
        {
            if (SetProperty(ref _probeHint, value)) OnPropertyChanged(nameof(HasProbeHint));
        }
    }

    public bool HasProbeHint => _probeHint.Length > 0;
```

  - `ShowSummary` thành:

```csharp
    private void ShowSummary()
    {
        if (_summary is null) return;
        var status = _services.Features.GetStatus();
        ProbeStatus = Strings.Format(nameof(Strings.CheckedAtFormat), _summary.CheckedAt);
        ProbeLines.Clear();
        foreach (var group in _summary.Groups) ProbeLines.Add(ProbeLineViewModel.From(group, status));
        ProbeHint = _summary.AnyUnreachable ? Strings.UnreachableHint
            : _summary.Groups.All(g => g.Worst == ProbeVerdict.Ok) ? Strings.AllOk
            : string.Empty;
        CanApplyRecommended = _summary.PendingSteamRemedies(status) != ProbeRemedies.None;
    }
```

  - Trong `CheckAsync`, sau `ProbeLines.Clear();` thêm `ProbeHint = string.Empty;` và `_summary = null;` (để một lần bật/tắt giữa chừng không vẽ lại kết quả cũ).
  - Trong `finally` của `ToggleAsync`, sau `Refresh();` thêm `ShowSummary();` (bật/tắt xong thì hậu tố đổi ngay).

- [ ] **Bước 6: XAML.** Thêm `xmlns:v="using:LunaDns.App.Views"` vào `<Window>`; xoá `xmlns:sys="using:System"`. Thay `ItemsControl` của `ProbeLines` bằng:

```xml
          <ItemsControl ItemsSource="{Binding ProbeLines}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:ProbeLineViewModel">
                <Grid ColumnDefinitions="Auto,Auto,*" Margin="0,2" AutomationProperties.Name="{Binding AutomationName}">
                  <PathIcon Classes="tone" VerticalAlignment="Top" Margin="0,2,8,0"
                            Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                            Classes.warning="{Binding Tone, Converter={x:Static v:ToneConverters.IsWarning}}"
                            Classes.error="{Binding Tone, Converter={x:Static v:ToneConverters.IsError}}" />
                  <TextBlock Grid.Column="1" Text="{Binding Group}" FontWeight="SemiBold" Margin="0,0,8,0" />
                  <TextBlock Grid.Column="2" Classes="tone" Text="{Binding Detail}" TextWrapping="Wrap"
                             Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                             Classes.warning="{Binding Tone, Converter={x:Static v:ToneConverters.IsWarning}}"
                             Classes.error="{Binding Tone, Converter={x:Static v:ToneConverters.IsError}}" />
                </Grid>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
          <TextBlock Classes="secondary" Text="{Binding ProbeHint}" TextWrapping="Wrap" IsVisible="{Binding HasProbeHint}" />
```

- [ ] **Bước 7: Chạy, thấy xanh**: lệnh verify chung → App 36 (29 + 7), Core 364 + 4, Platform 37 + 4.

---

### Task U5: Trạng thái tổng `OverallState`

**Files:** tạo `src/LunaDns.App/OverallState.cs`, `tests/LunaDns.App.Tests/OverallStateTests.cs`; sửa `Strings.cs`, `Strings.resx`, `StringsTests.cs`. Chưa nối vào giao diện (U6–U8 và U10 nối).

**Interfaces:** Produces: `internal enum OverallKind { Off, On, Busy, Attention }`; `internal sealed record OverallState(OverallKind Kind, IReadOnlyList<Feature> Active)` với `const int Total = 2`, `static OverallState From(FeatureStatus status, bool busy, bool attention)`, `Tone Tone`, `string Text`, `string Tooltip`.

- [ ] **Bước 1: Viết test (đỏ).** `tests/LunaDns.App.Tests/OverallStateTests.cs`:

```csharp
using LunaDns.Core.Features;

namespace LunaDns.App.Tests;

/// <summary>Bảng "Trạng thái tổng" ở KH6 mục 3.2 và tooltip khay ở 3.4.</summary>
public sealed class OverallStateTests
{
    private static FeatureStatus Status(bool dns, bool sni, bool pubg = false) => new(dns, sni, pubg, null, 0);

    [Fact]
    public void Nothing_on_is_off()
    {
        var state = OverallState.From(Status(false, false), busy: false, attention: false);

        Assert.Equal(OverallKind.Off, state.Kind);
        Assert.Equal("Đang tắt", state.Text);
        Assert.Equal("LunaDNS: đang tắt", state.Tooltip);
        Assert.Equal("Neutral", state.Tone.ToString());
    }

    [Fact]
    public void One_switch_on_counts_one_of_two()
    {
        var state = OverallState.From(Status(true, false), busy: false, attention: false);

        Assert.Equal(OverallKind.On, state.Kind);
        Assert.Equal("Đang bật 1/2", state.Text);
        Assert.Equal("LunaDNS: đang bật Định tuyến DNS", state.Tooltip);
        Assert.Equal("Good", state.Tone.ToString());
    }

    [Fact]
    public void Both_switches_on_are_listed_in_the_tooltip()
    {
        var state = OverallState.From(Status(true, true), busy: false, attention: false);

        Assert.Equal("Đang bật 2/2", state.Text);
        Assert.Equal("LunaDNS: đang bật Định tuyến DNS, Proxy SNI", state.Tooltip);
    }

    [Fact]
    public void Attention_wins_over_on()
    {
        var state = OverallState.From(Status(true, true), busy: false, attention: true);

        Assert.Equal(OverallKind.Attention, state.Kind);
        Assert.Equal("Cần xử lý", state.Text);
        Assert.Equal("LunaDNS: cần xử lý, mở để xem", state.Tooltip);
        Assert.Equal("Error", state.Tone.ToString());
    }

    [Fact]
    public void Busy_wins_over_attention_and_keeps_the_active_list()
    {
        var state = OverallState.From(Status(true, false), busy: true, attention: true);

        Assert.Equal(OverallKind.Busy, state.Kind);
        Assert.Equal("Đang xử lý…", state.Text);
        Assert.Equal("LunaDNS: đang xử lý…", state.Tooltip);
        Assert.Equal("Busy", state.Tone.ToString());
        Assert.Equal([Feature.DnsRouting], state.Active);
    }

    /// <summary>KH6 U0: Chế độ PUBG ẩn khỏi v1, không tính vào trạng thái tổng.</summary>
    [Fact]
    public void Pubg_mode_alone_is_off() =>
        Assert.Equal(OverallKind.Off, OverallState.From(Status(false, false, pubg: true), busy: false, attention: false).Kind);
}
```

  Trong `StringsTests.cs`: `49` → `57`.

- [ ] **Bước 2: Chạy, thấy đỏ**: `dotnet test tests/LunaDns.App.Tests` → lỗi biên dịch `OverallState` không tồn tại.

- [ ] **Bước 3: Chuỗi.** `Strings.cs`, thêm sau dòng `StatusProxyFormat`:

```csharp
    public static string OverallOff => Get(nameof(OverallOff));
    public static string OverallOnFormat => Get(nameof(OverallOnFormat));
    public static string OverallBusy => Get(nameof(OverallBusy));
    public static string OverallAttention => Get(nameof(OverallAttention));
```

  và sau dòng `TrayTooltip`:

```csharp
    public static string TrayTooltipOff => Get(nameof(TrayTooltipOff));
    public static string TrayTooltipOnFormat => Get(nameof(TrayTooltipOnFormat));
    public static string TrayTooltipBusy => Get(nameof(TrayTooltipBusy));
    public static string TrayTooltipAttention => Get(nameof(TrayTooltipAttention));
```

  `Strings.resx`, thêm sau mục `StatusProxyFormat`:

```xml
  <data name="OverallOff" xml:space="preserve"><value>Đang tắt</value></data>
  <data name="OverallOnFormat" xml:space="preserve"><value>Đang bật {0}/{1}</value></data>
  <data name="OverallBusy" xml:space="preserve"><value>Đang xử lý…</value></data>
  <data name="OverallAttention" xml:space="preserve"><value>Cần xử lý</value></data>
```

  và sau mục `TrayTooltip`:

```xml
  <data name="TrayTooltipOff" xml:space="preserve"><value>LunaDNS: đang tắt</value></data>
  <data name="TrayTooltipOnFormat" xml:space="preserve"><value>LunaDNS: đang bật {0}</value></data>
  <data name="TrayTooltipBusy" xml:space="preserve"><value>LunaDNS: đang xử lý…</value></data>
  <data name="TrayTooltipAttention" xml:space="preserve"><value>LunaDNS: cần xử lý, mở để xem</value></data>
```

- [ ] **Bước 4: `OverallState.cs`**

```csharp
using LunaDns.App.ViewModels;
using LunaDns.Core.Features;

namespace LunaDns.App;

internal enum OverallKind
{
    Off,
    On,
    Busy,
    Attention,
}

/// <summary>
/// Trạng thái tổng cho huy hiệu header, icon khay và tooltip (KH6 3.2, 3.4). Ưu tiên Đang xử lý &gt; Cần xử lý &gt; Bật &gt; Tắt.
/// Chỉ đếm hai công tắc trên giao diện; Chế độ PUBG ẩn khỏi v1 (KH6 U0).
/// </summary>
internal sealed record OverallState(OverallKind Kind, IReadOnlyList<Feature> Active)
{
    public const int Total = 2;

    public static OverallState From(FeatureStatus status, bool busy, bool attention)
    {
        ArgumentNullException.ThrowIfNull(status);
        var active = new List<Feature>(Total);
        if (status.DnsRouting) active.Add(Feature.DnsRouting);
        if (status.SniProxy) active.Add(Feature.SniProxy);
        var kind = busy ? OverallKind.Busy
            : attention ? OverallKind.Attention
            : active.Count > 0 ? OverallKind.On
            : OverallKind.Off;
        return new OverallState(kind, active);
    }

    public Tone Tone => Kind switch
    {
        OverallKind.Off => Tone.Neutral,
        OverallKind.On => Tone.Good,
        OverallKind.Busy => Tone.Busy,
        _ => Tone.Error,
    };

    public string Text => Kind switch
    {
        OverallKind.Off => Strings.OverallOff,
        OverallKind.On => Strings.Format(nameof(Strings.OverallOnFormat), Active.Count, Total),
        OverallKind.Busy => Strings.OverallBusy,
        _ => Strings.OverallAttention,
    };

    public string Tooltip => Kind switch
    {
        OverallKind.Off => Strings.TrayTooltipOff,
        OverallKind.On => Strings.Format(nameof(Strings.TrayTooltipOnFormat), string.Join(", ", Active.Select(Strings.FeatureTitle))),
        OverallKind.Busy => Strings.TrayTooltipBusy,
        _ => Strings.TrayTooltipAttention,
    };
}
```

- [ ] **Bước 5: Chạy, thấy xanh**: lệnh verify chung → App 42 (36 + 6).

---

### Task U6–U8: Thẻ tính năng, thanh thông báo, dọn bố cục

**Files:** viết lại `src/LunaDns.App/ViewModels/FeatureViewModel.cs`, `ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml`; sửa `Strings.cs`, `Strings.resx`, `StringsTests.cs`.

**Interfaces:** Consumes: `Tone`, `ToneConverters` (U3); style/icon (U3, U4); `ProbeLineViewModel` (U2); `OverallState` (U5). Produces (U10 dùng): `MainViewModel.Overall : OverallState` (phát `PropertyChanged` mỗi lần trạng thái, việc đang chạy hoặc thông báo đổi), `MainViewModel.DisableAllCommand : AsyncCommand`.

Quy tắc trong code dưới đây:
- **Đang xử lý:** bật/tắt, Áp dụng đề xuất, Tắt hết, Kiểm tra đều chạy qua `RunBusyAsync`: khoá thẻ liên quan, Overall = Đang xử lý, xong thì làm mới thẻ và kết quả Kiểm tra.
- **Cần xử lý** = thông báo đang ở mức Lỗi, hoặc có thẻ đang ghi lỗi. Đóng thông báo hoặc bật/tắt lại thành công thì hết.
- **Mức thông báo:** khôi phục lúc mở thất bại, bật/tắt lỗi, watchdog tắt, Tắt hết lỗi, ghi báo cáo lỗi, lỗi không lường, lưu settings lỗi → **Lỗi**; Tắt hết xong → **Thành công**; đã hoàn tác N mục lúc mở, đang chẩn đoán, đã lưu báo cáo, không có gì để áp dụng → **Thông tin**. Mức Cảnh báo chưa dùng (đã có style để dùng sau).
- **"Tắt hết & khôi phục"** bấm được khi có công tắc bật (kể cả Chế độ PUBG sót) hoặc đang Cần xử lý (để hoàn tác mục sót).

- [ ] **Bước 1: Test đếm chuỗi (đỏ).** `StringsTests.cs`: `57` → `59`. Chạy `dotnet test tests/LunaDns.App.Tests --filter Every_static_string_resolves` → FAIL.

- [ ] **Bước 2: Chuỗi.** `Strings.cs`: xoá dòng `SettingsHeader`; thêm sau dòng `OverallAttention`:

```csharp
    public static string FeatureBusy => Get(nameof(FeatureBusy));
    public static string FeatureStoppedShort => Get(nameof(FeatureStoppedShort));
    public static string DismissMessage => Get(nameof(DismissMessage));
```

  `Strings.resx`: xoá mục `SettingsHeader`; thêm sau mục `OverallAttention`:

```xml
  <data name="FeatureBusy" xml:space="preserve"><value>Đang xử lý…</value></data>
  <data name="FeatureStoppedShort" xml:space="preserve"><value>Đã tự tắt vì lỗi. Xem thông báo phía trên.</value></data>
  <data name="DismissMessage" xml:space="preserve"><value>Đóng thông báo</value></data>
```

  D7, sửa giá trị hai mô tả (thêm cụm thường ngày ở đầu):

```xml
  <data name="FeatureDesc_DnsRouting" xml:space="preserve"><value>Giúp đăng nhập Steam: hỏi tên miền Steam qua DNS mã hoá (DoH) thay cho DNS của nhà mạng.</value></data>
  <data name="FeatureDesc_SniProxy" xml:space="preserve"><value>Giúp mở Store và Community: cắt gói mở kết nối để qua được bộ lọc SNI.</value></data>
```

- [ ] **Bước 3: `FeatureViewModel.cs`** (toàn bộ file):

```csharp
using System.Globalization;
using LunaDns.Core.Features;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Một thẻ công tắc (spec 4.8, KH6 U6). IsOn chỉ đổi qua Update; ToggleSwitch bind OneWay.
/// Tone: Đang xử lý &gt; Lỗi &gt; Bật &gt; Tắt; dải màu bên trái, icon và dòng trạng thái đổi theo Tone.
/// </summary>
internal sealed class FeatureViewModel : ObservableObject
{
    private bool _isOn;
    private bool _isBusy;
    private string _statusText = Strings.StatusOff;
    private string _errorText = string.Empty;

    public FeatureViewModel(Feature feature, Func<FeatureViewModel, Task> toggle, Action<Exception> onError)
    {
        Feature = feature;
        Title = Strings.FeatureTitle(feature);
        Description = Strings.FeatureDescription(feature);
        ToggleCommand = new AsyncCommand(() => toggle(this), onError);
    }

    public Feature Feature { get; }

    /// <summary>Chọn icon của huy hiệu (PathIcon.feature.dns).</summary>
    public bool IsDns => Feature == Feature.DnsRouting;

    public string Title { get; }

    public string Description { get; }

    public AsyncCommand ToggleCommand { get; }

    public bool IsOn => _isOn;

    /// <summary>MainViewModel đặt trong lúc bật/tắt: khoá toggle, hiện thanh tiến trình.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanToggle));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Tone));
        }
    }

    public bool CanToggle => !_isBusy;

    public string StatusText => _isBusy ? Strings.FeatureBusy : _statusText;

    public string ErrorText => _errorText;

    public bool HasError => _errorText.Length > 0;

    public Tone Tone => _isBusy ? Tone.Busy : HasError ? Tone.Error : _isOn ? Tone.Good : Tone.Neutral;

    /// <summary>Lý do ngắn hiện trên thẻ; chuỗi rỗng để xoá.</summary>
    public void SetError(string text)
    {
        if (_errorText == text) return;
        _errorText = text;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Tone));
    }

    /// <summary>Luôn phát IsOn, kể cả khi không đổi: ToggleSwitch đã tự lật lúc bấm, binding OneWay cần được đẩy lại giá trị thật.</summary>
    public void Update(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _isOn = IsDns ? status.DnsRouting : status.SniProxy;
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(Tone));
        UpdateStatusText(status);
    }

    /// <summary>Gọi từ timer: chỉ đổi dòng trạng thái, không đụng IsOn.</summary>
    public void UpdateStatusText(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var text = !_isOn ? Strings.StatusOff
            : Feature == Feature.SniProxy && status.ProxyPort is int port
                ? Strings.Format(nameof(Strings.StatusProxyFormat), port.ToString(CultureInfo.InvariantCulture), status.ProxyConnections)
                : Strings.StatusOn;
        if (_statusText == text) return;
        _statusText = text;
        OnPropertyChanged(nameof(StatusText));
    }
}
```

- [ ] **Bước 4: `MainViewModel.cs`** (toàn bộ file):

```csharp
using System.Collections.ObjectModel;
using Avalonia.Threading;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;
using LunaDns.Platform.Windows.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Màn hình chính (spec 4.8, KH6 3.1). Mọi lệnh gọi FeatureSet qua Task.Run vì Configurator chặn luồng.
/// FeatureStopped (luồng nền của watchdog) được chuyển về luồng UI bằng Dispatcher.UIThread.Post.
/// Overall (huy hiệu header, icon khay) tính lại mỗi khi trạng thái, việc đang chạy hoặc thông báo đổi.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);

    private readonly AppServices _services;
    private readonly IUserPrompts _prompts;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private ProbeSummary? _summary;
    private string _probeStatus = Strings.NotCheckedYet;
    private string _probeHint = string.Empty;
    private string _message = string.Empty;
    private Tone _messageTone;
    private bool _canApplyRecommended;
    private bool _anyOn;
    private int _busyCount;
    private OverallState _overall;

    public MainViewModel(AppServices services, IUserPrompts prompts, Func<Task> quit)
    {
        _services = services;
        _prompts = prompts;
        _logger = services.LoggerFactory.CreateLogger<MainViewModel>();
        // KH6 U0: PUBG ra khỏi v1, giao diện chỉ còn hai công tắc.
        Features =
        [
            new FeatureViewModel(Feature.DnsRouting, ToggleAsync, OnError),
            new FeatureViewModel(Feature.SniProxy, ToggleAsync, OnError),
        ];
        CheckCommand = new AsyncCommand(CheckAsync, OnError);
        ApplyRecommendedCommand = new AsyncCommand(ApplyRecommendedAsync, OnError);
        DisableAllCommand = new AsyncCommand(DisableAllAsync, OnError, () => _anyOn || HasAttention);
        DiagnosticsCommand = new AsyncCommand(DiagnosticsAsync, OnError);
        DismissMessageCommand = new AsyncCommand(DismissMessageAsync, OnError);
        QuitCommand = new AsyncCommand(quit, OnError);
        _services.Features.FeatureStopped += OnFeatureStopped;
        _timer = new DispatcherTimer(StatusInterval, DispatcherPriority.Background, (_, _) => RefreshStatusText());
        _overall = OverallState.From(_services.Features.GetStatus(), busy: false, attention: false);
        Refresh();
    }

    public IReadOnlyList<FeatureViewModel> Features { get; }

    public ObservableCollection<ProbeLineViewModel> ProbeLines { get; } = [];

    public AsyncCommand CheckCommand { get; }

    public AsyncCommand ApplyRecommendedCommand { get; }

    public AsyncCommand DisableAllCommand { get; }

    public AsyncCommand DiagnosticsCommand { get; }

    public AsyncCommand DismissMessageCommand { get; }

    public AsyncCommand QuitCommand { get; }

    public string ProbeStatus
    {
        get => _probeStatus;
        private set => SetProperty(ref _probeStatus, value);
    }

    /// <summary>Một câu dưới các dòng kết quả: "không thấy bị chặn" hoặc gợi ý khi có máy chủ không vào được.</summary>
    public string ProbeHint
    {
        get => _probeHint;
        private set
        {
            if (SetProperty(ref _probeHint, value)) OnPropertyChanged(nameof(HasProbeHint));
        }
    }

    public bool HasProbeHint => _probeHint.Length > 0;

    public bool CanApplyRecommended
    {
        get => _canApplyRecommended;
        private set => SetProperty(ref _canApplyRecommended, value);
    }

    public string Message => _message;

    public Tone MessageTone => _messageTone;

    public bool HasMessage => _message.Length > 0;

    public OverallState Overall
    {
        get => _overall;
        private set => SetProperty(ref _overall, value);
    }

    // Mục Nâng cao: ghi settings.json ngay; dịch vụ đang chạy giữ cấu hình cũ tới lần bật sau.
    public int SplitModeIndex
    {
        get => _services.Settings.SplitMode == SplitMode.MidSni ? 1 : 0;
        set => SaveSettings(s => s with { SplitMode = value == 1 ? SplitMode.MidSni : SplitMode.FirstByte });
    }

    public decimal? SplitDelayMs
    {
        get => _services.Settings.SplitDelayMs;
        set => SaveSettings(s => s with { SplitDelayMs = (int)Math.Clamp(value ?? 0, 0, AppSettings.MaxSplitDelayMs) });
    }

    public int DohIndex
    {
        get => _services.Settings.PreferGoogleDoh ? 1 : 0;
        set => SaveSettings(s => s with { PreferGoogleDoh = value == 1 });
    }

    private bool HasAttention => _messageTone == Tone.Error || Features.Any(f => f.HasError);

    /// <summary>Gọi sau khi cửa sổ hiện: báo kết quả hoàn tác lúc khởi động, bật timer, tự chạy "Kiểm tra" (spec 5).</summary>
    public void Start()
    {
        var startup = _services.StartupRollback;
        if (!startup.Success) ShowMessage(Strings.StartupRestoreFailed, Tone.Error);
        else if (startup.Reverted > 0) ShowMessage(Strings.Format(nameof(Strings.StartupRestoredFormat), startup.Reverted), Tone.Info);
        _timer.Start();
        CheckCommand.Execute(null);
    }

    public void ShowUnexpectedError()
    {
        ShowMessage(Strings.UnexpectedError, Tone.Error);
        Refresh();
    }

    /// <summary>Gọi khi thoát: dừng timer, bỏ đăng ký FeatureStopped.</summary>
    public void Stop()
    {
        _timer.Stop();
        _services.Features.FeatureStopped -= OnFeatureStopped;
    }

    private Task ToggleAsync(FeatureViewModel item) => RunBusyAsync([item], async () =>
    {
        item.SetError(string.Empty);
        var feature = item.Feature;
        var result = _services.Features.IsEnabled(feature)
            ? await Task.Run(() => _services.Features.DisableAsync(feature))
            : await EnableAsync(feature);
        ShowFailures([Failure(result)]);
    });

    /// <summary>NeedsProxyOverride: hỏi người dùng rồi bật lại với allowProxyOverride. Bấm Huỷ thì coi như Unchanged.</summary>
    private async Task<FeatureResult> EnableAsync(Feature feature)
    {
        var result = await Task.Run(() => _services.Features.EnableAsync(feature));
        if (result.Outcome != FeatureOutcome.NeedsProxyOverride) return result;
        if (!await _prompts.ConfirmAsync(Strings.ProxyOverrideTitle, Strings.ProxyOverrideText))
            return result with { Outcome = FeatureOutcome.Unchanged };
        return await Task.Run(() => _services.Features.EnableAsync(feature, allowProxyOverride: true));
    }

    private Task CheckAsync() => RunBusyAsync([], async () =>
    {
        ProbeStatus = Strings.CheckingStatus;
        ProbeLines.Clear();
        ProbeHint = string.Empty;
        CanApplyRecommended = false;
        _summary = null;
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        try
        {
            _summary = await Task.Run(() => _services.ProbeAsync(timeout.Token));
        }
        catch (OperationCanceledException)
        {
            ProbeStatus = Strings.CheckFailed;
        }
    });

    private void ShowSummary()
    {
        if (_summary is null) return;
        var status = _services.Features.GetStatus();
        ProbeStatus = Strings.Format(nameof(Strings.CheckedAtFormat), _summary.CheckedAt);
        ProbeLines.Clear();
        foreach (var group in _summary.Groups) ProbeLines.Add(ProbeLineViewModel.From(group, status));
        ProbeHint = _summary.AnyUnreachable ? Strings.UnreachableHint
            : _summary.Groups.All(g => g.Worst == ProbeVerdict.Ok) ? Strings.AllOk
            : string.Empty;
        CanApplyRecommended = _summary.PendingSteamRemedies(status) != ProbeRemedies.None;
    }

    /// <summary>Chỉ bật biện pháp cho Steam. DNS trước, proxy sau.</summary>
    private Task ApplyRecommendedAsync() => RunBusyAsync(Features, async () =>
    {
        if (_summary is null) return;
        var pending = _summary.PendingSteamRemedies(_services.Features.GetStatus());
        if (pending == ProbeRemedies.None)
        {
            ShowMessage(Strings.NothingToApply, Tone.Info);
            return;
        }
        foreach (var card in Features) card.SetError(string.Empty);
        var failures = new List<string?>();
        if (pending.HasFlag(ProbeRemedies.DnsRouting)) failures.Add(Failure(await EnableAsync(Feature.DnsRouting)));
        if (pending.HasFlag(ProbeRemedies.SniProxy)) failures.Add(Failure(await EnableAsync(Feature.SniProxy)));
        ShowFailures(failures);
    });

    /// <summary>"Tắt hết &amp; khôi phục" (spec 4.8): tắt mọi tính năng, app vẫn chạy.</summary>
    private Task DisableAllAsync() => RunBusyAsync(Features, async () =>
    {
        var result = await Task.Run(_services.Features.DisableAllAsync);
        if (!result.Success)
        {
            ShowMessage(Strings.RestoreFailed, Tone.Error);
            return;
        }
        foreach (var card in Features) card.SetError(string.Empty);
        ShowMessage(Strings.DisableAllDone, Tone.Good);
    });

    private async Task DiagnosticsAsync()
    {
        ShowMessage(Strings.DiagnosticsRunning, Tone.Info);
        try
        {
            var path = await Task.Run(() => ReportWriter.Write(_services.Paths, _services.LastProbe, WindowsDiagnostics.CreateDefault()));
            ShowMessage(Strings.Format(nameof(Strings.ReportSavedFormat), path), Tone.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReportFailed(_logger, ex);
            ShowMessage(Strings.Format(nameof(Strings.ReportFailedFormat), ex.Message), Tone.Error);
        }
    }

    private Task DismissMessageAsync()
    {
        ShowMessage(string.Empty, Tone.Neutral);
        return Task.CompletedTask;
    }

    /// <summary>Khoá thẻ liên quan và đặt Overall = Đang xử lý trong lúc chạy; xong thì làm mới thẻ và kết quả Kiểm tra.</summary>
    private async Task RunBusyAsync(IReadOnlyList<FeatureViewModel> cards, Func<Task> work)
    {
        _busyCount++;
        foreach (var card in cards) card.IsBusy = true;
        UpdateOverall();
        try
        {
            await work();
        }
        finally
        {
            foreach (var card in cards) card.IsBusy = false;
            _busyCount--;
            Refresh();
            ShowSummary();
        }
    }

    /// <summary>Câu lỗi cho thanh thông báo (null nếu không lỗi); đồng thời ghi lý do ngắn lên thẻ của tính năng.</summary>
    private string? Failure(FeatureResult result)
    {
        var message = ErrorMessages.For(result);
        if (message is not null) Card(result.Feature)?.SetError(Strings.Error(result.Error));
        return message;
    }

    /// <summary>Không có lỗi thì xoá thông báo cũ (như trước KH6); có thì gộp thành một thông báo mức Lỗi.</summary>
    private void ShowFailures(IEnumerable<string?> failures) =>
        ShowMessage(string.Join(" ", failures.OfType<string>()), Tone.Error);

    private FeatureViewModel? Card(Feature feature) => Features.FirstOrDefault(f => f.Feature == feature);

    /// <summary>Chuỗi rỗng luôn hạ mức về Neutral (ẩn thanh thông báo, hết Cần xử lý).</summary>
    private void ShowMessage(string text, Tone tone)
    {
        _message = text;
        _messageTone = text.Length == 0 ? Tone.Neutral : tone;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(MessageTone));
        OnPropertyChanged(nameof(HasMessage));
        UpdateOverall();
    }

    private void SaveSettings(Func<AppSettings, AppSettings> change)
    {
        try
        {
            _services.Settings = change(_services.Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Setter của binding: Avalonia nuốt exception, nên ranh giới phải nằm ở đây.
            LogSettingsSaveFailed(_logger, ex);
            ShowMessage(Strings.UnexpectedError, Tone.Error);
        }
        OnPropertyChanged(nameof(SplitModeIndex));
        OnPropertyChanged(nameof(SplitDelayMs));
        OnPropertyChanged(nameof(DohIndex));
    }

    private void OnFeatureStopped(object? sender, FeatureStoppedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            Card(e.Feature)?.SetError(Strings.FeatureStoppedShort);
            ShowMessage(ErrorMessages.Stopped(e), Tone.Error);
            Refresh();
            ShowSummary();
        });

    private void OnError(Exception ex)
    {
        LogCommandFailed(_logger, ex);
        ShowUnexpectedError();
    }

    private void Refresh()
    {
        var status = _services.Features.GetStatus();
        foreach (var feature in Features) feature.Update(status);
        _anyOn = status.DnsRouting || status.SniProxy || status.PubgMode;
        UpdateOverall(status);
    }

    private void UpdateOverall(FeatureStatus? status = null)
    {
        Overall = OverallState.From(status ?? _services.Features.GetStatus(), _busyCount > 0, HasAttention);
        DisableAllCommand.RaiseCanExecuteChanged();
    }

    private void RefreshStatusText()
    {
        var status = _services.Features.GetStatus();
        foreach (var feature in Features) feature.UpdateStatusText(status);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Lệnh trên giao diện lỗi.")]
    private static partial void LogCommandFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không ghi được báo cáo chẩn đoán.")]
    private static partial void LogReportFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không lưu được settings.json.")]
    private static partial void LogSettingsSaveFailed(ILogger logger, Exception error);
}
```

  Ghi chú cho người review: `ShowFailures` không có lỗi = `ShowMessage("")` = xoá thông báo, đúng hành vi cũ (`Message = ErrorMessages.For(result) ?? string.Empty`). Khi Kiểm tra quá 30 giây, `_summary` = null nên `ShowSummary` trong `finally` không ghi đè `CheckFailed`.

- [ ] **Bước 5: `MainWindow.axaml`** (toàn bộ file):

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:app="using:LunaDns.App"
        xmlns:vm="using:LunaDns.App.ViewModels"
        xmlns:v="using:LunaDns.App.Views"
        x:Class="LunaDns.App.Views.MainWindow"
        x:ClassModifier="internal"
        x:DataType="vm:MainViewModel"
        Title="{x:Static app:Strings.AppTitle}"
        Icon="/Assets/lunadns.ico"
        Background="{DynamicResource Brush.Window}"
        Width="560" Height="720" MinWidth="460" MinHeight="520"
        WindowStartupLocation="CenterScreen">
  <ScrollViewer>
    <StackPanel Margin="20" Spacing="16">

      <!-- Header + huy hiệu trạng thái tổng (KH6 3.1, 3.2) -->
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Spacing="4">
          <TextBlock Text="{x:Static app:Strings.AppTitle}" FontSize="24" FontWeight="SemiBold" />
          <TextBlock Classes="secondary" Text="{x:Static app:Strings.HeaderSubtitle}" TextWrapping="Wrap" />
        </StackPanel>
        <Border Grid.Column="1" Classes="pill" VerticalAlignment="Top" Margin="12,4,0,0"
                Classes.good="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                Classes.busy="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsBusy}}"
                Classes.error="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsError}}">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Classes="tone" Width="12" Height="12"
                      Classes.good="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                      Classes.busy="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsBusy}}"
                      Classes.error="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsError}}" />
            <TextBlock Classes="tone" FontWeight="SemiBold" Text="{Binding Overall.Text}"
                       Classes.good="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                       Classes.busy="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsBusy}}"
                       Classes.error="{Binding Overall.Tone, Converter={x:Static v:ToneConverters.IsError}}" />
          </StackPanel>
        </Border>
      </Grid>

      <!-- Thanh thông báo theo mức, ngay dưới header (U7) -->
      <Border Classes="banner" IsVisible="{Binding HasMessage}"
              Classes.good="{Binding MessageTone, Converter={x:Static v:ToneConverters.IsGood}}"
              Classes.warning="{Binding MessageTone, Converter={x:Static v:ToneConverters.IsWarning}}"
              Classes.error="{Binding MessageTone, Converter={x:Static v:ToneConverters.IsError}}">
        <Grid ColumnDefinitions="Auto,*,Auto">
          <PathIcon Classes="tone" VerticalAlignment="Top" Margin="0,2,8,0"
                    Classes.info="{Binding MessageTone, Converter={x:Static v:ToneConverters.IsInfo}}"
                    Classes.good="{Binding MessageTone, Converter={x:Static v:ToneConverters.IsGood}}"
                    Classes.warning="{Binding MessageTone, Converter={x:Static v:ToneConverters.IsWarning}}"
                    Classes.error="{Binding MessageTone, Converter={x:Static v:ToneConverters.IsError}}" />
          <SelectableTextBlock Grid.Column="1" Text="{Binding Message}" TextWrapping="Wrap" />
          <Button Grid.Column="2" Margin="8,0,0,0" Padding="4" VerticalAlignment="Top" Background="Transparent"
                  Command="{Binding DismissMessageCommand}"
                  ToolTip.Tip="{x:Static app:Strings.DismissMessage}"
                  AutomationProperties.Name="{x:Static app:Strings.DismissMessage}">
            <PathIcon Data="{DynamicResource Icon.Close}" Width="12" Height="12" />
          </Button>
        </Grid>
      </Border>

      <!-- Kiểm tra + kết quả theo nhóm + Áp dụng đề xuất (spec 4.8, KH6 3.2) -->
      <Border Classes="card">
        <StackPanel Spacing="8">
          <Grid ColumnDefinitions="Auto,*">
            <TextBlock Classes="section" Text="{x:Static app:Strings.CheckSectionTitle}" VerticalAlignment="Center" />
            <TextBlock Grid.Column="1" Classes="secondary" Text="{Binding ProbeStatus}" TextWrapping="Wrap"
                       TextAlignment="Right" Margin="12,0,0,0" />
          </Grid>
          <ItemsControl ItemsSource="{Binding ProbeLines}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:ProbeLineViewModel">
                <Grid ColumnDefinitions="Auto,Auto,*" Margin="0,2" AutomationProperties.Name="{Binding AutomationName}">
                  <PathIcon Classes="tone" VerticalAlignment="Top" Margin="0,2,8,0"
                            Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                            Classes.warning="{Binding Tone, Converter={x:Static v:ToneConverters.IsWarning}}"
                            Classes.error="{Binding Tone, Converter={x:Static v:ToneConverters.IsError}}" />
                  <TextBlock Grid.Column="1" Text="{Binding Group}" FontWeight="SemiBold" Margin="0,0,8,0" />
                  <TextBlock Grid.Column="2" Classes="tone" Text="{Binding Detail}" TextWrapping="Wrap"
                             Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                             Classes.warning="{Binding Tone, Converter={x:Static v:ToneConverters.IsWarning}}"
                             Classes.error="{Binding Tone, Converter={x:Static v:ToneConverters.IsError}}" />
                </Grid>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
          <TextBlock Classes="secondary" Text="{Binding ProbeHint}" TextWrapping="Wrap" IsVisible="{Binding HasProbeHint}" />
          <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Classes="accent" Command="{Binding CheckCommand}">
              <StackPanel Orientation="Horizontal" Spacing="6">
                <PathIcon Classes="button" Data="{DynamicResource Icon.Check}" />
                <TextBlock Text="{x:Static app:Strings.CheckButton}" />
              </StackPanel>
            </Button>
            <Button Content="{x:Static app:Strings.ApplyRecommendedButton}" Command="{Binding ApplyRecommendedCommand}"
                    IsVisible="{Binding CanApplyRecommended}" />
          </StackPanel>
        </StackPanel>
      </Border>

      <!-- Hai công tắc: huy hiệu icon, dải màu trái, dòng trạng thái, dòng lỗi riêng (U6) -->
      <ItemsControl ItemsSource="{Binding Features}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="vm:FeatureViewModel">
            <Border Classes="card" Margin="0,0,0,8" Padding="0">
              <Grid ColumnDefinitions="4,*">
                <Border Classes="strip"
                        Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                        Classes.busy="{Binding Tone, Converter={x:Static v:ToneConverters.IsBusy}}"
                        Classes.error="{Binding Tone, Converter={x:Static v:ToneConverters.IsError}}" />
                <Grid Grid.Column="1" Margin="12,16,16,16" ColumnDefinitions="Auto,*,Auto">
                  <Border Classes="badge" VerticalAlignment="Top" Margin="0,0,12,0">
                    <PathIcon Classes="feature" Classes.dns="{Binding IsDns}" />
                  </Border>
                  <StackPanel Grid.Column="1" Spacing="4">
                    <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                    <TextBlock Classes="secondary" Text="{Binding Description}" TextWrapping="Wrap" />
                    <StackPanel Orientation="Horizontal" Spacing="6">
                      <PathIcon Classes="tone" Width="12" Height="12" VerticalAlignment="Center"
                                Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                                Classes.busy="{Binding Tone, Converter={x:Static v:ToneConverters.IsBusy}}"
                                Classes.error="{Binding Tone, Converter={x:Static v:ToneConverters.IsError}}" />
                      <TextBlock Classes="tone" Text="{Binding StatusText}"
                                 Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}"
                                 Classes.busy="{Binding Tone, Converter={x:Static v:ToneConverters.IsBusy}}"
                                 Classes.error="{Binding Tone, Converter={x:Static v:ToneConverters.IsError}}" />
                    </StackPanel>
                    <TextBlock Classes="tone error" Text="{Binding ErrorText}" TextWrapping="Wrap" IsVisible="{Binding HasError}" />
                    <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsBusy}" MinWidth="0" MinHeight="2" Height="2" />
                  </StackPanel>
                  <ToggleSwitch Grid.Column="2" VerticalAlignment="Center" OnContent="" OffContent=""
                                IsChecked="{Binding IsOn, Mode=OneWay}" Command="{Binding ToggleCommand}"
                                IsEnabled="{Binding CanToggle}" AutomationProperties.Name="{Binding Title}" />
                </Grid>
              </Grid>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <!-- Chân trang: Tắt hết mờ khi không có gì bật (U8), Chẩn đoán thay cho Expander "Cài đặt" -->
      <Grid ColumnDefinitions="*,Auto">
        <Button Command="{Binding DisableAllCommand}" HorizontalAlignment="Left">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Classes="button" Data="{DynamicResource Icon.Restore}" />
            <TextBlock Text="{x:Static app:Strings.DisableAllButton}" />
          </StackPanel>
        </Button>
        <Button Grid.Column="1" Background="Transparent" Command="{Binding DiagnosticsCommand}">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Classes="button" Data="{DynamicResource Icon.Diagnostics}" />
            <TextBlock Text="{x:Static app:Strings.DiagnosticsButton}" />
          </StackPanel>
        </Button>
      </Grid>

      <Expander HorizontalAlignment="Stretch">
        <Expander.Header>
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Classes="button" Data="{DynamicResource Icon.Advanced}" />
            <TextBlock Text="{x:Static app:Strings.AdvancedHeader}" />
          </StackPanel>
        </Expander.Header>
        <StackPanel Spacing="8">
          <TextBlock Text="{x:Static app:Strings.SplitModeLabel}" />
          <ComboBox SelectedIndex="{Binding SplitModeIndex}" HorizontalAlignment="Stretch">
            <ComboBoxItem Content="{x:Static app:Strings.SplitModeFirstByte}" />
            <ComboBoxItem Content="{x:Static app:Strings.SplitModeMidSni}" />
          </ComboBox>
          <TextBlock Text="{x:Static app:Strings.SplitDelayLabel}" />
          <NumericUpDown Value="{Binding SplitDelayMs}" Minimum="0" Maximum="20" Increment="1" FormatString="0" />
          <TextBlock Text="{x:Static app:Strings.DohLabel}" />
          <ComboBox SelectedIndex="{Binding DohIndex}" HorizontalAlignment="Stretch">
            <ComboBoxItem Content="{x:Static app:Strings.DohCloudflareFirst}" />
            <ComboBoxItem Content="{x:Static app:Strings.DohGoogleFirst}" />
          </ComboBox>
          <TextBlock Classes="secondary" Text="{x:Static app:Strings.AdvancedHint}" TextWrapping="Wrap" />
        </StackPanel>
      </Expander>

    </StackPanel>
  </ScrollViewer>
</Window>
```

- [ ] **Bước 6: Verify** bằng lệnh verify chung → build 0 cảnh báo; App 42, Core 364 + 4, Platform 37 + 4. Chạy exe (Admin) xem nhanh: bấm toggle DNS thì thẻ khoá, có thanh tiến trình, header "Đang xử lý…" rồi "Đang bật 1/2". Bấm "Chẩn đoán" thì thanh thông báo xanh dương; nút × đóng được. Kéo cửa sổ về 460 px: chữ không bị cắt. Kiểm kỹ ở U11.

---

### Task U9: Icon app nhiều khung

**Files:** viết lại `src/LunaDns.App/Assets/make-icon.ps1`; sinh lại `src/LunaDns.App/Assets/lunadns.ico`.

**Interfaces:** Produces: hàm `Write-Ico <path> <sizes> <variant>` với variant `app`/`off`/`on`/`error` (U10 dùng ba variant cuối).

- [ ] **Bước 1: `make-icon.ps1`** (toàn bộ file; giữ encoding như file cũ, UTF-8 không BOM):

```powershell
# Sinh icon LunaDNS (KH6 U9-U10). Mỗi ICO chứa nhiều khung PNG (Windows Vista+ đọc được).
# lunadns.ico: 16/20/24/32/48/256. tray-off/on/error.ico: 16/20/24/32.
# Vẽ trên lưới 256 rồi thu về từng cỡ; khung <= 24 px dùng hình riêng (trăng dày hơn, bỏ chấm) để không nhoè ở khay.
# Chạy bằng Windows PowerShell 5.1 vì cần System.Drawing.
param([string]$OutDir = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$blue = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
$light = [System.Drawing.Color]::FromArgb(255, 241, 245, 249)
$grey = [System.Drawing.Color]::FromArgb(255, 107, 114, 128)
$red = [System.Drawing.Color]::FromArgb(255, 220, 38, 38)
$clear = [System.Drawing.Color]::Transparent

function New-Brush($color) { New-Object System.Drawing.SolidBrush $color }

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear($clear)
    return @($bmp, $g)
}

# Trăng lưỡi liềm trên lớp riêng: đĩa sáng, rồi khoét bằng đĩa trong suốt (SourceCopy) để không xoá nền xanh bên dưới.
function New-Moon([bool]$small, $color) {
    $bmp, $g = New-Canvas 256
    if ($small) {
        $g.FillEllipse((New-Brush $color), 40, 40, 176, 176)
        $cut = @(118, 16, 160, 160)
    } else {
        $g.FillEllipse((New-Brush $color), 56, 52, 150, 150)
        $cut = @(100, 30, 150, 150)
    }
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.FillEllipse((New-Brush $clear), $cut[0], $cut[1], $cut[2], $cut[3])
    $g.Dispose()
    return $bmp
}

# app: nền xanh + trăng sáng + chấm "điểm phân giải" (chỉ khung lớn). on: như app, không chấm.
# off: trăng xám, không nền. error: như on + huy hiệu đỏ có dấu ! ở góc dưới phải.
function New-Art([bool]$small, [string]$variant) {
    $bmp, $g = New-Canvas 256
    if ($variant -ne 'off') { $g.FillEllipse((New-Brush $blue), 8, 8, 240, 240) }
    $moonColor = if ($variant -eq 'off') { $grey } else { $light }
    $moon = New-Moon $small $moonColor
    $g.DrawImage($moon, 0, 0, 256, 256)
    $moon.Dispose()
    if ($variant -eq 'app' -and -not $small) { $g.FillEllipse((New-Brush $light), 150, 104, 28, 28) }
    if ($variant -eq 'error') {
        $g.FillEllipse((New-Brush $red), 136, 136, 120, 120)
        $g.FillRectangle((New-Brush $light), 186, 156, 20, 52)
        $g.FillRectangle((New-Brush $light), 186, 218, 20, 20)
    }
    $g.Dispose()
    return $bmp
}

function New-Png([int]$size, [string]$variant) {
    $art = New-Art ($size -le 24) $variant
    $bmp, $g = New-Canvas $size
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($art, 0, 0, $size, $size)
    $g.Dispose()
    $art.Dispose()
    $png = New-Object System.IO.MemoryStream
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $png.ToArray()
}

function Write-Ico([string]$Path, [int[]]$Sizes, [string]$Variant) {
    $images = @($Sizes | ForEach-Object { , (New-Png $_ $Variant) })
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$Sizes.Count)        # ICONDIR: reserved, type = icon, số khung
    $offset = 6 + 16 * $Sizes.Count
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $dim = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }                   # 0 nghĩa là 256
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)                                    # planes, bit/pixel
        $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $w.Write($image) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($Path, $out.ToArray())
    Write-Output "Đã ghi $Path ($($Sizes.Count) khung, $($out.Length) byte)"
}

Write-Ico (Join-Path $OutDir 'lunadns.ico') @(16, 20, 24, 32, 48, 256) 'app'
```

- [ ] **Bước 2: Sinh icon**: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File src/LunaDns.App/Assets/make-icon.ps1` (Windows PowerShell 5.1, không dùng pwsh 7).

- [ ] **Bước 3: Kiểm khung**:

```powershell
$b = [IO.File]::ReadAllBytes('src/LunaDns.App/Assets/lunadns.ico')
$n = [BitConverter]::ToUInt16($b, 4)
"$n khung: " + ((0..($n - 1) | ForEach-Object { $d = $b[6 + 16 * $_]; if ($d -eq 0) { 256 } else { $d } }) -join ', ')
```

  Mong đợi: `6 khung: 16, 20, 24, 32, 48, 256`.

- [ ] **Bước 4: Verify** bằng lệnh verify chung (số test như sau U6–U8). Mở exe: icon cửa sổ, taskbar, Alt+Tab nét. Mở `lunadns.ico` trong Explorer (view Large icons) để xem khung lớn.

---

### Task U10: Icon khay và tooltip theo trạng thái

**Files:** sửa `src/LunaDns.App/Assets/make-icon.ps1` (3 dòng), `src/LunaDns.App/LunaDns.App.csproj`, `src/LunaDns.App/App.axaml.cs`, `Strings.cs`, `Strings.resx`, `StringsTests.cs`; sinh `Assets/tray-off.ico`, `tray-on.ico`, `tray-error.ico`.

**Interfaces:** Consumes: `MainViewModel.Overall`, `MainViewModel.DisableAllCommand` (U6–U8); `OverallState.Kind/Active/Tooltip` (U5); `Write-Ico` (U9).

- [ ] **Bước 1: Test đếm (đỏ).** `StringsTests.cs`: `59` → `58`. Chạy `dotnet test tests/LunaDns.App.Tests --filter Every_static_string_resolves` → FAIL.

- [ ] **Bước 2: Gỡ `TrayTooltip`** khỏi `Strings.cs` và `Strings.resx` (tooltip giờ lấy từ `OverallState.Tooltip`).

- [ ] **Bước 3: Icon khay.** Thêm vào cuối `make-icon.ps1`:

```powershell
Write-Ico (Join-Path $OutDir 'tray-off.ico') @(16, 20, 24, 32) 'off'
Write-Ico (Join-Path $OutDir 'tray-on.ico') @(16, 20, 24, 32) 'on'
Write-Ico (Join-Path $OutDir 'tray-error.ico') @(16, 20, 24, 32) 'error'
```

  Chạy lại như U9 bước 2 → 4 dòng "Đã ghi …". Trong csproj, `<AvaloniaResource Include="Assets\lunadns.ico" />` → `<AvaloniaResource Include="Assets\*.ico" />`.

- [ ] **Bước 4: `App.axaml.cs`.**
  - Thêm `using System.ComponentModel;`.
  - Xoá field `IconUri`. Thêm các field:

```csharp
    private WindowIcon? _trayOff;
    private WindowIcon? _trayOn;
    private WindowIcon? _trayError;
```

  - Thay `CreateTray` bằng:

```csharp
    private void CreateTray()
    {
        var open = new NativeMenuItem(Strings.TrayOpen);
        open.Click += (_, _) => ShowWindow();
        // KH6 D6: Tắt hết ngay từ khay; Command tự xám khi không có gì để tắt.
        var disableAll = new NativeMenuItem(Strings.DisableAllButton) { Command = _viewModel!.DisableAllCommand };
        var quit = new NativeMenuItem(Strings.TrayQuit);
        quit.Click += async (_, _) => await QuitAsync();
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(disableAll);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _trayOff = LoadIcon("tray-off");
        _trayOn = LoadIcon("tray-on");
        _trayError = LoadIcon("tray-error");
        _tray = new TrayIcon { Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => ShowWindow();
        UpdateTray();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        TrayIcon.SetIcons(this, [_tray]);
    }

    private static WindowIcon LoadIcon(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://LunaDNS/Assets/{name}.ico"));
        return new WindowIcon(stream);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Overall)) UpdateTray();
    }

    /// <summary>Icon khác nhau cả về hình (có nền / không nền / có huy hiệu đỏ), không chỉ màu (KH6 3.4). Đang xử lý dùng lại icon Bật/Tắt.</summary>
    private void UpdateTray()
    {
        if (_tray is null || _viewModel is null) return;
        var overall = _viewModel.Overall;
        _tray.Icon = overall.Kind switch
        {
            OverallKind.Attention => _trayError,
            OverallKind.On => _trayOn,
            OverallKind.Busy when overall.Active.Count > 0 => _trayOn,
            _ => _trayOff,
        };
        _tray.ToolTipText = overall.Tooltip;
    }
```

  - Trong `CloseTray`, sau `_viewModel?.Stop();` thêm `if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;`.

- [ ] **Bước 5: Verify** bằng lệnh verify chung → build 0 cảnh báo; App 42, Core 364 + 4, Platform 37 + 4. Chạy exe (Admin): khay hiện trăng xám, tooltip "LunaDNS: đang tắt". Bật DNS → trong 1 giây icon nền xanh, tooltip "LunaDNS: đang bật Định tuyến DNS". Giả lập lỗi: chiếm cổng 53 trước khi bật DNS (trong cửa sổ Admin khác: `python -I -c "import socket,time;s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.bind(('127.0.0.1',53));time.sleep(120)"`), bật DNS → thanh thông báo đỏ, dòng lỗi trên thẻ DNS, icon khay có huy hiệu đỏ. Đóng thông báo → icon về trăng xám. Chuột phải khay: có "Tắt hết & khôi phục", xám khi không có gì bật.

---

### Task U11: Kiểm tay (PO chạy)

Chạy bản Debug bằng Admin. Ghi kết quả vào "Kết quả U11" bên dưới (Đạt / Không đạt + ghi chú). Mục Không đạt thì phiên chính sửa rồi PO chạy lại mục đó.

| # | Kiểm | Đạt khi |
|---|---|---|
| 1 | Theme sáng | Nền kem, thẻ trắng, chữ đọc rõ, nút Kiểm tra xanh `#2563EB` |
| 2 | Theme tối (đổi trong Windows khi app đang mở) | App đổi theo ngay; không còn chữ tối trên nền tối |
| 3 | DPI 100 % và 150 % | Icon, huy hiệu, toggle không mờ, không lệch |
| 4 | Bề rộng 460 px | Huy hiệu trạng thái tổng, hàng nút chân trang, dòng kết quả không bị cắt |
| 5 | Kiểm tra lúc bị chặn, chưa bật gì | Dòng Steam có icon ! hổ phách, "… · còn thiếu …"; hiện nút Áp dụng đề xuất |
| 6 | Áp dụng đề xuất | Hai thẻ khoá + thanh tiến trình trong lúc bật; xong thì dòng Steam ✓ xanh "… · đang né bằng …" (UX-1) |
| 7 | Không còn PUBG | Cửa sổ, hộp thoại, menu khay, tooltip không có chữ PUBG |
| 8 | Thông báo | "Đã lưu báo cáo" kiểu thông tin; lỗi cổng 53 kiểu lỗi; nút × đóng được; đường dẫn báo cáo chọn/copy được |
| 9 | Tắt hết & khôi phục | Mờ khi không có gì bật; bấm khi đang bật thì thông báo xanh lá |
| 10 | Khay | 3 icon (tắt / bật / cần xử lý) khác nhau cả về hình; tooltip đúng; menu có Tắt hết |
| 11 | Icon app | Taskbar, Alt+Tab, góc cửa sổ nét; ở 16 px vẫn thấy trăng lưỡi liềm |
| 12 | Bàn phím | Tab đi qua: Kiểm tra → (Áp dụng) → 2 toggle → Tắt hết → Chẩn đoán → Nâng cao; Space bật/tắt toggle |
| 13 | Narrator | Đọc được tên toggle ("Định tuyến DNS", "Proxy SNI") và dòng kết quả ("Steam: …") |
| 14 | Tương phản cao của Windows | Ghi lại thấy gì. Avalonia 11 không tự theo chế độ này, nên chỉ cần còn đọc được chữ; không đọc được thì ghi task riêng, không sửa trong KH6 |

#### Kết quả U11

14/11/2026: U0–U10 đã thực thi. Build -warnaserror sạch; Core 364 + 4, Platform 37 + 4, App 42. Chưa chạy exe mới (bản cũ đang chạy khoá `bin`). Bảng dưới PO điền.

14/11/2026 (PO chạy bản Debug, theme tối):

| # | Kết quả | Ghi chú |
|---|---|---|
| 1 | Đạt | PO báo đạt (theme sáng) |
| 2 | Đạt sau sửa | Lần đầu: header "Nâng cao" nền gần đen (Fluent tô header bằng `ExpanderHeaderBackground*`, không theo `Expander.Background`) và chữ mô tả chạm toggle. Sửa: đặt `ExpanderHeader*`/`ExpanderContent*` trong cả hai ThemeDictionary của `Theme.axaml`; toggle `Margin="12,0,-12,0"` (template ToggleSwitch có cột trống 12 px sau track). Build lại: xanh, PO xác nhận bằng ảnh. Đổi theme khi app đang mở: chưa kiểm |
| 3 | Đạt | PO báo đạt (DPI 100 % và 150 %) |
| 4 | Đạt | PO báo đạt (bề rộng 460 px) |
| 5 | Đạt | Steam icon ! hổ phách, "Bị lọc SNI · còn thiếu Định tuyến DNS, Proxy SNI", có nút Áp dụng đề xuất |
| 6 | Đạt (trạng thái cuối) | Sau Áp dụng: Steam ✓ xanh "Bị lọc SNI · đang né bằng Proxy SNI", huy hiệu "Đang bật 2/2", hai thẻ dải xanh lá, Proxy SNI "Đang bật · cổng 18080 · 0 kết nối". Lúc đang bật (thẻ khoá + thanh tiến trình) chưa chụp được |
| 7 | Đạt | PO báo đạt (cửa sổ, hộp thoại, menu khay, tooltip không có chữ PUBG) |
| 8 | Đạt | PO báo đạt (thông báo "Đã lưu báo cáo", nút ×, copy đường dẫn) |
| 9 | Đạt | Sáng khi có tính năng bật; bấm thì thông báo xanh lá "Đã tắt hết và trả lại cấu hình mạng như trước." có nút ×, hai thẻ về "Đang tắt", nút mờ lại, Steam quay về ! "còn thiếu …" |
| 10 | Đạt | PO báo đạt (icon khay tắt/bật khác nhau, tooltip, menu có Tắt hết) |
| 11 | Đạt | PO báo đạt (icon app ở taskbar, Alt+Tab, góc cửa sổ; 16 px vẫn thấy trăng) |
| 12 | Đạt | PO báo đạt (thứ tự Tab, Space bật/tắt toggle) |
| 13 | Đạt | PO báo đạt (Narrator đọc tên toggle và dòng kết quả) |
| 14 | Đạt | PO báo đạt (tương phản cao: chữ vẫn đọc được; không cần task riêng) |

14/11/2026: cả 14 mục Đạt; U11 xong. Build -warnaserror xanh; Core 364 + 4, Platform 37 + 4, App 42.

### 7.1 Self-review của plan (14/11/2026)

- **Phủ spec:** P1 → U5 + header U6–U8; P2 (UX-1) → U1 + U2; P3 → U2; P4 → U7; P5 → U6; P6 → U10; P7 → U9; P8 → U3 (theme tay: D3 không làm); P9, P11 → U8; P12 → mô tả D7 ở U6–U8; P10 → U0. Icon ở 3.4 → U4.
- **Lệch so với thiết kế mục 1–5, PO cần biết:**
  - U3: ba màu theme tối đổi vì trượt tương phản: nhấn (chữ) `#3B82F6` → `#60A5FA`, lỗi `#EF4444` → `#F87171`, trung tính `#64748B` → `#94A3B8`. Nút nhấn dùng `#2563EB` ở cả hai theme.
  - U6–U8 gộp một task. Thứ tự làm: U3, U4 trước U2.
  - SNI khi cả hai biện pháp đều tắt ghi "còn thiếu Định tuyến DNS, Proxy SNI" (bảng 3.2 chỉ ghi "chưa đủ").
  - Định tuyến DNS dùng icon quả địa cầu thường (Fluent không có bản có mũi tên rẽ).
  - Header không có icon trăng (Avalonia `Image` không đọc ICO). Icon app vẫn ở góc cửa sổ, taskbar.
  - "Vòng quay" lúc đang xử lý dùng `ProgressBar` mảnh (Avalonia 11 không có ProgressRing sẵn).
  - Mức Cảnh báo của thanh thông báo có style nhưng chưa có thông báo nào dùng.
- **Chuỗi tĩnh:** 50 → U0 46 → U2 49 → U5 57 → U6–U8 59 → U10 58.
- **Test mới:** Core +8 (U1), App +7 (U2), +6 (U5) → Core 364, App 42.

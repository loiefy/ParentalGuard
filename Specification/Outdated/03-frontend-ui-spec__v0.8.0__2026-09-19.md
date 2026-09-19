# 03 — Frontend / UI Spec

> Version: v0.8.0 | Trạng thái: Approved | Cập nhật: 2026-09-19

## 1. Nguyên tắc thiết kế

- `FE-001`: UI hiện đại theo Fluent Design của Windows 11 — dùng Mica/Acrylic material, dark mode tự động theo hệ thống, rounded corner, animation mượt (dùng khả năng sẵn có của WinUI 3, không tự vẽ lại).
- `FE-002`: UI dành cho **phụ huynh** phải rõ ràng, không gây hoang mang — vì đây là công cụ bảo vệ, không phải công cụ giám sát bí mật.
- `FE-003`: UI phía **trẻ em** (overlay, icon trạng thái) phải tối giản, không phán xét gay gắt, đủ rõ để hiểu hành động cần làm.

## 2. Danh sách màn hình (Screens)

| # | Màn hình | Đối tượng | Mô tả |
|---|---|---|---|
| S1 | Onboarding / Setup ban đầu | Phụ huynh | Đặt mật khẩu lần đầu, thiết lập câu hỏi/khoá khôi phục, chọn mức độ nhạy cảm mặc định |
| S2 | Dashboard chính | Phụ huynh | Trạng thái tổng quan: đang hoạt động/tạm dừng, **biểu đồ thống kê số lần chặn** (`FE-070`), truy cập cài đặt |
| S3 | Lịch sử/Audit log | Phụ huynh | Danh sách sự kiện (thời gian, hành động, app liên quan) + biểu đồ theo thời gian — không hiển thị ảnh |
| S4 | Cài đặt nâng cao | Phụ huynh | Whitelist app/domain, cấu hình hiệu năng, **nội dung thông điệp overlay** (`FE-012`), ngôn ngữ hiển thị (`FE-063`) — không có mục chỉnh ngưỡng nhạy cảm (xem `BE-091`) |
| S5 | Xác thực mật khẩu (Auth Prompt) | Phụ huynh | Modal yêu cầu mật khẩu trước hành động nhạy cảm (tạm dừng, gỡ, đổi cấu hình) |
| S6 | Quên mật khẩu / Khôi phục | Phụ huynh | Luồng recovery — xem `06-password-management-spec.md` |
| S7 | **Overlay chặn nội dung** | Trẻ em | Màn hình blur phủ browser + thông điệp + nút "Tắt nội dung" |
| S8 | Icon trạng thái giám sát | Trẻ em & Phụ huynh | Icon cố định góc màn hình, hover hiện tooltip trạng thái |
| S9 | Thông báo tạm dừng đang hoạt động | Cả hai | Banner nhỏ nhắc app đang ở chế độ tạm dừng, còn lại bao lâu |

## 3. Chi tiết màn hình quan trọng

### 3.1 S7 — Overlay chặn nội dung (quan trọng nhất)

- `FE-010`: Kích thước overlay = kích thước cửa sổ ứng dụng vi phạm (browser, video player, hoặc ứng dụng bất kỳ — xem `BE-071`), không full-screen, không che các cửa sổ khác.
- `FE-011`: Hiệu ứng blur nền (Gaussian blur hoặc acrylic blur) phủ toàn bộ nội dung cửa sổ đó.
- `FE-012`: Nội dung hiển thị trên overlay:
  - Icon cảnh báo (không dùng hình ảnh gây sợ hãi quá mức, ưu tiên tông trung tính).
  - Dòng chữ thông điệp — **ĐÃ CHỐT (v0.3.0)**: nội dung thông điệp **cho phép phụ huynh tự cấu hình** trong Cài đặt nâng cao (`S4`). Giá trị mặc định: **"Nội dung nhạy cảm được phát hiện, hãy thoát nội dung để bảo vệ chính bạn."** — nếu phụ huynh không đổi, dùng nguyên câu này. **Giới hạn độ dài (chốt v0.4.0): tối đa 255 ký tự**, hiển thị bộ đếm ký tự còn lại khi nhập, và luôn có nút "Khôi phục mặc định".
  - `FE-012a` (ĐÃ CHỐT v0.6.0): **Không chấp nhận ký tự đặc biệt/emoji** trong thông điệp overlay tự cấu hình, để tránh phá layout. Chặn ở UI ngay khi nhập (không cho gõ/paste):
    - **Cho phép**: chữ cái (Latin + tiếng Việt có dấu), chữ số, khoảng trắng, và dấu câu tiêu chuẩn dùng trong câu văn tiếng Việt: `. , ! ? : ; - ( ) " '`.
    - **Cấm**: toàn bộ emoji/pictograph Unicode, ký tự điều khiển (control character), và symbol không cần thiết cho 1 câu cảnh báo thông thường: `@ # $ % ^ & * + = < > { } [ ] | \ ~ \` _ /`.
    - Câu mặc định hiện tại ("Nội dung nhạy cảm được phát hiện, hãy thoát nội dung để bảo vệ chính bạn.") chỉ dùng dấu phẩy và dấu chấm, đều nằm trong danh sách cho phép — không bị ảnh hưởng bởi quy tắc này.
  - Nút chính: "Tắt nội dung" (primary button, màu nhấn rõ ràng) — nhãn nút cũng nằm trong hệ thống đa ngôn ngữ (`FE-060`), không cho phép phụ huynh tự đổi riêng nhãn nút (chỉ đổi được nội dung thông điệp chính) để tránh vô tình làm mất rõ ràng hành động.
- `FE-013`: Overlay luôn `topmost`, không thể click xuyên qua vùng nội dung đã blur, không có nút đóng nào khác ngoài nút chính — **ngoại trừ vùng nút đóng gốc của cửa sổ hệ điều hành**, xem `FE-016` ngay dưới đây.
- `FE-014`: Overlay phải theo dõi vị trí/kích thước cửa sổ real-time (nếu user kéo giãn hoặc di chuyển cửa sổ trước khi bấm nút).
- `FE-015`: Sau khi bấm "Tắt nội dung" → hiệu ứng chuyển tiếp ngắn (fade out) trước khi force-close, tránh cảm giác giật cục.
- `FE-016` **(mới, v0.2.0 — bổ sung theo yêu cầu review)**: Overlay **bắt buộc phải chừa nguyên vẹn vùng nút đóng cửa sổ gốc** (nút X trên title bar chuẩn Windows, hoặc control tương đương của ứng dụng) để người dùng luôn có thể tự đóng cửa sổ ứng dụng vi phạm bằng thao tác thông thường, không phụ thuộc hoàn toàn vào nút "Tắt nội dung" của app. Đây là yêu cầu **an toàn/UX bắt buộc**, không phải tuỳ chọn — lý do: tránh tình huống người dùng cảm thấy bị "khoá" trong màn hình, và tạo thêm 1 lối thoát độc lập với logic force-close của chính app (phòng trường hợp nút "Tắt nội dung" có bug).
  - `FE-016a`: Vùng overlay được vẽ **trừ đi (subtract)** một hình chữ nhật nhỏ tương ứng vị trí nút đóng thật của cửa sổ — vùng này để trong suốt hoàn toàn và cho phép click xuyên qua tới đúng control gốc của hệ điều hành/ứng dụng bên dưới (không phải overlay tự vẽ 1 nút X giả rồi tự xử lý — phải là nút X thật của cửa sổ gốc, để hành vi đóng cửa sổ giữ nguyên logic mặc định của Windows/ứng dụng đó, tránh sai khác hành vi giữa các loại app).
  - `FE-016b`: Vị trí vùng trừ ra cần được tính lại mỗi khi overlay theo dõi cửa sổ thay đổi vị trí/kích thước (đồng bộ với `FE-014`).
  - `FE-016c` **(ĐÃ CHỐT v0.3.0, con số cụ thể bổ sung v0.4.0)**: Xác định vùng nút đóng theo **3 lớp ưu tiên**, kết hợp cả độ chính xác lẫn nguyên tắc an toàn đã thống nhất ("không có nội dung khiêu dâm nào đủ nhỏ để lọt qua vùng loại trừ được mở rộng"):
    1. **Lớp chính (bắt buộc triển khai ngay Phase 1)**: Dùng **UI Automation API** (`IUIAutomation`) để lấy bounding box thực tế của control nút đóng (`ControlType = Button`, `AutomationId`/`Name`/`LocalizedControlType` gợi ý "Close") — áp dụng cho **mọi loại ứng dụng**, kể cả title bar tuỳ biến (Electron/modern UI). Quyết định: **không lùi việc này sang Phase 2** như phương án đã cân nhắc trước đó — đầu tư làm đúng ngay từ đầu vì đây là yêu cầu an toàn cốt lõi.
       - **Timeout = 150ms — ĐÃ CHỐT CHÍNH THỨC (v0.4.2)**: nếu UI Automation không trả kết quả trong 150ms, chuyển ngay sang lớp 3, không chờ thêm — để tránh làm chậm thời gian hiện overlay (mục tiêu độ trễ tổng thể ≤ 1 giây theo `08-performance-cpu-spec.md`). Giá trị này không còn ở trạng thái tạm thời/chờ benchmark — giữ nguyên trừ khi có lý do kỹ thuật cụ thể phát sinh ở System Design buộc phải điều chỉnh.
    2. **Lớp mở rộng an toàn (áp dụng lên kết quả của lớp 1)**: Sau khi có toạ độ chính xác từ UI Automation, **mở rộng thêm 1 khoảng đệm cố định** quanh vùng đó — đúng nguyên tắc đã chốt: mở rộng không tạo lỗ hổng, vì không nội dung vi phạm nào đủ nhỏ để "lách" qua khe hở đó, và dù có nhỏ đến mức đó thì cũng không ai xem được nội dung ở kích thước đó.
       - **Đề xuất ban đầu (cần benchmark xác nhận) — cập nhật v0.4.1: tạm chốt 160px rộng × 50px cao**: vùng loại trừ mặc định = **160px rộng × 50px cao** tính từ góc trên-phải cửa sổ, đo ở 100% DPI scaling, tự động nhân theo tỷ lệ DPI hiện tại của hệ thống khi máy chạy scaling khác 100%. Cơ sở tính: title bar chuẩn Windows/WinUI cao 32px, mỗi nút caption (minimize/maximize/close) rộng ~46px theo chuẩn Fluent Design, cụm 3 nút ~138px × 32px — con số tạm chốt dư ra khoảng 20px chiều rộng và ~18px chiều cao để cover các app tự vẽ title bar riêng (Chrome, Firefox, VLC...) có kích thước dao động không hoàn toàn giống chuẩn WinUI. Đây vẫn là giá trị **tạm thời** (theo quyết định của chủ dự án), cần đo thực tế trên các app cụ thể ở System Design để xác nhận hoặc tinh chỉnh trước khi khoá cứng vào code.
    3. **Lớp dự phòng (fallback tức thời)**: Nếu UI Automation không tìm được control trong 150ms — dùng ngay vùng ước lượng cố định ở mục 2 (đã có sẵn khoảng đệm an toàn) làm phương án dự phòng, đảm bảo overlay không bao giờ phải chờ UI Automation mà trễ việc che nội dung.
  - ~~`FE-016d` (cập nhật v0.3.0): Vì luôn có lớp dự phòng (lớp 3) đảm bảo overlay có ít nhất 1 vùng loại trừ hợp lệ trong mọi trường hợp, tình huống "không xác định được vị trí nút đóng" theo thiết kế cũ không còn xảy ra. Gợi ý phím tắt (ví dụ "Nhấn Alt+F4 để đóng ứng dụng") vẫn được giữ lại như 1 lớp bảo hiểm bổ sung (defense in depth) hiển thị kèm overlay, phòng trường hợp cả lớp 1 và lớp 3 đều gặp lỗi ngoài dự kiến — không phải phương án chính.~~ — **DEPRECATED, superseded by `FE-016e`** (gợi ý Alt+F4 không còn phù hợp sau quyết định chặn Alt+F4 trên overlay, xem `FE-016e`).
  - `FE-016e` **(ĐÃ CHỐT v0.7.0, 2026-09-18, supersedes `FE-016d`)**: Chủ dự án quyết định **chặn hoàn toàn phím tắt Alt+F4 (và các đường tắt hệ thống tương đương: Alt+Space → Close, click phải icon taskbar → Close nếu overlay có xuất hiện trên taskbar) trên chính cửa sổ overlay** — overlay window **bắt buộc chỉ được đóng qua đúng luồng nút "Tắt nội dung"** (có audit log + đóng cửa sổ vi phạm thật, xem `FE-015`/`BE-032`), không cho phép bất kỳ đường tắt bàn phím/hệ thống nào bypass qua lớp blur. Lý do: overlay là cửa sổ đang `topmost`/giữ focus che nội dung vi phạm (`FE-013`) — nếu Alt+F4 được phép tác động lên overlay, trẻ có thể đóng lớp blur mà không thực sự đóng ứng dụng vi phạm bên dưới, không có audit log — đây là lỗ hổng bypass nghiêm trọng đối với mục tiêu bảo vệ trẻ em của sản phẩm. Do đó gợi ý "Nhấn Alt+F4 để đóng ứng dụng" nêu ở `FE-016d` (bản cũ) bị **loại bỏ khỏi thiết kế** — không hiển thị gợi ý này kèm overlay nữa, và cũng không có lớp bảo hiểm bàn phím thay thế nào khác ở Phase 1. Nếu cả lớp 1 (UI Automation) và lớp 3 (fallback ước lượng cố định) của `FE-016c` đều thất bại ngoài dự kiến, phương án duy nhất còn lại vẫn là nút "Tắt nội dung" chính trên overlay (luôn hiển thị, không phụ thuộc vào việc xác định được vùng loại trừ hay không).
  - `FE-016f` **(ĐÃ CHỐT v0.8.0, 2026-09-19)** — **`FE-016` (và toàn bộ `FE-016a`-`FE-016e`) KHÔNG áp dụng trong chế độ overlay gộp**: Phát hiện gap khi `architecture-writer` viết `Architecture/07-overlay-architecture.md` (Đợt 2) — cơ chế vùng loại trừ 3 lớp giả định ánh xạ 1 overlay ↔ 1 cửa sổ, bị phá vỡ khi hệ thống chuyển sang **chế độ overlay gộp** (`BE-088`, kích hoạt khi có hơn 10 cửa sổ vi phạm đồng thời trên toàn hệ thống). Chủ dự án chốt trực tiếp qua 2 vòng hỏi đáp (2026-09-19): đây là **chủ đích thiết kế (deliberate), không phải thiếu sót** — nguyên văn lý do: "khi người dùng cố tình mở >10 nội dung vi phạm, hệ thống phải quyết liệt hơn". Hành vi cụ thể ở chế độ gộp (thay thế hoàn toàn `FE-010`/`FE-016` cho overlay tương ứng, xem `BE-088a`/`BE-089a` ở `02-backend-spec.md`):
    - Overlay của mỗi màn hình chuyển thành **full-screen lock**: che phủ **toàn bộ màn hình** đó (không còn khoanh vùng riêng theo kích thước từng cửa sổ vi phạm như `FE-010`), và **không chừa bất kỳ vùng trong suốt/click-through nào** cho nút đóng của các cửa sổ gốc bên dưới — khác hẳn nguyên tắc "luôn chừa vùng nút đóng gốc" ở `FE-016`.
    - Trên overlay full-screen lock, chỉ hiển thị **đúng 1 nút "Tắt nội dung" duy nhất** cho cả màn hình (khác chế độ thường ở `FE-012`/`BE-085`, nơi mỗi cửa sổ vi phạm có overlay + nút riêng). Bấm nút này đóng (force-close, cơ chế `WM_CLOSE` — `BE-032`) **toàn bộ danh sách cửa sổ đang vi phạm bị gộp cùng lúc trên toàn hệ thống**, không chỉ riêng màn hình chứa overlay vừa bấm — xem `BE-089a` (supersedes `BE-089`).
    - Ở chế độ gộp, người dùng **chỉ còn duy nhất 1 lối thoát** (nút "Tắt nội dung" trên overlay full-screen) — nguyên tắc "luôn có ít nhất 2 cách thoát độc lập nhau" của `GEN-007` **không áp dụng** cho trường hợp cụ thể này. Đây là ngoại lệ tường minh đã được chủ dự án xác nhận trực tiếp — xem `GEN-007a` ở `00-INDEX.md` mục 6. Lý do ưu tiên: mục tiêu của chế độ gộp là ngăn chặn quyết liệt hành vi cố tình mở nhiều nội dung vi phạm vượt ngưỡng, không phải bảo toàn trải nghiệm UX thoát độc lập của chế độ giám sát bình thường.

### 3.2 S8 — Icon trạng thái giám sát

- `FE-020` **(cập nhật v0.3.0)**: Icon nhỏ, vị trí **mặc định là góc dưới-phải (bottom-right)** màn hình — **không đặt mặc định ở góc trên-phải** vì đây là vùng thường trùng với nút đóng (X) của phần lớn cửa sổ ứng dụng, dễ gây nhầm lẫn thao tác hoặc bị che khuất bởi chính cửa sổ ứng dụng đang mở. Icon luôn `topmost` nhưng không chặn tương tác với nội dung bên dưới (click-through cho vùng ngoài icon).
- `FE-020a` **(mới v0.3.0)**: Cho phép người dùng **kéo-thả (drag) icon bằng chuột đến bất kỳ vị trí nào trên màn hình**. Vị trí sau khi kéo được lưu lại (per-monitor, vì app hỗ trợ multi-monitor theo `BE-080`) và giữ nguyên cho các lần khởi động sau, cho đến khi người dùng kéo lại vị trí khác. Việc kéo icon không yêu cầu xác thực mật khẩu (đây chỉ là thay đổi vị trí hiển thị, không phải tắt/ẩn icon — icon vẫn luôn hiển thị đâu đó trên màn hình, không có cách nào làm icon biến mất qua thao tác kéo).
- `FE-021`: 3 trạng thái hiển thị bằng màu sắc/icon khác nhau: Đang hoạt động (xanh) / Tạm dừng (vàng, kèm đếm ngược) / Lỗi-gián đoạn (đỏ, hiếm khi xảy ra, watchdog nên khắc phục nhanh).
- `FE-022`: Hover vào icon hiện tooltip nhỏ, không hiện thông tin nhạy cảm (không hiện số liệu chi tiết ở đây, chỉ trạng thái chung).

### 3.3 S1 — Onboarding

- `FE-030` **(cập nhật v0.3.0)**: Luồng bắt buộc theo thứ tự: Giới thiệu ngắn về cách app hoạt động (minh bạch) → Đặt mật khẩu → Thiết lập khôi phục mật khẩu → Hoàn tất, app bắt đầu chạy nền với ngưỡng nhạy cảm mặc định do đội phát triển quyết định (`BE-090`/`BE-091` — **đã bỏ bước "chọn mức độ nhạy cảm" khỏi Onboarding**, vì ngưỡng này không phơi ra cho phụ huynh chỉnh ở Phase 1).
- `FE-030a` (ĐÃ CHỐT v0.5.0, liên kết `PWD-030a` ở `06-password-management-spec.md`): Bước "Thiết lập khôi phục mật khẩu" bắt buộc phụ huynh tick checkbox xác nhận **"Tôi đã lưu lại Recovery Key"** trước khi cho bấm nút Tiếp tục/Hoàn tất. Nếu chưa tick, không cho hoàn tất Onboarding — nghĩa là app **chưa bắt đầu kích hoạt giám sát**, toàn bộ thiết lập mật khẩu coi như chưa xong.
- `FE-031`: Bắt buộc phụ huynh phải đọc và xác nhận 1 đoạn giải thích ngắn: dữ liệu không rời máy, ảnh không được lưu — tăng tính minh bạch, tránh hiểu lầm về mục đích sử dụng.

## 4. Design System (tham chiếu kỹ thuật)

| Thành phần | Lựa chọn |
|---|---|
| Framework | WinUI 3 (Windows App SDK, bản mới nhất ổn định tại thời điểm build) |
| Material nền | Mica cho cửa sổ chính, Acrylic cho overlay/modal |
| Typography | Segoe UI Variable (mặc định hệ thống Windows 11) |
| Icon set | Fluent System Icons |
| Theme | Tự động theo Windows theme (Light/Dark), không tự set cứng |
| Layout | NavigationView cho dashboard chính (pattern chuẩn Fluent cho app có nhiều màn hình con) |

## 5. Đa ngôn ngữ (Localization) — ĐÃ CHỐT v0.3.0

- `FE-060`: **Toàn bộ text hiển thị trong app** (UI dashboard, overlay, thông báo, tooltip, log hiển thị...) phải lấy từ **file tài nguyên ngôn ngữ** (resource file, ví dụ `.resw` chuẩn của Windows App SDK hoặc `.json`/`.resx` tuỳ lựa chọn kỹ thuật ở System Design) — **không hardcode chuỗi text trực tiếp trong code** dưới bất kỳ hình thức nào, kể cả text tưởng chừng cố định như tên nút hay nhãn field.
- `FE-061`: Ngôn ngữ mặc định Phase 1: **Tiếng Việt**. Kiến trúc phải sẵn sàng để thêm ngôn ngữ khác (tiếng Anh...) ở phase sau chỉ bằng cách thêm file resource mới, không cần sửa code logic.
- `FE-062`: Nội dung thông điệp overlay do phụ huynh tự cấu hình (`FE-012`) là **ngoại lệ** — đây là dữ liệu người dùng nhập (user content), không phải chuỗi hệ thống, nên lưu riêng trong `config.db` (theo đúng cơ chế ở `02-backend-spec.md`), không nằm trong file resource ngôn ngữ. Giá trị mặc định ban đầu của trường này (trước khi phụ huynh đổi) vẫn được lấy từ file resource ngôn ngữ hiện hành, để nếu sau này đổi ngôn ngữ hệ thống, câu mặc định gợi ý cũng đổi theo tương ứng.
- `FE-063`: Cài đặt ngôn ngữ (khi có nhiều lựa chọn ở phase sau) nằm trong Cài đặt nâng cao (`S4`), áp dụng toàn app ngay sau khi đổi (không cần khởi động lại nếu kỹ thuật cho phép hot-reload resource, hoặc yêu cầu khởi động lại UI nếu đơn giản hơn — quyết định cụ thể ở System Design).

## 6. Thống kê trên Dashboard (Chart) — ĐÃ CHỐT v0.3.0

- `FE-070`: Màn hình Dashboard chính (`S2`) và/hoặc màn hình Lịch sử (`S3`) **bắt buộc có biểu đồ thống kê** số lần chặn nội dung theo thời gian (không chỉ danh sách dạng bảng đơn thuần như spec v0.1.0 ban đầu).
- `FE-071`: Loại biểu đồ đề xuất: biểu đồ cột (bar chart) số lần chặn theo ngày, có thể chuyển đổi khoảng xem (7 ngày / 30 ngày). Có thể mở rộng thêm biểu đồ phân loại theo loại ứng dụng vi phạm (browser vs video player vs khác) ở phase sau nếu hữu ích, không bắt buộc Phase 1.
- `FE-072`: Dữ liệu cho biểu đồ lấy từ audit log (metadata only, đã có sẵn theo `MISC-010`) — không cần lưu trữ thêm dữ liệu riêng cho mục đích thống kê, chỉ cần aggregate lại từ log đã có.

## 7. Trạng thái rỗng & lỗi (Empty/Error states)

- `FE-040`: Màn hình lịch sử log khi chưa có sự kiện nào → hiển thị trạng thái tích cực ("Chưa phát hiện nội dung nào cần chặn"), tránh cảm giác trống trải tiêu cực.
- `FE-041`: Khi Vision Engine gặp lỗi (model load fail, GPU không hỗ trợ DirectML...) → Dashboard hiển thị cảnh báo rõ ràng kèm hướng dẫn khắc phục cơ bản, không fail âm thầm.

## 8. Accessibility

- `FE-050`: Toàn bộ UI dashboard tuân thủ chuẩn accessibility cơ bản của WinUI 3 (contrast ratio, keyboard navigation, screen reader label) — riêng overlay S7 có thể tối giản accessibility hơn vì mục đích khác (chặn, không phải điều hướng phức tạp).

## 9. Câu hỏi mở

- [ ] Kích thước vùng loại trừ nút đóng `160px×50px` (`FE-016c`) — phụ huynh đã xác nhận đồng ý giữ nguyên cách tiếp cận này (chưa có số liệu benchmark thực tế mới), nên **vẫn giữ trạng thái tạm thời**, chưa thể đánh dấu ĐÃ CHỐT số liệu cuối cùng. Vẫn cần benchmark xác nhận/tinh chỉnh thực tế trên các app phổ biến (Chrome, Firefox, VLC, PotPlayer...) ở bước System Design trước khi khoá cứng vào code. (Riêng timeout UI Automation 150ms đã chốt chính thức, không còn nằm trong mục cần benchmark lại.)

## 10. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.8.0 | 2026-09-19 | **MINOR — thêm `FE-016f`**: phát sinh khi `architecture-writer` viết `Architecture/07-overlay-architecture.md` (Đợt 2), phát hiện gap — cơ chế vùng loại trừ nút đóng 3 lớp (`FE-016`) giả định ánh xạ 1 overlay ↔ 1 cửa sổ, bị phá vỡ ở chế độ overlay gộp (`BE-088`, `02-backend-spec.md`). Chủ dự án chốt trực tiếp qua 2 vòng hỏi đáp: `FE-016` **không áp dụng** trong chế độ gộp (chủ đích thiết kế, không phải thiếu sót) — overlay chế độ gộp chuyển thành **full-screen lock** che toàn màn hình, chỉ có **đúng 1 nút "Tắt nội dung" duy nhất** đóng toàn bộ cửa sổ vi phạm bị gộp cùng lúc. Đồng bộ với `02-backend-spec.md` → v0.12.0 (`BE-088a`, `BE-089a` supersedes `BE-089`) và `00-INDEX.md` (`GEN-007a`, ngoại lệ tường minh cho nguyên tắc "luôn có ít nhất 2 cách thoát"). Archive: `Specification/Outdated/03-frontend-ui-spec__v0.7.0__2026-09-19.md` |
| v0.7.0 | 2026-09-18 | **`FE-016e` (MINOR, supersedes `FE-016d`)**: chủ dự án chốt quyết định **chặn hoàn toàn Alt+F4 (và đường tắt hệ thống tương đương) trên cửa sổ overlay** — overlay chỉ đóng được qua đúng luồng nút "Tắt nội dung" (có audit log, xem `BE-032`), không cho phép bypass lớp blur qua phím tắt. `FE-016d` (gợi ý hiển thị "Nhấn Alt+F4 để đóng ứng dụng" như lớp bảo hiểm bổ sung) đánh dấu `DEPRECATED` vì mâu thuẫn trực tiếp với quyết định mới — không còn lớp bảo hiểm bàn phím thay thế ở Phase 1, phương án cuối cùng vẫn là nút "Tắt nội dung". Đồng bộ với câu hỏi mở tương ứng đã chốt ở `11-testing-qa-process.md` mục 3.4. Archive bản cũ ở `Specification/Outdated/03-frontend-ui-spec__v0.6.1__2026-09-18.md` |
| v0.6.1 | 2026-09-17 | Chủ dự án approve toàn bộ requirement trong file này — chuyển trạng thái file từ `Draft` sang `Approved` (câu hỏi mở còn lại về `FE-016c` vẫn giữ nguyên, không tính là requirement chưa duyệt — xem mục 9) |
| v0.6.0 | 2026-09-17 | **Chốt 1 trong 2 câu hỏi mở**: `FE-012a` — không chấp nhận ký tự đặc biệt/emoji trong thông điệp overlay tự cấu hình, liệt kê rõ danh sách dấu câu được phép (đủ để câu mặc định hiện tại vẫn hợp lệ) và danh sách symbol bị cấm. Câu hỏi về kích thước vùng loại trừ nút đóng (`FE-016c`) vẫn giữ trạng thái tạm thời, chưa có số liệu benchmark thực tế nên chưa thể chốt số cuối cùng |
| v0.5.0 | 2026-09-17 | Thêm `FE-030a` — bước thiết lập khôi phục mật khẩu ở Onboarding bắt buộc tick xác nhận "Tôi đã lưu lại Recovery Key" trước khi hoàn tất, liên kết `PWD-030a` ở `06-password-management-spec.md` |
| v0.4.2 | 2026-09-17 | Chốt chính thức timeout UI Automation = 150ms (không còn ở trạng thái tạm thời/chờ benchmark) — `FE-016c` lớp 1. Kích thước vùng loại trừ 160px×50px vẫn giữ trạng thái tạm thời, chờ benchmark |
| v0.4.1 | 2026-09-17 | Tạm chốt vùng loại trừ nút đóng = **160px × 50px** (điều chỉnh chiều cao từ 48px đề xuất ban đầu lên 50px theo quyết định chủ dự án) — `FE-016c`. Vẫn giữ trạng thái "tạm thời, cần benchmark xác nhận ở System Design" |
| v0.4.0 | 2026-09-17 | **Chốt con số cụ thể**: vùng loại trừ nút đóng đề xuất 160px×48px (100% DPI) + timeout UI Automation 150ms, dựa trên chuẩn Fluent Design (title bar 32px, nút caption ~46px) — `FE-016c`. Giới hạn thông điệp overlay tối đa 255 ký tự — `FE-012` |
| v0.3.0 | 2026-09-17 | **Chốt**: icon trạng thái mặc định góc dưới-phải, cho phép kéo-thả tự do (`FE-020`, `FE-020a`); thông điệp overlay cho phép phụ huynh cấu hình, mặc định "Nội dung nhạy cảm được phát hiện, hãy thoát nội dung để bảo vệ chính bạn" (`FE-012`); toàn app dùng hệ thống resource đa ngôn ngữ, mặc định tiếng Việt (`FE-060` đến `FE-063`, section 5 mới); Dashboard bắt buộc có biểu đồ thống kê (`FE-070` đến `FE-072`, section 6 mới); `FE-016c`/`FE-016d` chốt phương án 3 lớp (UI Automation ngay Phase 1 + khoảng đệm an toàn + fallback ước lượng cố định), không còn để ngỏ sang Phase 2 |
| v0.2.0 | 2026-09-17 | Thêm `FE-016` (và các mục con) — bắt buộc overlay chừa vùng nút đóng cửa sổ gốc; tổng quát hoá `FE-010`/`FE-013` từ "browser" sang "ứng dụng vi phạm" theo phạm vi mới ở `02-backend-spec.md` |
| v0.1.0 | 2026-09-17 | Khởi tạo |

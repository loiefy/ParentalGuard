# 08 — Performance & CPU Optimization Spec

> Version: v0.5.3 | Trạng thái: Approved | Cập nhật: 2026-09-17

## 1. Mục tiêu hiệu năng

- `PERF-001`: App chạy nền không gây cảm nhận rõ rệt về độ trễ hệ thống trong tác vụ thông thường (duyệt web, gõ văn bản, xem video).
- `PERF-002`: Mức tiêu thụ CPU trung bình mục tiêu: ≤ 5% CPU (đo trên máy cấu hình tầm trung) khi ở trạng thái giám sát bình thường (không có nội dung nghi ngờ liên tục); cho phép tăng đột biến ngắn khi đang inference một frame.
- `PERF-003`: Không gây tăng nhiệt độ máy/quạt chạy to bất thường trên laptop khi chạy nền dài hạn.

## 2. Chiến lược Adaptive Frame Rate (giảm tần suất capture khi không cần thiết)

- `PERF-010`: **Không capture liên tục ở tần suất cố định cao**. Thay vào đó áp dụng tần suất thích ứng theo ngữ cảnh:

| Ngữ cảnh | Tần suất capture đề xuất |
|---|---|
| Cửa sổ foreground là ứng dụng nằm trong exclude-list (`BE-073`/`BE-073a`, ví dụ Task Manager, PowerShell, các MMC snap-in) | Tạm dừng capture hoàn toàn (chỉ theo dõi sự kiện đổi cửa sổ qua `WinEventHook`, không tốn CPU capture) |
| Cửa sổ foreground là ứng dụng bất kỳ khác (không nằm exclude-list), nội dung tĩnh (không đổi) | 1 frame / 5 giây |
| Cửa sổ foreground đang thay đổi nội dung liên tục (cuộn trang, chuyển tab, video đang phát) | Tăng lên 1 frame/giây tạm thời |
| Vừa phát hiện risk score cận ngưỡng (nghi ngờ nhưng chưa đủ chặn) | Tăng tần suất tạm thời để xác nhận nhanh hơn, giảm độ trễ phát hiện |

> **Cập nhật v0.2.0**: Sau khi mở rộng phạm vi giám sát ở `BE-071` (không giới hạn browser), bảng trên đổi từ điều kiện "browser foreground" sang "bất kỳ cửa sổ foreground nào không nằm trong exclude-list". Về mặt hiệu năng, thay đổi này **không làm tăng chi phí capture** (vẫn chỉ capture 1 cửa sổ active tại 1 thời điểm như thiết kế cũ) — chỉ thay đổi điều kiện lọc từ allow-list (browser) sang exclude-list (loại trừ ứng dụng hệ thống rõ ràng không cần thiết).

- `PERF-011`: Dùng **perceptual hashing** (ví dụ pHash) so sánh frame hiện tại với frame trước đó — nếu độ khác biệt dưới ngưỡng, bỏ qua không chạy inference (tiết kiệm phần tốn CPU nhất trong pipeline).

## 3. Chỉ capture khi cần thiết (Window-aware capture)

- `PERF-020`: Dùng `GetForegroundWindow` + kiểm tra process name có nằm trong **exclude-list** (`BE-073`, danh sách process cụ thể xem `BE-073a` ở `02-backend-spec.md`) hay không — nếu cửa sổ foreground nằm trong exclude-list (ứng dụng hệ thống chắc chắn không hiển thị nội dung media), không cần capture, có thể tạm ngưng vòng lặp Vision hoàn toàn cho đến khi có sự kiện đổi cửa sổ. Mọi cửa sổ khác (kể cả không xác định được là loại ứng dụng gì) mặc định **được giám sát**, theo đúng nguyên tắc "ưu tiên an toàn" đã chốt ở `BE-071`/`BE-072`.
- `PERF-021`: Nếu máy có nhiều màn hình, chỉ capture vùng màn hình chứa cửa sổ browser đang active, không capture toàn bộ tất cả màn hình mỗi lần (tối ưu băng thông capture + kích thước ảnh cần xử lý). Đây là tối ưu ở cấp **chọn màn hình nào** — muốn tối ưu tiếp ở cấp **crop trong màn hình đó về đúng kích thước cửa sổ**, xem `IMG-012` ở `09-image-processing-spec.md`.

## 4. Tối ưu tầng inference (AI)

- `PERF-030`: Dùng **ONNX Runtime với DirectML execution provider** để tận dụng GPU tích hợp (Intel UHD/AMD APU) hoặc GPU rời nếu có — giảm tải CPU đáng kể so với chạy thuần CPU provider.
- `PERF-031`: Fallback về CPU execution provider nếu máy không hỗ trợ DirectML, kèm cảnh báo hiệu năng có thể thấp hơn hiển thị trong Dashboard (`FE-041`).
- `PERF-032`: Chọn model kích thước nhẹ (MobileNet-based hoặc tương đương, input resolution thấp như 224×224 hoặc thấp hơn nếu độ chính xác vẫn chấp nhận được) thay vì model độ chính xác cao nhưng nặng — đánh đổi có chủ đích giữa độ chính xác và hiệu năng, cần benchmark thực tế để chọn điểm cân bằng (chi tiết quy trình đánh giá ở `11-testing-qa-process.md`). **Model cụ thể đã chốt: xem `IMG-014` ở `09-image-processing-spec.md`** (trọng số `GantMan/nsfw_model`, kiến trúc MobileNetV2, license MIT, convert sang ONNX).

## 5. Quản lý bộ nhớ (Memory Management)

- `PERF-040`: Tái sử dụng buffer RAM cho vòng lặp capture (object pooling) thay vì cấp phát mới mỗi frame — giảm áp lực Garbage Collector (.NET) và giảm rủi ro fragment bộ nhớ khi chạy dài hạn.
- `PERF-041`: Giám sát rò rỉ bộ nhớ (memory leak) là một tiêu chí bắt buộc trong test 72 giờ liên tục (`GEN` — Definition of Done ở `01-tong-quan-va-pham-vi.md`).

## 6. Giới hạn tài nguyên cấu hình được (Resource Throttling Config)

- `PERF-050` (PROPOSED): Cho phép phụ huynh chọn "Chế độ hiệu năng" trong Cài đặt nâng cao (`S4`):
  - **Cân bằng** (mặc định): theo chiến lược adaptive ở mục 2.
  - **Tiết kiệm pin**: giảm tần suất tối đa, ưu tiên hiệu năng máy hơn (đánh đổi tăng độ trễ phát hiện).
  - **Bảo vệ tối đa**: tăng tần suất capture/inference, chấp nhận tiêu tốn CPU cao hơn.
- Rủi ro cần lưu ý khi review: có nên cho phép chọn "Tiết kiệm pin" không, vì nó làm giảm hiệu quả bảo vệ — cần cân nhắc kỹ giữa tính linh hoạt và tránh phụ huynh vô tình tự làm yếu bảo vệ.

## 7. Benchmark & tiêu chí đo lường (để chốt trước khi code)

| Chỉ số | Công cụ đo | Ngưỡng chấp nhận (đề xuất, cần review) |
|---|---|---|
| CPU trung bình lúc idle-content (không nghi ngờ) | Windows Performance Counter | ≤ 5% |
| CPU đỉnh lúc inference 1 frame | Windows Performance Counter | ≤ 25% trong ≤ 200ms |
| Độ trễ từ lúc nội dung xuất hiện đến lúc overlay hiện | Đo thủ công/script test tự động | ≤ 1 giây ở chế độ Cân bằng |
| RAM sử dụng ổn định sau 24h chạy liên tục | Task Manager / .NET diagnostics | Không tăng liên tục (leak) quá X MB/giờ (cần benchmark thực tế để đặt số cụ thể) |

- `PERF-060` (ĐÃ CHỐT v0.3.0, liên kết `BE-088`/`BE-089` ở `02-backend-spec.md`): Giới hạn tối đa **10 overlay đồng thời** (tổng trên toàn hệ thống, gộp mọi màn hình) trước khi chuyển sang chế độ overlay gộp theo màn hình (degraded mode). Con số 10 là **giá trị khởi điểm dựa trên đánh giá hiệu năng sơ bộ, cần benchmark xác nhận thực tế** ở System Design (đo CPU/RAM khi render đồng thời N overlay trên máy cấu hình tầm trung) trước khi khoá cứng vào code — tương tự cách các con số kỹ thuật khác trong spec (`FE-016c`) đã được xử lý.
- `PERF-061` (ĐÃ CHỐT v0.5.0): Toàn bộ ngưỡng ở bảng trên là **giá trị đề xuất ban đầu, bắt buộc benchmark lại thực tế** ngay sau khi có bản build đầu tiên, đo trên **nhiều cấu hình máy khác nhau** (máy cũ/yếu vs máy mới/mạnh) — không chỉ 1 cấu hình tham chiếu duy nhất. Không khoá cứng số liệu vào code cho tới khi có kết quả benchmark đa cấu hình này.
- `PERF-062` (ĐÃ CHỐT v0.5.0): **Không** đo riêng benchmark hao hụt pin (%/giờ) như 1 chỉ số độc lập ở Phase 1 — CPU% đã là chỉ số đại diện đủ dùng, không cần thêm bộ đo riêng cho laptop chạy pin.
- `PERF-063` (ĐÃ CHỐT v0.5.0): **Không** cần policy tần suất capture riêng cho "đang phát video" so với "đang duyệt web" — bucket "nội dung thay đổi liên tục" đã chốt ở `PERF-010` (1 frame/giây) áp dụng chung cho cả 2 trường hợp, không tách riêng. Không cần benchmark riêng cho kịch bản video player.

## 8. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.5.3 | 2026-09-17 | Chủ dự án approve toàn bộ requirement trong file này — chuyển trạng thái file từ `Draft` sang `Approved` |
| v0.5.2 | 2026-09-17 | Liên kết `PERF-032` tới model cụ thể đã chốt (`IMG-014` mới ở `09-image-processing-spec.md`: `GantMan/nsfw_model`, MobileNetV2, MIT) |
| v0.5.1 | 2026-09-17 | Thêm liên kết chéo tại `PERF-021` tới `IMG-012` mới ở `09-image-processing-spec.md` (crop GPU-side về đúng cửa sổ, khác cấp với việc chọn màn hình ở `PERF-021`), tránh nhầm lẫn 2 khái niệm |
| v0.5.0 | 2026-09-17 | **Chốt cả 3 câu hỏi mở còn lại**: `PERF-061` — xác nhận toàn bộ ngưỡng ở mục 7 là đề xuất ban đầu, bắt buộc benchmark lại thực tế trên nhiều cấu hình máy sau khi có bản build đầu tiên. `PERF-062` — không đo riêng benchmark hao hụt pin. `PERF-063` — không cần policy tần suất riêng cho video player, dùng chung bucket "nội dung thay đổi liên tục" đã chốt ở `PERF-010`. File này không còn câu hỏi mở |
| v0.4.0 | 2026-09-17 | **Chốt con số cụ thể** ở bảng `PERF-010`: nội dung tĩnh đổi từ "1 frame/2-3 giây" xuống **1 frame/5 giây**; nội dung thay đổi liên tục đổi từ "2-4 frame/giây" xuống **1 frame/giây** |
| v0.3.0 | 2026-09-17 | Thêm `PERF-060` — chốt giới hạn khởi điểm 10 overlay đồng thời tối đa (liên kết `BE-088`/`BE-089` mới ở `02-backend-spec.md`), cần benchmark xác nhận ở System Design |
| v0.2.1 | 2026-09-17 | Cập nhật liên kết tới danh sách process cụ thể của exclude-list (`BE-073a` mới ở `02-backend-spec.md`) tại `PERF-010`, `PERF-020` |
| v0.2.0 | 2026-09-17 | Đổi chiến lược lọc capture từ allow-list (browser) sang exclude-list (loại trừ ứng dụng hệ thống), theo đúng thay đổi phạm vi ở `02-backend-spec.md` |
| v0.1.0 | 2026-09-17 | Khởi tạo |

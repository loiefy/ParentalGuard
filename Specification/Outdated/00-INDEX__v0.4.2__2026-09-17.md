# ParentalGuard — Bộ Spec Kỹ Thuật (Windows)

> **Codename dự án**: `ParentalGuard` (tạm thời, chưa chốt tên thương hiệu chính thức)
> **Nền tảng phase 1**: Windows 10/11 x64
> **Trạng thái spec**: `DRAFT v0.4.2` — đang chờ review (`01-tong-quan-va-pham-vi.md` → v0.3.0, `02-backend-spec.md` → v0.11.0, `03-frontend-ui-spec.md` → v0.6.0, `04-security-spec.md` → v0.6.0, `05-anti-uninstall-tamper-spec.md` → v0.3.1, `10-additional-mechanisms-spec.md` → v0.2.0, `11-testing-qa-process.md` → v0.2.0, `06-password-management-spec.md` → v0.3.0, `07-pause-resume-spec.md` → v0.2.0, `08-performance-cpu-spec.md` → v0.5.2, `09-image-processing-spec.md` → v0.5.1, `12-dev-process-standards.md` → v0.4.0, các file khác giữ nguyên version hiện tại)
> **Cập nhật lần cuối**: 2026-09-17

---

## 1. Mục đích bộ tài liệu này

Đây là bộ spec kỹ thuật sống (living document) cho dự án ParentalGuard — app giám sát màn hình real-time, phát hiện và chặn nội dung khiêu dâm, chạy hoàn toàn local trên máy Windows. Bộ spec này là **nguồn sự thật duy nhất** (single source of truth) trước khi bước vào thiết kế hệ thống (system design) và code.

Quy trình làm việc đã thống nhất:

```
[1] Viết Spec (đang ở bước này)
        ↓ review & chốt
[2] Thiết kế hệ thống (System Design / Architecture)
        ↓ review & chốt
[3] Code theo từng giai đoạn (feature-by-feature)
        ↓ mỗi feature → Dev → Test → Review → Approve → mới sang feature tiếp theo
```

## 2. Cấu trúc bộ spec

| # | File | Nội dung | Trạng thái |
|---|---|---|---|
| 00 | `00-INDEX.md` | File này — mục lục, changelog, quy trình bảo trì spec | Draft |
| 01 | `01-tong-quan-va-pham-vi.md` | Mục tiêu sản phẩm, phạm vi, non-goals, đối tượng người dùng, giả định | Draft |
| 02 | `02-backend-spec.md` | Kiến trúc backend/service, các module, IPC, data flow | Draft |
| 03 | `03-frontend-ui-spec.md` | UI/UX, các màn hình, design system, luồng tương tác | Draft |
| 04 | `04-security-spec.md` | Mô hình bảo mật tổng thể, threat model, nguyên tắc "local-first" | Draft |
| 05 | `05-anti-uninstall-tamper-spec.md` | Cơ chế chống gỡ, chống tamper, watchdog | Draft |
| 06 | `06-password-management-spec.md` | Đặt mật khẩu, lưu trữ an toàn, quên mật khẩu, rate-limit | Draft |
| 07 | `07-pause-resume-spec.md` | Cơ chế tạm dừng giám sát, giới hạn thời gian, log lại | Draft |
| 08 | `08-performance-cpu-spec.md` | Tối ưu CPU/GPU/pin, tần suất capture, adaptive throttling | Draft |
| 09 | `09-image-processing-spec.md` | Pipeline xử lý ảnh, nén, resize, không lưu bộ nhớ tạm | Draft |
| 10 | `10-additional-mechanisms-spec.md` | Các cơ chế bổ sung tự nghiên cứu: audit log, update, recovery key, whitelist, đa hồ sơ... | Draft |
| 11 | `11-testing-qa-process.md` | Quy trình test/kiểm thử cho từng giai đoạn code | Draft |
| 12 | `12-dev-process-standards.md` | Quy tắc quản lý dự án code: GitHub settings, quản lý secret/key, code styling | Draft |

## 3. Quy tắc đánh mã yêu cầu (Requirement ID)

Mỗi yêu cầu trong spec được gán 1 ID duy nhất để sau này map sang test case và code commit:

| Prefix | Ý nghĩa |
|---|---|
| `GEN-xxx` | General / tổng quan |
| `BE-xxx` | Backend |
| `FE-xxx` | Frontend / UI |
| `SEC-xxx` | Security |
| `ANTI-xxx` | Anti-uninstall / tamper protection |
| `PWD-xxx` | Password management |
| `PAUSE-xxx` | Pause/resume mechanism |
| `PERF-xxx` | Performance / CPU optimization |
| `IMG-xxx` | Image processing pipeline |
| `MISC-xxx` | Cơ chế bổ sung |
| `TEST-xxx` | Quy trình test |
| `DEV-xxx` | Quản lý dự án code (GitHub, secret/key, code style) |

Mỗi requirement có trạng thái: `PROPOSED` → `APPROVED` → `IMPLEMENTED` → `VERIFIED`.

## 4. Quy trình bảo trì & cập nhật spec

- **Không sửa trực tiếp requirement đã `APPROVED`** — nếu cần thay đổi, tạo requirement mới ghi chú "supersedes GEN-xxx" và đánh dấu bản cũ là `DEPRECATED`.
- Mỗi lần cập nhật file spec: bump version ở đầu file đó + thêm dòng vào Changelog bên dưới.
- Version bộ spec tổng theo semver: `MAJOR.MINOR.PATCH`
  - `PATCH`: sửa lỗi chính tả, làm rõ câu chữ, không đổi ý nghĩa yêu cầu.
  - `MINOR`: thêm requirement mới, không phá vỡ requirement cũ.
  - `MAJOR`: thay đổi kiến trúc/phạm vi lớn (ví dụ đổi ngôn ngữ, đổi mô hình threat).
- Trước khi bước sang giai đoạn "Thiết kế hệ thống", toàn bộ requirement `PROPOSED` phải được review và chuyển thành `APPROVED` hoặc `REJECTED` (có lý do).

## 5. Changelog (bộ spec tổng)

| Version | Ngày | Nội dung thay đổi |
|---|---|---|
| — | 2026-09-17 | **`12-dev-process-standards.md` → v0.4.0**: thêm nguyên tắc viết code — `DEV-026` code tinh gọn, không viết thừa, không comment rườm rà; `DEV-040`–`DEV-043` bắt buộc đánh giá fail case/exception trước khi code (cập nhật spec tương ứng trước nếu exception ảnh hưởng hành vi chưa chốt), bắt buộc duy trì và cập nhật Dependency Map (hàm ↔ file ↔ caller/callee) mỗi lần thêm/sửa/xoá hàm để hỗ trợ impact analysis khi sửa code |
| — | 2026-09-17 | **`GEN-003` → `GEN-003a` (supersedes)**: đổi stack từ .NET 8 sang **.NET 10** — .NET 8 hết hạn hỗ trợ 10/11/2026 (~2 tháng nữa), .NET 10 là LTS mới nhất (hỗ trợ đến 11/2028). Quyết định lúc chuẩn bị môi trường code, xác nhận trực tiếp bởi chủ dự án. `GEN-003` cũ đánh dấu DEPRECATED |
| — | 2026-09-17 | **`12-dev-process-standards.md` → v0.3.0**: chốt câu hỏi mở còn lại về repo — dùng chung 1 repo GitHub cho cả code lẫn tài liệu (`DEV-001a`). Đồng bộ `Architecture/01-tong-quan-kien-truc.md` (`ADR-11`) |
| — | 2026-09-17 | **`12-dev-process-standards.md` → v0.2.0**, **`04-security-spec.md` → v0.6.0**: **chốt bỏ hẳn kế hoạch bán thương mại** — dự án chuyển 100% sang free/open-source. Sửa `SEC-030`/`DEV-012`: bỏ EV Code Signing Certificate tự mua (không còn ngân sách), thay bằng chứng chỉ OV miễn phí qua chương trình SignPath Foundation cho open-source (private key do SignPath tự giữ trên HSM, dự án không cầm key nào cả) |
| — | 2026-09-17 | **Thêm file mới `12-dev-process-standards.md` → v0.1.0**: quy tắc quản lý dự án code — GitHub settings (branch protection, signed commits, SECURITY.md...), quản lý secret/key (trọng tâm chứng chỉ EV code signing vì app zero-network không có secret runtime truyền thống), code styling (`.editorconfig`, nullable, security analyzers), liên kết quy trình Agent đã chốt ở `11`. Thêm prefix ID mới `DEV-xxx` vào mục 3, thêm dòng vào bảng cấu trúc mục 2 |
| — | 2026-09-17 | **`11-testing-qa-process.md` → v0.2.0**: chốt 2 câu hỏi mở của file này — `TEST-002` tự động hoá Dev/Test/Debug/Report bằng Claude Code Agent (không phải CI script truyền thống), chi tiết để ở System Design; `TEST-003` chủ dự án là người Approve duy nhất. File này không còn câu hỏi mở (còn `03`, `04`, `09` vẫn còn câu hỏi mở treo, chờ benchmark thực tế/quyết định kỹ thuật ở System Design) |
| — | 2026-09-17 | **`10-additional-mechanisms-spec.md` → v0.2.0** (chốt cả 2 câu hỏi mở, file hết open question), **`02-backend-spec.md` → v0.11.0**, **`04-security-spec.md` → v0.5.0**, **`01-tong-quan-va-pham-vi.md` → v0.3.0**, **`05-anti-uninstall-tamper-spec.md` → v0.3.1**: duyệt bảng ưu tiên additional mechanisms, với 2 điều chỉnh quan trọng — **`MISC-020` (auto-update) bị REJECTED hoàn toàn**, app chuyển sang **tuyệt đối zero internet** không ngoại lệ (`GEN-034`, `SEC-001a`), cập nhật chỉ qua cài lại thủ công; **`MISC-080` (Emergency Override) bị REJECTED**, loại khỏi roadmap vì nhu cầu đã được tính năng Tạm dừng giải quyết |
| — | 2026-09-17 | **`09-image-processing-spec.md` → v0.5.0**, **`08-performance-cpu-spec.md` → v0.5.2**: chốt model AI cụ thể — `IMG-014` dùng trọng số `GantMan/nsfw_model` (MobileNetV2, MIT license), convert sang ONNX, không đóng gói runtime Python. `IMG-015` — an toàn network đảm bảo bởi 2 lớp độc lập (model là dữ liệu tĩnh + chặn network tầng OS đã có ở `SEC-016`-`018`), thêm hardening tắt telemetry ONNX Runtime. Liên kết `PERF-032` tới lựa chọn này |
| — | 2026-09-17 | **`09-image-processing-spec.md` → v0.4.0**: chốt 2/3 câu hỏi mở — `IMG-001a` không có ngoại lệ thumbnail trong audit log; `IMG-013` làm rõ tường minh chiều risk score (càng cao càng chắc chắn vi phạm); hoãn dataset test cho video player sang giai đoạn triển khai hệ thống. Câu hỏi về phương pháp benchmark chọn ngưỡng risk score cụ thể vẫn còn mở |
| — | 2026-09-17 | **`09-image-processing-spec.md` → v0.3.0**, **`08-performance-cpu-spec.md` → v0.5.1**: thêm `IMG-012` — crop GPU-side về đúng bounding rect cửa sổ trước khi resize (chèn giữa Bước 1/Bước 2 cũ trong pipeline), lợi ích kép về hiệu năng lẫn độ chính xác phát hiện với cửa sổ nhỏ trên màn hình lớn. Thêm liên kết chéo ở `PERF-021` để phân biệt rõ với việc "chọn màn hình nào" |
| — | 2026-09-17 | **`08-performance-cpu-spec.md` → v0.5.0**: chốt 3 câu hỏi mở cuối cùng của file — `PERF-061` xác nhận cần benchmark lại toàn bộ ngưỡng hiệu năng trên nhiều cấu hình máy sau bản build đầu tiên; `PERF-062` không đo riêng hao hụt pin; `PERF-063` không cần policy tần suất riêng cho video player. File này không còn câu hỏi mở |
| — | 2026-09-17 | **`08-performance-cpu-spec.md` → v0.4.0**: chốt lại con số adaptive frame rate ở `PERF-010` — nội dung tĩnh giảm xuống 1 frame/5 giây (từ 1 frame/2-3 giây), nội dung thay đổi liên tục giảm xuống 1 frame/giây (từ 2-4 frame/giây) |
| — | 2026-09-17 | **`05-anti-uninstall-tamper-spec.md` → v0.3.0**, **`02-backend-spec.md` → v0.10.0**: chốt 3 câu hỏi mở cuối của file 05 — không bổ sung cảnh báo chủ động cho sự kiện kill-restart (`ANTI-061`, chỉ log thụ động); Phase 1 không đầu tư chống Safe Mode with Networking bypass; sau khi fallback config mặc định, Service tự động ghi đè `config.db` + gửi Windows Toast Notification chủ động cảnh báo phụ huynh (`ANTI-070a`/`ANTI-070b`, `BE-061a`/`BE-061b`). File 05 không còn câu hỏi mở |
| — | 2026-09-17 | **`03-frontend-ui-spec.md` → v0.6.0**: chốt `FE-012a` — không chấp nhận ký tự đặc biệt/emoji trong thông điệp overlay tự cấu hình, liệt kê rõ danh sách dấu câu được phép/bị cấm. Câu hỏi mở còn lại (kích thước vùng loại trừ nút đóng) vẫn giữ trạng thái tạm thời do chưa có benchmark thực tế |
| — | 2026-09-17 | **`02-backend-spec.md` → v0.9.0**, **`08-performance-cpu-spec.md` → v0.3.0**: chốt 2 câu hỏi mở cuối về overlay đồng thời — z-index overlay khớp z-index cửa sổ vi phạm gốc (`BE-087`); giới hạn tối đa 10 overlay đồng thời toàn hệ thống, vượt quá chuyển sang overlay gộp theo màn hình (`BE-088`, `PERF-060`); trong chế độ gộp, nút "Tắt nội dung" ở bất kỳ overlay nào force-close toàn bộ cửa sổ vi phạm (`BE-089`). `02-backend-spec.md` không còn câu hỏi mở |
| — | 2026-09-17 | **`02-backend-spec.md` → v0.8.0**: thêm remote desktop/video call (mstsc, TeamViewer, AnyDesk, Zoom, Google Meet) vào danh sách test ưu tiên `BE-075` — không phải exclude-list, nhóm này vẫn giám sát bình thường, chỉ cần test thêm do rủi ro fullscreen exclusive giống `BE-074` |
| — | 2026-09-17 | **`02-backend-spec.md` → v0.7.0**, **`08-performance-cpu-spec.md` → v0.2.1**: thêm `BE-073a` — danh sách khởi điểm cụ thể process đưa vào exclude-list (Task Manager, PowerShell, MMC snap-ins, Notepad classic...), kèm danh sách tường minh các ứng dụng **không được** loại trừ dù trông giống tiện ích hệ thống (File Explorer, Settings, Search UI...) vì có khả năng hiển thị ảnh/video |
| — | 2026-09-17 | **`07-pause-resume-spec.md` → v0.2.0**: chốt 2 câu hỏi mở — mốc "Hết ngày hôm nay" dùng cứng 23:59 giờ hệ thống, không cấu hình "giờ đi ngủ" riêng (`PAUSE-002a`); Phase 1 chỉ hỗ trợ tạm dừng toàn bộ, không phân biệt theo app/browser (`PAUSE-002b`). File này không còn câu hỏi mở |
| — | 2026-09-17 | **`06-password-management-spec.md` → v0.3.0**: chốt nốt câu hỏi mở cuối — `PWD-035` định nghĩa rõ "ký tự đặc biệt" bị cấm (liệt kê danh sách symbol), xác nhận chữ cái tiếng Việt có dấu là chữ cái hợp lệ, không phải ký tự đặc biệt. File này không còn câu hỏi mở |
| — | 2026-09-17 | **`06-password-management-spec.md` → v0.2.0**, **`03-frontend-ui-spec.md` → v0.5.0**: chốt 3 câu hỏi mở về mật khẩu — giới hạn mật khẩu tối đa 50 ký tự (`PWD-002a`); bắt buộc tick xác nhận "Tôi đã lưu lại Recovery Key" trước khi hoàn tất thiết lập, không xác nhận thì chưa kích hoạt giám sát (`PWD-030a`, `FE-030a`); câu hỏi bảo mật bổ sung chuyển sang Phase 2, kèm quy tắc chuẩn hoá câu trả lời (`PWD-034`, `PWD-035` mới) |
| — | 2026-09-17 | **`02-backend-spec.md` → v0.6.0**, **`05-anti-uninstall-tamper-spec.md` → v0.2.0**, **`04-security-spec.md` → v0.4.2**: chốt hành vi fail-secure khi file cấu hình `config.db` bị hỏng/không đọc được — `Service` không được dừng hoạt động, tự động fallback dùng bộ cấu hình mặc định hard-code trong code (giám sát luôn BẬT). Requirement mới `BE-061`, `ANTI-070`, `SEC-040a` (liên kết 2 chiều giữa 3 file) |
| — | 2026-09-17 | **`02-backend-spec.md` → v0.5.0 (quan trọng)**: phát hiện & sửa lỗi kiến trúc tiềm ẩn — `Vision` phải được `Service` khởi chạy vào đúng session tương tác của user qua `WTSQueryUserToken`+`CreateProcessAsUser` (không phải `CreateProcess` thường), do Session 0 Isolation của Windows. **`04-security-spec.md` → v0.4.1**: liên kết ngoại lệ Least Privilege tương ứng |
| — | 2026-09-17 | **`04-security-spec.md` → v0.4.0** + **`02-backend-spec.md` → v0.4.0**: chốt nguyên tắc "single-purpose" tường minh cho `Vision` — chỉ capture+phân tích+trả kết quả, cấm tuyệt đối network/registry/file write ngoài đọc model/spawn process/giao tiếp trực tiếp với Overlay-UI (`SEC-017`, `SEC-018`), đồng bộ mô tả module `Vision` ở backend spec (`BE-020`). Đồng thời sửa lỗi version header bị lệch ở `02-backend-spec.md` |
| — | 2026-09-17 | **`04-security-spec.md` → v0.3.0 (đính chính)**: sửa lại lý do sandbox cho Vision — "ảnh bẫy khai thác decoder" không áp dụng vì Vision chỉ capture pixel thô qua DXGI, không giải mã file ảnh nén. Hạ AppContainer đầy đủ từ "Phase 2 đã lên kế hoạch" xuống backlog/optional; mức tối thiểu (network block + process tách biệt) vẫn bắt buộc Phase 1 |
| — | 2026-09-17 | **`04-security-spec.md` → v0.2.0**: chốt kịch bản "trẻ có quyền admin" ngoài phạm vi Phase 1 (có lý do kỹ thuật nền tảng + roadmap Phase 2 gồm kernel driver/MDM); chốt AppContainer cho Vision theo 2 mức (tối thiểu bắt buộc Phase 1, đầy đủ đẩy Phase 2 do rủi ro tương thích DirectML) |
| v0.4.2 | 2026-09-17 | Chốt chính thức timeout UI Automation = 150ms (không còn tạm thời) — sửa `03` |
| v0.4.1 | 2026-09-17 | Tạm chốt vùng loại trừ nút đóng = 160px × 50px (điều chỉnh chiều cao 48px → 50px) — sửa `03`, vẫn đánh dấu "tạm thời, chờ benchmark ở System Design" |
| v0.4.0 | 2026-09-17 | **Chốt con số kỹ thuật cụ thể** (đề xuất ban đầu, chờ benchmark xác nhận ở System Design): vùng loại trừ nút đóng 160px×48px (100% DPI) dựa trên chuẩn Fluent Design (title bar 32px, nút caption ~46px), timeout UI Automation 150ms trước khi fallback; giới hạn thông điệp overlay tối đa 255 ký tự — sửa `03` |
| v0.3.0 | 2026-09-17 | **Chốt hàng loạt quyết định mở**: bắt buộc multi-monitor, mỗi cửa sổ vi phạm 1 overlay riêng, ngưỡng risk score do dev quyết định (không phơi UI) — sửa `02`. Icon trạng thái mặc định góc dưới-phải + kéo-thả tự do, thông điệp overlay cho phép phụ huynh cấu hình, hệ thống đa ngôn ngữ toàn app (mặc định Việt), Dashboard bắt buộc có biểu đồ, vùng nút đóng dùng UI Automation ngay Phase 1 (3 lớp: UI Automation + khoảng đệm an toàn + fallback) — sửa `03`. Xem changelog riêng từng file để biết chi tiết requirement ID |
| v0.2.0 | 2026-09-17 | **Mở rộng phạm vi giám sát**: từ whitelist browser sang giám sát cửa sổ foreground bất kỳ (video player, ứng dụng bất kỳ) — sửa `01`, `02`, `03`, `08`, `09`. **Thêm yêu cầu UX/an toàn**: overlay bắt buộc chừa vùng nút đóng cửa sổ gốc (`FE-016`) — sửa `03`. Xem changelog riêng từng file để biết chi tiết requirement ID thay đổi |
| v0.1.0 | 2026-09-17 | Khởi tạo toàn bộ bộ spec draft, dựa trên các quyết định kiến trúc đã thảo luận (Windows-first, C#/.NET 8 + WinUI 3, multi-process architecture) |

## 6. Các quyết định nền tảng đã chốt (carry-over từ thảo luận trước)

Các mục dưới đây được coi là **input cố định** cho toàn bộ spec, trừ khi có quyết định thay đổi rõ ràng:

- `GEN-001`: Nền tảng đầu tiên là Windows 10/11 x64. iOS/Android/macOS là phase sau, không nằm trong scope spec này.
- `GEN-002`: Toàn bộ xử lý ảnh/AI chạy **local**, không có bất kỳ network call nào trong pipeline capture→classify.
- ~~`GEN-003`: Stack kỹ thuật: C# / .NET 8, UI dùng WinUI 3 (Windows App SDK), AI inference dùng ONNX Runtime, capture dùng Desktop Duplication API (DXGI).~~ — **DEPRECATED, superseded by `GEN-003a`**.
- `GEN-003a` (ĐÃ CHỐT 2026-09-17, supersedes `GEN-003`): Stack kỹ thuật: **C# / .NET 10** (đổi từ .NET 8 — lý do: .NET 8 hết hạn hỗ trợ 10/11/2026, chỉ còn ~2 tháng tính đến lúc bắt đầu code; .NET 10 là LTS mới nhất, hỗ trợ đến 11/2028, máy dev cũng đã sẵn .NET 10 runtime), UI dùng WinUI 3 (Windows App SDK), AI inference dùng ONNX Runtime, capture dùng Desktop Duplication API (DXGI).
- `GEN-004`: Kiến trúc chia nhiều process/module tách biệt theo nguyên tắc least-privilege (UI, Overlay, Watchdog Service, Vision Engine).
- `GEN-005`: App không được gỡ cài đặt nếu không có xác thực mật khẩu quản trị (phụ huynh).
- `GEN-006` (thêm v0.2.0): Phạm vi giám sát không giới hạn ở trình duyệt — bao gồm video player và về nguyên tắc mọi ứng dụng hiển thị nội dung trực quan, dựa trên chiến lược giám sát theo cửa sổ foreground (`BE-071`).
- `GEN-007` (thêm v0.2.0): Overlay chặn nội dung không bao giờ được che khuất vùng nút đóng cửa sổ gốc — người dùng luôn có ít nhất 2 cách thoát độc lập nhau (`FE-016`).

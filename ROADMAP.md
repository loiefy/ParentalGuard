# ParentalGuard — Development Roadmap

> **Trạng thái**: `APPROVED` — chủ dự án đã phê duyệt 2026-09-17. Bắt đầu Đợt 0.
> Tạo: 2026-09-17

Tài liệu này trả lời câu hỏi **"làm cái gì trước, cái gì sau"** — khác `Specification/` (WHAT) và `Architecture/` (HOW), nên không theo quy tắc archive/tuần tự của 2 thư mục đó. Cập nhật trực tiếp khi thứ tự ưu tiên thay đổi, có changelog ngắn ở cuối file.

## 1. Mục đích

- Chia toàn bộ tính năng đã `APPROVED` trong `Specification/` thành nhóm theo domain, dễ tra cứu.
- Sắp xếp thứ tự implement theo đúng ưu tiên chủ dự án đã chốt: **app cơ bản có pipeline xử lý ảnh chạy được trước**, các tính năng bảo vệ/UI/vận hành bổ sung sau.
- Đảm bảo thứ tự này không buộc phải refactor lớn khi thêm tính năng — bằng cách xây đúng "khung xương" (process/IPC/security boundary) ở Đợt 0 trước khi viết logic nghiệp vụ.

## 2. Nguyên tắc kiến trúc xuyên suốt để đảm bảo linh hoạt khi mở rộng

Lý do phải có Đợt 0 riêng trước khi đụng vào pipeline ảnh: nếu ranh giới giữa các module (process, IPC message, state) không rõ ngay từ đầu, mỗi tính năng thêm sau (password, pause, anti-tamper...) sẽ phải sửa lại code lõi thay vì cắm thêm vào. Cụ thể:

- **IPC message schema có khả năng mở rộng** (`03-ipc-communication.md` sẽ chi tiết hoá): định nghĩa message type dạng có version/field mở rộng được ngay từ đầu, không hard-code cấu trúc chỉ đủ cho use-case hiện tại.
- **`Overlay` chỉ nhận "danh sách rect cần che" từ `Service`, không tự biết LÝ DO che.** Nhờ vậy tính năng sau này (whitelist, pause, chế độ overlay gộp `BE-088`) chỉ cần thay đổi logic phía `Service` lọc danh sách trước khi gửi xuống — không đụng code `Overlay`.
- **`Vision` là hàm phân loại thuần theo từng frame, không giữ state nghiệp vụ.** Adaptive frame rate (`PERF-*`), pause/resume sau này chỉ thay đổi TẦN SUẤT gọi `Vision` từ `Service`, không sửa logic bên trong `Vision`.
- **`Service` giữ 1 config/state trung tâm, tính năng mới CHỈ thêm field/handler, không tái cấu trúc state hiện có** — state đặt tên và phân nhóm theo domain (`AuthState`, `MonitoringState`, `PauseState`...) ngay từ đầu dù ban đầu chỉ `MonitoringState` có dữ liệu thật.
- **Dependency Map (`DEV-042`/`DEV-043`) cập nhật từ hàm đầu tiên** — để lúc thêm tính năng sau, `feature-dev` tra map thay vì đọc lại toàn bộ codebase, đúng mục tiêu "không ảnh hưởng quá nhiều" bạn nêu.
- **Security boundary xây 1 lần, không bổ sung sau**: cô lập process, chặn network `Vision` (WFP), ACL + ký HMAC cho Named Pipe — làm ở Đợt 0 cùng lúc với skeleton, vì đây là ranh giới kiến trúc rất tốn kém nếu phải bolt-on sau khi đã có nhiều tính năng phụ thuộc vào IPC.

## 3. Danh sách tính năng theo domain

| Domain | Requirement ID chính | Architecture file phụ thuộc (`Architecture/`) |
|---|---|---|
| Core Platform / Process Skeleton | `GEN-004`, `BE-010`–`015`, `BE-023a/b`, `BE-040`, `BE-050`/`051`, `BE-060`/`061`/`061a`/`061b` | `02`, `03`, `04`, `06` |
| Image Processing Pipeline | `IMG-001`–`IMG-041`, `BE-020`–`023`, liên kết `PERF-011`/`012`/`020`/`021`/`030`/`031` | `05` |
| Overlay & Force-close | `BE-030`–`033`, `BE-080`–`089`, `FE-015`/`016` | `02`, `03`, `08` |
| Password & Authentication | `PWD-001`–`PWD-035` | `04`, `06` |
| Anti-uninstall / Tamper | `ANTI-010`–`ANTI-070` | `07` |
| Pause/Resume | `PAUSE-001`–`PAUSE-030` | `02` |
| Frontend / UI Dashboard | `FE-0xx` (S1–S9), đa ngôn ngữ, chart, accessibility | `08` |
| Performance / CPU | `PERF-0xx` | `05`, `02` |
| Additional mechanisms | `MISC-010/030/050/070/090` (Phase 1 theo bảng ưu tiên đã chốt; `MISC-020`/`080` REJECTED, `MISC-040`/`060` Phase 2/ghi nhận) | `04`, `07` |
| Security hardening | `SEC-0xx` | `06` |
| Dev/Release infra | `DEV-0xx` | `09`, `10` (agent đã scaffold ở `.claude/agents/`) |

## 4. Roadmap theo Đợt (Milestone)

Mỗi Đợt vẫn chạy Feature Gate **theo từng tính năng con** bên trong nó (`Dev feature-dev` → `Self-test` → `test-runner`/`security-privacy-auditor` → `debugger` nếu fail → `release-reporter` → Approve chủ dự án), không approve nguyên cả Đợt 1 lần.

### Đợt 0 — Nền tảng (Foundation & Security-by-construction)
**Mục tiêu**: dựng khung xương 5 process + IPC + security boundary — chưa có tính năng người dùng thấy được, nhưng là điều kiện bắt buộc để Đợt 1 không phải sửa lại.
- Skeleton `Service`/`Vision`/`Overlay` thật (thay README placeholder), `Vision` spawn đúng qua `CreateProcessAsUser` vào session tương tác (`BE-023a/b`).
- IPC Named Pipe + Protobuf, ACL theo SID, ký HMAC (`BE-050`/`051`).
- Chặn network tầng OS cho `Vision` (WFP outbound rule) — `SEC-016`–`018`.
- `config.db` (SQLite) + DPAPI, fallback mặc định fail-secure khi hỏng (`BE-061`/`061a`/`061b`).
- Heartbeat `Service`↔`Vision`, `Service`↔`Overlay` (`BE-040`).
- **Cần xong trước**: `Architecture/02`, `03`, `04`, `06` (viết & approve qua `architecture-writer`).

### Đợt 1 — Image Processing Pipeline lõi ⭐ ƯU TIÊN #1 (app cơ bản có xử lý ảnh chạy được)
**Mục tiêu**: 1 cửa sổ, 1 màn hình, happy path đầy đủ end-to-end — capture thật, chặn thật.
- Pipeline đủ 7 bước: Capture (DXGI) → Crop GPU-side (`IMG-012`) → Resize → Normalize → Inference ONNX (`GantMan/nsfw_model`, `IMG-014`) → Zero-out buffer (`IMG-003`) → Quyết định theo ngưỡng (`IMG-013`).
- Foreground-window detection cơ bản (`BE-071`), exclude-list khởi điểm (`BE-073a`).
- Overlay tối giản: blur đúng rect cửa sổ, nút "Tắt nội dung" hoạt động (chưa cần vùng chừa nút đóng tinh vi — để Đợt 2).
- Compliance check tự động `IMG-040`/`IMG-041` (bắt buộc pass 100% — `security-privacy-auditor`) chạy ngay từ Đợt này, không để dồn về sau.
- **Cần xong trước**: `Architecture/05` (và `02`/`03`/`06` từ Đợt 0).

### Đợt 2 — Overlay hoàn chỉnh
- Vùng loại trừ nút đóng 3 lớp: UI Automation + khoảng đệm + fallback (`FE-016`).
- Đa cửa sổ vi phạm đồng thời (`BE-084`–`086`), z-index khớp cửa sổ gốc (`BE-087`), giới hạn 10 overlay + chế độ gộp (`BE-088`/`089`).
- Multi-monitor (`BE-080`–`083`).

### Đợt 3 — Password & Authentication
- Đặt mật khẩu lần đầu, Argon2id, memory hygiene (`PWD-0xx` mục 2/3/7).
- Auth modal cho hành động nhạy cảm (`PWD` mục 4).
- Recovery key, rate-limit, đổi mật khẩu (`PWD` mục 5/6).
- **Lý do đặt sau Overlay, trước Anti-tamper/Pause**: Anti-tamper và Pause đều cần xác thực mật khẩu làm gate — phải có trước.

### Đợt 4 — Anti-uninstall / Tamper protection
- Dual Watchdog (`ANTI-010`+), ACL file/registry (`ANTI-030`/`031`), chặn gỡ qua Control Panel (`ANTI-020`).
- Rate-limit & cảnh báo tấn công liên tục (`ANTI-060`), hoàn thiện đầy đủ luồng fail-secure đã có skeleton ở Đợt 0 (`ANTI-070`).

### Đợt 5 — Pause/Resume
- Tạm dừng có giới hạn tần suất, tự động resume, banner nhắc (`PAUSE-0xx`).

### Đợt 6 — Dashboard UI hoàn chỉnh
- S1 Onboarding, S2 Dashboard + chart, S3 Audit log UI, S4 Settings, S5/S6 Auth/Recovery UI polish.
- Đa ngôn ngữ (`FE` mục 5), accessibility, empty/error states.

### Đợt 7 — Performance tuning chính thức
- Adaptive frame rate (`PERF-010`+), window-aware capture, resource throttling config, benchmark đa cấu hình máy (`PERF-061`).

### Đợt 8 — Additional mechanisms & hardening bổ sung
- Audit log tamper-evident hash-chain hoàn chỉnh (`MISC-010`), whitelist/báo cáo False Positive từ UI (`MISC-030`), self-diagnostic (`MISC-050`), verify checksum model AI (`MISC-090`), Behavior Disclosure công khai (`MISC-070`), crash dump policy, AV/SmartScreen mitigation (`SEC` mục 5/6).

### Đợt 9 — Release/CI hoàn thiện
- GitHub Actions CI (build + `dotnet format` + test), bật branch protection (`DEV-002`), SignPath signing pipeline (`DEV-012`), Dependabot (`DEV-005`), quyết định signed commits (`DEV-004`, câu hỏi mở).

## 5. Theo dõi tiến độ

- [x] Đợt 0 — Nền tảng (build sạch + 11/11 test pass, xác nhận lại 2026-09-18; runtime thật trên máy Windows có session tương tác vẫn cần chủ dự án tự kiểm tra theo `feedback-no-perfile-review-gate`)
- [ ] Đợt 1 — Image Processing Pipeline lõi ⭐ (**code hoàn thành + đã qua toàn bộ sandbox gate tính tới 2026-09-19**: build 0 warning/0 error, test 46/46 pass. Vòng re-verify 2026-09-19 phát hiện 1 FAIL cứng mới — `FrameClassificationPipeline.ProcessFrame` giữ pixel buffer chưa-zero suốt lúc ONNX inference chạy (vi phạm `IMG-003`) — đã được feature-dev sửa (nested try/finally đúng `Architecture/05` mục 6) và **security-privacy-auditor re-audit độc lập vòng 2 xác nhận RESOLVED** (đọc code thật + chạy lại test thật). Alt+F4 block, BE-032, zero-out ban đầu, crash-guard exit code 19 đều đã verify PASS ở mức code-review/unit-test. `release-reporter` đã tổng hợp báo cáo 2026-09-19. **Việc còn lại BẮT BUỘC trước khi tick `[x]` (theo `TEST-001`, không có ngoại lệ)**: `IMG-040`/`IMG-041` compliance scan (quét %TEMP%/disk/clipboard/heap-dump sau ≥30 phút chạy thật) — cần chủ dự án tự chạy trên máy Windows thật (session tương tác, GPU, DXGI thật, model `.onnx` thật, `dotnet-gcdump`), sandbox agent không thể thực thi được. Nên xác nhận thêm (rủi ro thấp hơn, không bắt buộc tuyệt đối): overlay resize/move live, Alt+F4 thao tác thật, integration test Named Pipe trên máy sạch. Xem `docs/dependency-map.md` mục "Ghi chú khoảng trống đã biết" cho chi tiết kỹ thuật.)
- [ ] Đợt 2 — Overlay hoàn chỉnh (**code hoàn thành + qua sandbox gate 2026-09-19/20**: build 0 warning/0 error, test 99/99 pass — vùng loại trừ 3 lớp, full-screen lock + auto-timeout 30s ở chế độ gộp (`BE-088a`/`089a`/`089b`, quyết định mới chốt qua 2 vòng hỏi đáp chủ dự án — xem `GEN-007b` supersedes `GEN-007a`), đa cửa sổ/z-index/giới hạn 10, multi-monitor cả Vision (capture đa output DXGI) lẫn Overlay (icon trạng thái UX đầy đủ). 1 FAIL cứng phát hiện bởi security-privacy-auditor (catch filter hẹp ở `CloseButtonLocator` có thể crash `Overlay.exe`) đã sửa + re-audit xác nhận RESOLVED. `release-reporter` tổng hợp 2026-09-20: nhấn mạnh `IMG-040`/`IMG-041` (TEST-001, bắt buộc pass 100%) **vẫn là blocker chung dồn từ Đợt 1**, chưa từng chạy được trong sandbox — đề xuất chủ dự án gộp 1 phiên kiểm tra máy Windows thật cho cả Đợt 1+2 cùng lúc (bao gồm: IMG-040/041, overlay resize/move live, Alt+F4 thật, auto-timeout 30s đo thật, hot-plug màn hình, kéo-thả icon, UIA dưới Low IL). Nên xác nhận thêm không bắt buộc tuyệt đối: padding 16px lớp 2/fallback 160×50 lớp 3 (`FE-016c`) vẫn là giá trị tạm, cần benchmark trên app ưu tiên `BE-075`. Câu hỏi mở UX chưa chốt: có hiển thị đếm ngược trực quan cho auto-timeout hay không (`FE-016g` mục 9 spec) — code đã chừa hook sẵn, chưa gắn UI.)
- [ ] Đợt 3 — Password & Authentication (**code hoàn thành + qua sandbox gate 2026-09-20**: build 0 warning/0 error, test 190/190 pass. Argon2id `m=32MiB,t=2,p=2` (nhẹ hơn đề xuất ban đầu, chốt qua hỏi đáp chủ dự án), Recovery Key 24 ký tự, rate-limit dùng chung. **3 FAIL cứng phát hiện + sửa + re-audit độc lập xác nhận RESOLVED**: (1) Recovery Key plaintext `string`→`bytes` để zero được, zero ngay sau khi gửi response qua pipe; (2) Recovery Key có thể dùng lại 2 lần ở 1 edge case — đã đảo thứ tự validate; (3) timing oracle lộ trạng thái `auth.dat` corrupt — đã thêm decoy Argon2id cân bằng chi phí. Kèm sửa 1 bug riêng (`AuditLogWriter` hardcode path production, khiến test pollute `%ProgramData%\ParentalGuard\audit.log` thật — đã dọn file bị pollute trên máy dev theo xác nhận chủ dự án). `release-reporter` tổng hợp: TEST-001 domain PWD-* PASS 100%, nhưng `IMG-040`/`IMG-041` (TEST-001 cấp toàn dự án) vẫn là blocker chung dồn từ Đợt 1 — đề xuất gộp 1 phiên máy thật cho cả Đợt 1+2+3 (thêm: benchmark Argon2id tham số mới trên máy yếu thật). **Lưu ý vận hành quan trọng**: toàn bộ thư mục `tests/` (và nhiều file khác từ Đợt 0-3) hiện CHƯA được `git add`/commit — không phải do `.gitignore`, cần chủ dự án tự kiểm tra trước khi commit để không mất test suite.)
- [ ] Đợt 4 — Anti-uninstall / Tamper (**code hoàn thành + qua sandbox gate 2026-09-20**: build 0 warning/0 error, test 220/220 pass — 2 process mới `ParentalGuard.Watchdog`/`ParentalGuard.Uninstaller`. Dual Watchdog (heartbeat 3s/3-miss, khôi phục peer stop→force-kill→start→`sc create`), giám sát tamper registry qua `RegNotifyChangeKeyValue`, custom uninstaller tái dùng `action_token` Đợt 3, rate-limit `ANTI-060` N=5/T=30 phút (chốt qua hỏi đáp chủ dự án, lỏng hơn ví dụ gốc 5/10 phút). **Audit lần đầu KHÔNG có FAIL cứng nào** (khác Đợt 3 từng có 3 FAIL) — `security-privacy-auditor` xác nhận 7/7 mục trọng tâm PASS (bất biến an toàn ADR-98 "Uninstaller không bao giờ tự xoá nếu Service không xác nhận SUCCESS", action_token dùng 1 lần, race Watchdog↔Service đã fix qua `SuppressRecovery()`, khóa HMAC hằng số pipe Watchdog, sc.exe không injection, rate-limit sliding-window, plaintext/log check). Đã tự sửa 1 khuyến nghị UX non-blocking (try/catch quanh pipe call, thêm `ConnectionLost` outcome). ANTI-070a/070b đã implement từ Đợt 0 (không phải gap Đợt 4); ANTI-040/041 là yêu cầu tài liệu cho phụ huynh, không phải code, chưa được lên lịch chính thức. `release-reporter` tổng hợp: **checklist runtime bắt buộc `TEST-001` mục 3.2 (5 mục: kill Service qua Task Manager, kill cả 2 cùng lúc, gỡ qua Control Panel end-to-end, ACL từ chối user Standard trên file/registry) đều CHƯA CHẠY** — đòi hỏi cài ĐÚNG 2 Windows Service thật qua SCM, rủi ro/phức tạp hơn các Đợt trước, **khuyến nghị mạnh dùng VM/máy test riêng thay vì máy chính**. Giờ dồn 4 Đợt (1-2-3-4) cùng chờ 1 phiên kiểm tra máy thật duy nhất — xem danh sách đầy đủ trong báo cáo release-reporter 2026-09-20.)
- [ ] Đợt 5 — Pause/Resume
- [ ] Đợt 6 — Dashboard UI hoàn chỉnh
- [ ] Đợt 7 — Performance tuning
- [ ] Đợt 8 — Additional mechanisms & hardening
- [ ] Đợt 9 — Release/CI hoàn thiện

## 6. Quy trình khi bắt đầu 1 Đợt

1. `architecture-writer` viết/hoàn thiện các file `Architecture/` mà Đợt đó phụ thuộc (mục 3), chờ approve.
2. `feature-dev` implement từng tính năng con trong Đợt, theo đúng `DEV-040`/`041` (đánh giá fail case trước khi code).
3. `test-runner` + `security-privacy-auditor` (bắt buộc cho domain `PWD-*`/`ANTI-*`/`IMG-0*`/`SEC-*`) chạy checklist.
4. `debugger` xử lý khi fail, quay lại `feature-dev`.
5. `release-reporter` tổng hợp, trình chủ dự án Approve → đánh dấu requirement `VERIFIED`, cập nhật checklist mục 5 file này.

## Changelog

| Ngày | Thay đổi |
|---|---|
| 2026-09-17 | Chủ dự án APPROVE roadmap. Bắt đầu Đợt 0 |
| 2026-09-17 | Khởi tạo roadmap — 10 Đợt (0-9), ưu tiên Image Processing Pipeline lõi (Đợt 1) ngay sau khung xương nền tảng (Đợt 0), theo yêu cầu chủ dự án |

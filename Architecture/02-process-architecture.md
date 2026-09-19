# 02 — Process Architecture

> Version: v0.1.3 | Trạng thái: Approved | Cập nhật: 2026-09-19

## 1. Mục đích

File này trả lời **HOW** cho 5 process đã liệt kê ở `01-tong-quan-kien-truc.md` mục 3: lifecycle (khi nào start/stop), trách nhiệm cụ thể, và state machine trung tâm. Không phát minh yêu cầu sản phẩm mới — mọi hành vi mô tả ở đây phải trích được về Requirement ID trong `Specification/`.

Nguyên tắc xuyên suốt file này (bám `ROADMAP.md` mục 2, đã thống nhất với chủ dự án): thiết kế lifecycle/ranh giới process **một lần cho đúng** ở Đợt 0, để các tính năng thêm sau (Password, Anti-tamper, Pause/Resume, UI đầy đủ...) chỉ cắm thêm state/handler vào khung đã có, không phải tái cấu trúc lifecycle.

## 2. Lifecycle từng process

### 2.1 `ParentalGuard.Service`

- Windows Service, `Start type = Automatic (Delayed Start không dùng — cần chạy ngay từ boot, trước đăng nhập, theo `BE-010`)`.
- **Khởi động**: SCM start lúc boot → `Service` load `config.db` (`BE-060`) → nếu đọc/giải mã thất bại, fallback cấu hình mặc định hard-code, ghi audit log, tự ghi đè lại `config.db`, gửi Toast cảnh báo qua `Overlay` (`BE-061`/`061a`/`061b` — chi tiết cơ chế ghi đè + toast để ở `Architecture/08-anti-tamper-architecture.md`, ở đây chỉ mô tả nhánh lifecycle) → khởi tạo Named Pipe server (chờ `Vision`/`Overlay`/`UI` kết nối) → chờ sự kiện có session tương tác active (`WTSGetActiveConsoleSessionId`) để spawn `Vision`+`Overlay`.
- **Dừng**: chỉ dừng khi hệ điều hành tắt máy hoặc SCM stop (không có API cho phép user thường dừng `Service` — đúng `GEN-005`/`ANTI-*`). Trước khi dừng: gửi lệnh graceful-stop cho `Vision`/`Overlay` (đóng named pipe, cho phép chúng tự thoát trong timeout ngắn) rồi mới thoát.
- **Trách nhiệm** (không lặp lại chi tiết đã có ở `02-backend-spec.md`, chỉ tóm tắt để định vị trong lifecycle): điều phối vòng đời `Vision`/`Overlay` (`BE-011`), enforce cấu hình (`BE-012`), xác thực mật khẩu (`BE-013`), audit log (`BE-014`), tự-watchdog (`BE-015`, chi tiết ở Architecture/07).

### 2.2 `ParentalGuard.Watchdog`

- Windows Service #2, độc lập hoàn toàn với `Service` (không phải child process, không phụ thuộc `Service` để tự khởi động — cả hai đăng ký `Automatic` start riêng biệt với SCM).
- **Khởi động**: cùng lúc boot, song song `Service`, không có thứ tự phụ thuộc bắt buộc giữa 2 service này (nếu có thứ tự phụ thuộc, kẻ tấn công chỉ cần kill 1 cái theo đúng thứ tự — xem `ANTI-010`+, chi tiết dual-watchdog để ở Architecture/07).
- **Vòng đời**: chạy suốt, poll trạng thái `Service` định kỳ (giao thức cụ thể — named pipe hay Service Control Manager query — quyết định ở Architecture/07, không quyết ở đây).
- **Dừng**: chỉ khi tắt máy/SCM stop, cùng điều kiện như `Service`.

### 2.3 `ParentalGuard.Vision`

- **Không phải Windows Service** — là child process do `Service` spawn vào đúng session tương tác qua `WTSQueryUserToken` + `DuplicateTokenEx` + `CreateProcessAsUser` (`BE-023a`, `ADR-01` ở `01-tong-quan-kien-truc.md`). Không bao giờ tự khởi động độc lập, không có entry Task Scheduler/Registry Run riêng.
- **Khởi động**: `Service` phát hiện có active console session (lúc user đăng nhập, hoặc lúc Service khởi động nếu đã có session sẵn) → spawn `Vision` vào session đó.
- **Sự kiện đổi session** (khoá màn hình, Fast User Switching, Remote Desktop connect/disconnect — `BE-023a` mục 4): `Service` nhận `WTSRegisterSessionNotification`, dừng `Vision` instance cũ (graceful, timeout ngắn rồi force-kill nếu cần) → spawn `Vision` mới vào session đang active. Không có trạng thái "nhiều Vision cùng chạy" tại bất kỳ thời điểm nào.
- **Dừng**: (a) session đổi (mục trên), (b) `Service` yêu cầu graceful-stop lúc tắt máy, (c) crash → `Service` phát hiện qua heartbeat (mục 4) và tự restart trong ≤ 3 giây (`BE-023`), (d) trong lúc Pause: **không dừng hẳn** — xem mục 3 (`PAUSE-031`).
- **Vòng lặp chính**: nhận lệnh cấu hình (bật/tắt, tần suất) qua IPC từ `Service` → capture → tiền xử lý → inference → gửi risk score + bbox (không phải ảnh) về `Service` → zero-out buffer (`BE-021`, chi tiết pipeline đầy đủ để ở `05-image-pipeline-architecture.md`). `Vision` **không tự quyết định gì** ngoài phân loại 1 frame — mọi state nghiệp vụ (đang pause hay không, ngưỡng bao nhiêu, exclude-list gì) do `Service` đẩy xuống qua lệnh IPC, `Vision` không tự đọc `config.db` (đúng nguyên tắc single-purpose, `SEC-017`, `BE-020`).

### 2.4 `ParentalGuard.Overlay`

- Process riêng, chạy trong session người dùng, do `Service` spawn cùng cơ chế `CreateProcessAsUser` như `Vision` (không network, quyền thấp hơn `Vision` vì không đụng dữ liệu ảnh — chi tiết quyền cụ thể để ở `06-security-architecture.md`).
- **Khởi động/dừng**: đồng bộ với `Vision` — cùng session tương tác, cùng sự kiện đổi session (mục 2.3). Khác biệt duy nhất: `Overlay` **không dừng khi Pause** (vẫn cần hiển thị icon trạng thái `S8` màu vàng + banner `S9` — `PAUSE-010`/`011` — trong suốt lúc pause).
- **Vòng lặp chính**: nhận danh sách "rect cần che" từ `Service` qua IPC → vẽ overlay tương ứng (0..N instance, N cửa sổ vi phạm đồng thời — `BE-030`+) → nhận sự kiện click nút "Tắt nội dung" → **tự gọi API OS (`PostMessage(WM_CLOSE)`) lên đúng `window_handle` đó ngay tại chỗ**, đồng thời gửi `ForceCloseRequest` lên `Service` để `Service` cập nhật lại danh sách rect đang active + ghi audit log (`BE-032`).
  - *Sửa lại ở v0.1.1*: bản v0.1.0 ghi nhầm "Overlay gửi window handle về `Service` để `Service` force-close" — điều này **bất khả thi về mặt vật lý**: `Service` chạy ở Session 0 (Session 0 Isolation, `BE-023a`), không có window station/desktop tương tác nên không thể gọi bất kỳ API `user32` nào (kể cả `PostMessage`/`GetWindowThreadProcessId`) lên 1 HWND thuộc session tương tác nơi `Overlay` đang chạy. Đây chính là lý do gốc khiến `Vision`/`Overlay` phải spawn vào session tương tác thay vì chạy ở Session 0 (mục 2.3/2.4). Vì vậy bên **có quyền thực thi** lệnh đóng cửa sổ bắt buộc phải là `Overlay` (đã ở đúng session), không phải `Service`.
  - `Service` vẫn giữ đúng vai trò "nguồn sự thật duy nhất" cho *state* (danh sách overlay đang active) — nhận `ForceCloseRequest` để cập nhật state + audit log — nhưng không tự gọi API OS nữa. Tách biệt rõ: "ai quyết định window nào cần đóng" (`Service`, qua việc đưa handle vào danh sách rect ban đầu) vs "ai có quyền thực thi hành động OS đóng cửa sổ" (`Overlay`) — không đổi IPC message schema (`ForceCloseRequest` ở `03-ipc-communication.md` giữ nguyên), chỉ đổi chiều trách nhiệm gọi `user32`.
  - Overlay **không tự biết lý do 1 rect nằm trong danh sách** (không phân biệt "vi phạm nội dung" hay tương lai có thể là "test/debug") — đây là quyết định kiến trúc chính của file này, xem mục 5.
- Đồng thời chịu trách nhiệm icon trạng thái (`BE-033`, `S8`) và banner pause (`S9`) — 2 UI element này luôn hiển thị bất kể có overlay chặn nội dung hay không, nên về lifecycle chúng gắn với chính `Overlay` process, không phải 1 process riêng.

### 2.5 `ParentalGuard.UI`

- App WinUI 3 thông thường, user tự mở (Start Menu/Desktop shortcut) — **không** được `Service` tự động khởi chạy.
- **Khởi động**: user double-click → `UI` kết nối tới `Service` qua Named Pipe (client) để đọc trạng thái/lịch sử/cấu hình.
- **Dừng**: user đóng cửa sổ bình thường — không ảnh hưởng `Service`/`Vision`/`Overlay` (giám sát vẫn chạy nền dù `UI` không mở, đúng mô hình "Dashboard là cửa sổ điều khiển, không phải nơi chứa logic giám sát").

## 3. State machine trung tâm (`Service`)

`Service` giữ 1 state machine cấp cao nhất, làm nguồn sự thật duy nhất cho toàn hệ thống (mọi process khác chỉ phản ứng theo lệnh từ state này, không tự suy luận trạng thái):

```
        ┌──────────┐
        │ Starting │  (đọc config.db, fallback nếu hỏng — BE-061)
        └────┬─────┘
             ▼
   ┌───────────────────┐   pause (PAUSE-001, xác thực OK)   ┌───────────────────┐
   │ Running·Monitoring │ ───────────────────────────────▶  │  Running·Paused   │
   │ (Vision + Overlay  │ ◀─────────────────────────────── │ (Vision suspended, │
   │  hoạt động bình     │   hết hạn tự động (PAUSE-003)     │  Overlay vẫn chạy  │
   │  thường)            │   hoặc resume sớm (PAUSE-004)     │  hiển thị S8/S9)   │
   └─────────┬──────────┘                                   └─────────┬─────────┘
             │ config.db hỏng/không đọc được khi cần ghi lại          │
             ▼                                                        ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │                     Degraded·FailSecure                            │
   │  (BE-061/061a/061b: dùng default hard-code, giám sát luôn BẬT,      │
   │   audit log + Toast cảnh báo phụ huynh, tự ghi đè config.db)        │
   └───────────────────────────────┬──────────────────────────────────┘
                                    │ ghi đè config.db thành công
                                    ▼
                         quay lại Running·Monitoring
             │
             ▼ (SCM stop / shutdown)
        ┌──────────┐
        │ Stopping │
        └──────────┘
```

- **`Degraded·FailSecure` không phải trạng thái "lỗi dừng hoạt động"** — theo đúng nguyên tắc fail-secure (`BE-061`, `Architecture/01` mục 5), state này vẫn active giám sát bình thường, chỉ khác ở nguồn cấu hình (hard-code thay vì `config.db`). Về mặt state machine, nó **song song** với `Running·Monitoring`, không phải nhánh riêng biệt loại trừ nhau — implement cụ thể (nhánh code hay flag `usingFallbackConfig` trên `Running·Monitoring`) để ở lúc code, không quyết ở đây.
- **`Running·Paused`**: `Vision` không bị kill hẳn mà **suspend** (`PAUSE-031`) — giữ heartbeat ở tần suất thấp hơn để Watchdog không hiểu nhầm là bị tấn công (liên kết `ANTI-060`, chi tiết cơ chế suspend cụ thể — `SuspendThread`/Job Object hay tín hiệu IPC riêng — để ở `05-image-pipeline-architecture.md` khi thiết kế threading model `Vision`). Thời điểm hết hạn pause được tính từ timestamp lưu trong `config.db` (`PAUSE-030`), không phải đếm giờ trong RAM — nên nếu `Service` restart giữa lúc pause, state machine phải đọc lại timestamp này ở bước `Starting` để quay thẳng vào `Running·Paused` (nếu còn hạn) hoặc `Running·Monitoring` (nếu đã hết hạn), không mặc định luôn về `Running·Monitoring`.
- Chuyển `Running·Monitoring ↔ Running·Paused` **luôn đi qua xác thực mật khẩu** (`PAUSE-001`/`PAUSE-004`) — state machine không expose transition nào bỏ qua bước này.

## 4. Heartbeat & phát hiện crash (`BE-040`)

| Kênh | Chu kỳ | Ngưỡng coi là crash | Hành động |
|---|---|---|---|
| `Service` ↔ `Vision` | 1 giây (giảm tần suất khi `Running·Paused`, xem `PAUSE-031`) | mất 3 heartbeat liên tiếp | `Service` kill (nếu còn treo) + spawn lại `Vision` trong ≤ 3s (`BE-023`), ghi audit log |
| `Service` ↔ `Overlay` | 2 giây | mất 3 heartbeat liên tiếp | tương tự, restart `Overlay` |
| `Watchdog` ↔ `Service` | để ở `Architecture/09-anti-tamper-architecture.md` | — | — |

Heartbeat mang theo state hiện tại (ví dụ `Vision` báo "đang xử lý frame thứ N") chỉ để chẩn đoán/log — không phải cơ chế truyền lệnh nghiệp vụ (lệnh nghiệp vụ đi qua message IPC riêng, xem `03-ipc-communication.md`).

## 5. Nguyên tắc thiết kế đảm bảo mở rộng linh hoạt

Đây là quyết định kiến trúc quan trọng nhất của file này (bám `GEN-004`, `SEC-002` least-privilege, và thoả thuận ở `ROADMAP.md` mục 2 với chủ dự án):

1. **`Overlay` là hàm render thuần theo danh sách rect, không mang nghiệp vụ.** `Service` gửi xuống `[{windowHandle, rect, reason?}]` — `Overlay` chỉ vẽ, không quan tâm `reason`. Hệ quả: tính năng sau này (whitelist app, chế độ overlay gộp `BE-088`, pause tạm ẩn overlay...) chỉ cần đổi logic **phía `Service`** khi build danh sách này, `Overlay` không đổi 1 dòng code.
   - Nguyên tắc "render thuần" này áp dụng cả cho hành động force-close (mục 2.4, `BE-032`): `Overlay` chỉ **thực thi cơ học** lệnh đóng cửa sổ (`PostMessage(WM_CLOSE)` lên đúng `window_handle` mà `Service` đã đưa vào danh sách) khi user bấm nút — nó không tự quyết định window nào bị đóng hay khi nào đóng, quyết định "window nào cần đóng" vẫn nằm ở `Service` (qua việc đưa handle vào danh sách rect). `Service` là bên duy nhất cập nhật state/audit log khi nhận `ForceCloseRequest`, chỉ khác là không tự gọi API `user32` (không khả thi do Session 0 Isolation, `BE-023a` — xem mục 2.4).
2. **`Vision` là hàm phân loại thuần theo từng frame, không giữ state nghiệp vụ giữa các lần gọi** (ngoại trừ state kỹ thuật thuần tuý như perceptual-hash frame trước để so sánh, `IMG-011` — đây là tối ưu hiệu năng, không phải nghiệp vụ). Hệ quả: Adaptive frame rate (`PERF-*`), Pause/Resume chỉ thay đổi **tần suất `Service` gọi `Vision`**, không sửa logic bên trong `Vision`.
3. **`Service` giữ 1 state trung tâm, chia theo domain ngay từ Đợt 0** dù ban đầu chỉ `MonitoringState` (state machine mục 3) có dữ liệu thật:
   - `MonitoringState` — state machine mục 3 (đã có).
   - `AuthState` — RAM-only, không có bảng trong `config.db` (`PWD-013` yêu cầu tách biệt vật lý, xem `04-data-architecture.md` mục 4). Thiết kế đầy đủ ở `08-password-authentication-architecture.md` mục 7: `PendingSetup` (chờ xác nhận Recovery Key lúc Onboarding), `PendingActionTokens` (cầu nối ngắn hạn cho Auth Modal), và bản sao RAM của `rate_limit` đọc từ `auth.dat` lúc boot.
   - `PauseState` — timestamp hết hạn, số lần pause gần đây (`PAUSE-020`/`021`) — khung đã có sẵn ở mục 3, dữ liệu chi tiết implement ở Đợt tương ứng.
   - Nguyên tắc: tính năng mới **thêm 1 domain-state mới hoặc thêm field vào domain-state có sẵn**, không sửa cấu trúc `MonitoringState` đã chốt ở đây trừ khi chính `MonitoringState` cần đổi.
4. **IPC message schema có trường mở rộng được ngay từ đầu** — chi tiết cụ thể (versioning, optional field...) để ở `03-ipc-communication.md`, nhưng ràng buộc thiết kế bắt buộc phải tuân theo nguyên tắc 3 mục trên khi thiết kế message.

## 6. Khởi động đồng bộ lúc boot / đăng nhập

```
[Máy khởi động]
   ├─▶ SCM start ParentalGuard.Service (Automatic)         ──┐
   └─▶ SCM start ParentalGuard.Watchdog (Automatic)          │  song song, không phụ thuộc thứ tự
                                                               │
[Service: state = Starting]                                   │
   đọc config.db (fallback nếu hỏng — BE-061)                 │
   khởi tạo Named Pipe server                                 │
   chờ WTSGetActiveConsoleSessionId có session hợp lệ          │
                                                               │
[User đăng nhập / đã có session sẵn]                          │
   Service: WTSQueryUserToken → CreateProcessAsUser            │
      ├─▶ spawn ParentalGuard.Vision (vào session đó)          │
      └─▶ spawn ParentalGuard.Overlay (vào session đó)         │
   Service: state = Running·Monitoring (hoặc ·Paused nếu       │
      config.db còn ghi trạng thái pause chưa hết hạn)         │
                                                               ▼
[Watchdog: bắt đầu giám sát Service — chi tiết Architecture/07]
```

`ParentalGuard.UI` không nằm trong chuỗi này — chỉ khởi động khi user tự mở, độc lập hoàn toàn với trình tự trên.

## 7. Bảng ADR bổ sung (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-12 | `Service` là state machine trung tâm duy nhất; `Vision`/`Overlay` không giữ state nghiệp vụ | Đảm bảo tính nhất quán (single source of truth) và tính mở rộng (mục 5) |
| ADR-13 | Domain-state (`MonitoringState`/`AuthState`/`PauseState`...) tách riêng ngay từ Đợt 0 dù chưa dùng hết | Tránh phải tái cấu trúc state lớn khi thêm Password/Anti-tamper/Pause ở các Đợt sau |
| ADR-14 | `Degraded·FailSecure` là flag song song với `Running·Monitoring`, không phải state loại trừ | Khớp đúng ngữ nghĩa fail-secure: hệ thống vẫn "Monitoring", chỉ khác nguồn cấu hình |

## 8. Câu hỏi mở

- [ ] Cơ chế suspend cụ thể cho `Vision` lúc Pause (`PAUSE-031`) — `SuspendThread`, Job Object, hay tín hiệu IPC tự nguyện dừng vòng lặp — quyết định cụ thể để ở `05-image-pipeline-architecture.md` khi thiết kế threading model.
- [ ] Giao thức `Watchdog` ↔ `Service` cụ thể (Named Pipe riêng hay Service Control Manager query) — để ở `09-anti-tamper-architecture.md`.

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.1.3 | 2026-09-19 | PATCH — cụ thể hoá con trỏ `AuthState` (mục 5, trước là placeholder rỗng) trỏ sang `08-password-authentication-architecture.md` mục 7 (Đợt 3 vừa thiết kế xong); đổi 2 tham chiếu `08-anti-tamper-architecture.md` thành `09-anti-tamper-architecture.md` (mục 4, mục 8), theo renumbering ở `00-INDEX.md` khi chèn `08-password-authentication-architecture.md` mới cho Đợt 3. Không đổi nội dung quyết định state machine |
| v0.1.2 | 2026-09-19 | PATCH — đổi 2 tham chiếu `07-anti-tamper-architecture.md` thành `08-anti-tamper-architecture.md` (mục 4, mục 8), theo renumbering ở `00-INDEX.md` (chèn `07-overlay-architecture.md` mới cho Đợt 2, dồn `07`→`08`/`08`→`09`/`09`→`10`/`10`→`11` — không có file nào trong các số cũ từng được viết thật, xem lý do đầy đủ ở changelog `00-INDEX.md`). Không đổi nội dung quyết định |
| v0.1.0 | 2026-09-17 | Khởi tạo — lifecycle 5 process, state machine trung tâm `Service` (Starting/Running·Monitoring/Running·Paused/Degraded·FailSecure/Stopping), heartbeat, nguyên tắc thiết kế đảm bảo mở rộng linh hoạt (Overlay declarative, Vision stateless, Service domain-state), trình tự khởi động lúc boot |
| v0.1.1 | 2026-09-18 | PATCH — sửa lỗi thiết kế phát hiện lúc `feature-dev` implement Đợt 1 (Image Processing Pipeline): mục 2.4 (và mục 5.1) trước đây ghi "Overlay gửi window handle về `Service` để `Service` force-close" — bất khả thi vì `Service` chạy Session 0 (`BE-023a`), không có quyền gọi `user32` lên HWND thuộc session tương tác. Sửa lại: `Overlay` (đã ở đúng session tương tác) tự gọi `PostMessage(WM_CLOSE)` cục bộ, đồng thời vẫn gửi `ForceCloseRequest` lên `Service` để `Service` cập nhật state (danh sách overlay active) + audit log — không đổi IPC message schema (`03-ipc-communication.md` giữ nguyên `ForceCloseRequest`), không đổi hành vi sản phẩm (`BE-032` không đổi). Không cần sửa `Specification/` vì đây là chi tiết HOW thuần kỹ thuật, không phải WHAT. Xem code thật đã áp dụng: `src/ParentalGuard.Overlay/Rendering/OverlayCoordinator.cs`, `src/ParentalGuard.Service/Ipc/OverlayDecisionCoordinator.cs` |

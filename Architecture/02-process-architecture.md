# 02 — Process Architecture

> Version: v0.2.0 | Trạng thái: Approved | Cập nhật: 2026-09-20

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

## 3a. Pause/Resume — chi tiết hoá state machine (Đợt 5, `PAUSE-001`–`031`)

Mục này cụ thể hoá đầy đủ phần khung đã có ở mục 3 — trả lời **HOW** cho `Specification/07-pause-resume-spec.md` (Approved, không còn câu hỏi mở ở cấp file, ngoại trừ 1 điểm còn treo ở mục 8 dưới đây). Toàn bộ luồng dùng lại nguyên vẹn hạ tầng đã có, không phát minh cơ chế mới: cổng xác thực `action_token` (`08-password-authentication-architecture.md` mục 7.2), cơ chế suspend `Vision` qua `ControlVisionCommand.MonitoringEnabled` (`05-image-pipeline-architecture.md` mục 3.4, ADR-40), schema `pause_state` (`04-data-architecture.md` mục 3.4), và `MonitoringStatusUpdate`/`IconState.PAUSED` (`03-ipc-communication.md` mục 3.3, đã có sẵn field `pause_expires_at_unix_ms` từ Đợt 2).

### 3a.1 Kích hoạt Pause (`PAUSE-001`/`002`/`002a`/`002b`)

```
UI                                              Service
 │── AuthVerifyRequest{                          │
 │      password, action_context="pause_monitoring"} ─▶│  (đúng cổng chung 08 mục 7.2 — rate-limit
 │◀── AuthVerifyResponse{result, action_token?} ─│   dùng chung bộ đếm mật khẩu, không có bộ đếm riêng)
 │                                               │
 │── PauseMonitoringRequest{                     │
 │      action_token, duration} ────────────────▶│  1. Verify action_token: còn hạn (15s) + đúng
 │                                               │     action_context="pause_monitoring" + chưa dùng
 │                                               │     → xoá khỏi PendingActionTokens NGAY (dùng 1 lần)
 │                                               │  2. Nếu state hiện tại ĐÃ là Running·Paused: trả
 │                                               │     PauseResult.ALREADY_PAUSED (idempotent guard,
 │                                               │     hiếm — 2 phiên UI thao tác gần như đồng thời)
 │                                               │  3. Tính pause_expires_at_unix_ms theo duration đã
 │                                               │     chọn (PAUSE-002, mục 3a.2 bên dưới)
 │                                               │  4. Ghi pause_state vào config.db NGAY, đồng bộ,
 │                                               │     trước khi trả response (is_paused=true,
 │                                               │     pause_started_at=trusted_now, pause_expires_at)
 │                                               │  5. State machine → Running·Paused
 │                                               │  6. ControlVisionCommand{MonitoringEnabled=false}
 │                                               │     → Vision (ADR-40 ở 05)
 │                                               │  7. OverlayRectListCommand{rects=[]} → Overlay,
 │                                               │     GIẢI PHÓNG toàn bộ overlay đang che (nếu có) —
 │                                               │     ADR-106 mục 3a.6
 │                                               │  8. Đổi cadence heartbeat Service↔Vision → 10s,
 │                                               │     reset bộ đếm miss (mục 3a.4)
 │                                               │  9. MonitoringStatusUpdate{state=PAUSED,
 │                                               │     pause_expires_at_unix_ms} → Overlay (icon vàng,
 │                                               │     đếm ngược hover — đã thiết kế đủ ở
 │                                               │     07-overlay-architecture.md mục 4.1.2)
 │                                               │  10. audit.log: PauseActivated{duration,
 │                                               │      pause_expires_at_unix_ms} (detail schema mới,
 │                                               │      xem amendment 04-data-architecture.md)
 │                                               │  11. Khởi động PauseMonitor tick 30s (mục 3a.3)
 │◀── PauseMonitoringResponse{result=SUCCESS,    │
 │      pause_expires_at_unix_ms} ──────────────│
```

- **Tính `pause_expires_at_unix_ms` theo `duration`** (`PauseDuration` enum, `03-ipc-communication.md` amendment cùng lượt):
  - `FIFTEEN_MINUTES`/`THIRTY_MINUTES`/`ONE_HOUR`/`FOUR_HOURS`: `trusted_now + N phút` — dùng **monotonic anchor `trusted_now`** đã có sẵn ở `08-password-authentication-architecture.md` mục 7.7/ADR-77 (`wall0 + (Environment.TickCount64 - tick0)`), áp dụng lại nguyên vẹn ở đây (ADR-105, mục 3a.6) — không tính theo `DateTime.UtcNow` trực tiếp.
  - `END_OF_DAY`: 23:59:59.999 theo **giờ hệ thống LOCAL** (`PAUSE-002a` ghi rõ "giờ hệ thống máy tính", tức localtime, không phải UTC) của ngày hiện tại tính theo `trusted_now`. Trường hợp hiếm `trusted_now` đã qua 23:59:59.999 đúng lúc xử lý (race condition ở giây giao thời) → lấy mốc 23:59:59.999 **ngày hôm sau** (không bao giờ trả về pause 0 giây hoặc âm).
- **`ALREADY_PAUSED`/`NOT_PAUSED`** là idempotent guard thuần kỹ thuật, không phải lỗi bảo mật — chỉ có thể xảy ra khi phụ huynh mở 2 cửa sổ Dashboard cùng lúc trên 2 máy/phiên khác nhau (dùng chung 1 mật khẩu).

### 3a.2 Resume sớm chủ động (`PAUSE-004`)

```
UI                                              Service
 │── AuthVerifyRequest{                          │
 │      password, action_context="pause_monitoring"} ─▶│  (CÙNG action_context với Pause — 08 mục 7.2
 │◀── AuthVerifyResponse{result, action_token?} ─│   bảng hằng số đã dự kiến sẵn "PAUSE-001/004"
 │                                               │   dùng chung 1 action_context — ADR-101)
 │── ResumeMonitoringRequest{action_token} ─────▶│  1. Verify action_token (như 3a.1 bước 1)
 │                                               │  2. Nếu state hiện tại KHÔNG phải Running·Paused:
 │                                               │     trả ResumeResult.NOT_PAUSED (idempotent guard)
 │                                               │  3. Ghi pause_state: is_paused=false,
 │                                               │     pause_started_at=null, pause_expires_at=null
 │                                               │  4. State machine → Running·Monitoring
 │                                               │  5. ControlVisionCommand{MonitoringEnabled=true}
 │                                               │  6. Cadence heartbeat Service↔Vision → 1s (bình
 │                                               │     thường), reset bộ đếm miss (mục 3a.4)
 │                                               │  7. MonitoringStatusUpdate{state=ACTIVE} → Overlay
 │                                               │  8. audit.log: PauseResumed{trigger="manual",
 │                                               │     pause_expires_at_unix_ms (mốc gốc dự kiến),
 │                                               │     actual_resumed_at_unix_ms}
 │                                               │  9. Dừng PauseMonitor tick (mục 3a.3)
 │◀── ResumeMonitoringResponse{result=SUCCESS} ──│
```

### 3a.3 Tự động resume + banner nhắc định kỳ (`PAUSE-003`, `PAUSE-011`)

**Cơ chế (ADR-102)**: **KHÔNG** dùng 1 `Timer`/`Task.Delay` bắn đúng 1 lần tại thời điểm hết hạn — rủi ro trôi lịch khi máy vào chế độ sleep/hibernate giữa lúc Pause (thời gian hệ thống ngủ không được tính đều bởi mọi cơ chế timer). Thay vào đó: **`PauseMonitor`** — 1 tick nội bộ chu kỳ **30 giây**, CHỈ chạy trong lúc `Running·Paused` (khởi động ở bước cuối 3a.1, dừng ở bước cuối 3a.2/khi auto-resume), mỗi lần tick kiểm tra:

```
tick mỗi 30 giây (chỉ khi state == Running·Paused):
  nếu trusted_now >= pause_expires_at_unix_ms:
      thực hiện đúng trình tự resume ở 3a.2 bước 3-7, NHƯNG:
        audit.log: PauseResumed{trigger="auto_expired", ...}  (thay vì "manual")
        KHÔNG cần action_token (không phải hành động do UI khởi xướng)
      dừng PauseMonitor tick
  ngược lại, nếu (trusted_now - PauseState.LastBannerShownAtUnixMs) >= 10 phút
      (hoặc LastBannerShownAtUnixMs chưa từng set — lần đầu CHỜ đủ 10 phút kể
       từ pause_started_at mới gửi, KHÔNG gửi ngay lúc vừa kích hoạt Pause —
       lý do: phụ huynh vừa tự tay pause, đã biết rõ trạng thái, banner chỉ có
       ý nghĩa nhắc nhở sau một khoảng thời gian — ADR-103):
      gửi ShowToastCommand nhắc còn lại bao lâu (thiết kế đầy đủ nội dung/
      trigger ở 07-overlay-architecture.md mục 4.1.5, amendment cùng lượt)
      PauseState.LastBannerShownAtUnixMs = trusted_now  (RAM-only, KHÔNG persist
      — nếu Service restart giữa lúc Paused, banner đơn giản bắt đầu đếm lại
      từ 0, không phải cơ chế bảo mật nên không cần sống sót qua restart)
```

- Sai số tối đa của auto-resume so với mốc chính xác: **≤ 30 giây** (chu kỳ tick) — chấp nhận được, không phải ngưỡng bảo mật (khác `ANTI-060`).
- `PauseState.LastBannerShownAtUnixMs`: field RAM-only mới, KHÔNG có trong schema `config.db.pause_state` (không cần persist, xem lý do trên).

**Khôi phục sau khi `Service` restart giữa lúc đang Pause** (`PAUSE-030`, cụ thể hoá bước `Starting` đã mô tả sơ bộ ở mục 3):

```
Service Starting → đọc pause_state từ config.db:
  is_paused == false → vào thẳng Running·Monitoring, không làm gì thêm
  is_paused == true:
    nếu pause_expires_at_unix_ms > trusted_now (còn hạn):
      → Running·Paused NGAY (giữ nguyên pause_started_at gốc, KHÔNG reset) —
        ControlVisionCommand{MonitoringEnabled=false}, cadence heartbeat khởi
        tạo thẳng ở 10s (chưa từng ở 1s trong phiên chạy mới, không cần "đổi"),
        khởi động lại PauseMonitor tick — đúng PAUSE-030 "không tự ý resume
        ngay khi khởi động lại nếu vẫn còn trong thời gian đã chọn"
    nếu pause_expires_at_unix_ms <= trusted_now (đã hết hạn TRONG LÚC Service
    down — vd máy tắt qua đêm lúc đang pause 4 giờ):
      → auto-resume NGAY trong bước Starting, TRƯỚC KHI vào Running·Monitoring
        chính thức: ghi pause_state{is_paused=false}, audit.log: PauseResumed
        {trigger="auto_expired_while_offline", ...} — cadence heartbeat khởi
        tạo thẳng ở 1s (bình thường), không đi qua bước "đổi cadence" vì chưa
        từng thiết lập kết nối Vision nào ở cadence Paused trong phiên này —
        đúng PAUSE-030 "không tự ý pause thêm nếu đã hết hạn"
```

### 3a.4 Cadence heartbeat `Service`↔`Vision` khi Pause (`PAUSE-031`, ADR-104)

Bổ sung cụ thể cho bảng mục 4: cadence **10 giây** khi `Running·Paused` (thay vì 1 giây bình thường), ngưỡng crash-detect giữ nguyên tắc "mất 3 heartbeat liên tiếp" nhưng tính theo cadence hiện hành (tương đương 30 giây khi Paused). **Bắt buộc**: ngay tại thời điểm `Service` gửi lệnh đổi `MonitoringEnabled` (cả 2 chiều Pause/Resume, mục 3a.1 bước 8 / 3a.2 bước 6), `Service` đồng thời (a) đổi chu kỳ lên lịch gửi `HeartbeatPing` kế tiếp cho đúng cadence mới, và (b) reset bộ đếm "miss liên tiếp" + mốc "heartbeat cuối nhận được" về thời điểm hiện tại — tránh việc so sánh nhầm giữa ranh giới cadence cũ/mới gây false-positive crash-detect ngay lúc chuyển trạng thái.

`Overlay` **không đổi cadence heartbeat** (giữ nguyên 2 giây) trong suốt Pause — `Overlay` vẫn hoạt động đầy đủ (hiển thị `S8`/`S9`), không có lý do giảm tần suất.

### 3a.5 Vision không bị dừng hẳn, chỉ đổi tần suất hiệu dụng (trả lời `ROADMAP.md` mục 2)

Đúng nguyên tắc đã cam kết ở `ROADMAP.md` mục 2 ("pause/resume ... chỉ thay đổi TẦN SUẤT gọi `Vision` từ `Service`, không sửa logic bên trong `Vision`"): Pause là trường hợp đặc biệt của việc đổi tần suất — tần suất capture hiệu dụng về **0** (`CaptureLoopWorker` tự park ở `wakeEvent.Wait()`, không tốn CPU/GPU, `05` mục 3.4 ADR-40) trong khi `Vision` process **vẫn sống nguyên vẹn**, IPC/heartbeat Thread không bị ảnh hưởng. Không có khái niệm "dừng hẳn Vision" trong Pause — khác hẳn crash-restart (`BE-023`).

### 3a.6 Tương tác với `ANTI-060` — xác nhận tách biệt đúng (`09-anti-tamper-architecture.md` mục 6)

- Pause hợp lệ (qua `ControlVisionCommand.MonitoringEnabled=false`) **không** kill/restart `Vision` process — không có sự kiện `ProcessRestarted` nào được sinh ra trong suốt Pause hợp lệ, nên bộ đếm `ANTI-060` phía `Service` (đếm `ProcessRestarted{Vision}`/`{Overlay}`/`VisionNetworkBlocked`/`TamperDetected`) **hoàn toàn không bị ảnh hưởng** bởi Pause.
- Heartbeat `Service`↔`Vision` tiếp tục đều đặn trong suốt Pause (chỉ chậm hơn, mục 3a.4) — `Vision` luôn phản hồi `HeartbeatAck` bình thường (chỉ Capture-Inference loop park, IPC Thread không park, `05` mục 3.4) → không bao giờ kích hoạt nhánh "mất 3 heartbeat liên tiếp" trong lúc Pause hợp lệ, miễn cadence mới được áp dụng + bộ đếm miss được reset đúng lúc chuyển trạng thái (mục 3a.4).
- **Kết luận**: 2 luồng "dừng hợp lệ có xác thực" (Pause) và "bị kill/tamper" (`ANTI-060`) đã tách biệt triệt để về mặt cơ chế kỹ thuật ngay từ thiết kế — Pause không bao giờ sinh ra bất kỳ sự kiện nào mà `ANTI-060` đang đếm, nên **không cần thêm logic loại trừ tường minh nào** (không có gì để loại trừ).

**ADR bổ sung cho mục 3a** (đánh số tiếp nối ADR-100, số cao nhất đã dùng ở `09-anti-tamper-architecture.md` — ADR-101 đến ADR-106, bảng đầy đủ ở mục 7):

- ADR-101: Pause (`PAUSE-001`) và resume sớm (`PAUSE-004`) dùng CHUNG 1 `action_context="pause_monitoring"` (không tách 2 hằng số riêng) — đúng dự kiến sẵn ở `08` mục 7.2 bảng hằng số, tránh thêm `action_context` không cần thiết cho 2 hành động cùng nhóm "quản lý pause".
- ADR-102: Auto-resume dùng `PauseMonitor` tick 30 giây (không dùng `Timer` bắn đúng 1 lần) — tránh trôi lịch khi máy sleep/hibernate, đơn giản hoá (không cần huỷ/tái lập Timer chính xác mỗi lần đổi state), chi phí CPU không đáng kể.
- ADR-103: Banner nhắc (`PAUSE-011`) tần suất **10 phút/lần**, KHÔNG gửi ngay lúc kích hoạt Pause (chỉ gửi từ lần tick đủ 10 phút đầu tiên trở đi) — chọn đúng số ví dụ minh hoạ trong spec (`PAUSE-011` ghi "ví dụ mỗi 10 phút"), thuần UX không phải ngưỡng bảo mật (khác `ANTI-060`) nên tự quyết định trực tiếp, cùng tinh thần các hằng số UX khác đã quyết định trực tiếp trong dự án (N=50 audit verify ở `04` ADR-28, `PendingSetup` 30 phút ở `08` mục 7.1).
- ADR-104: Cadence heartbeat `Service`↔`Vision` giảm còn 10 giây khi `Running·Paused`, reset bộ đếm miss + mốc heartbeat cuối ngay tại thời điểm đổi cadence (cả 2 chiều) — tuân đúng chữ "tần suất thấp hơn" của `PAUSE-031` dù tiết kiệm CPU chính đã đạt được qua park capture loop (`05` ADR-40); giữ nhất quán "miss 3 lần" ở cadence hiện hành, tránh false-positive lúc chuyển trạng thái.
- ADR-105: Áp dụng lại nguyên vẹn "trusted_now" (monotonic anchor `tick0`/`wall0`, đã có ở `08` ADR-77) cho việc tính `pause_expires_at_unix_ms` và kiểm tra hết hạn — nhất quán chống bypass qua đổi giờ hệ thống trong lúc `Service` đang chạy (cùng residual risk đã ghi nhận ở `08` ADR-77 cho trường hợp đổi giờ trước khi restart), ngăn né giám sát bằng cách lùi giờ hệ thống để kéo dài Pause vượt quá thời lượng đã chọn — vi phạm trực tiếp tinh thần `PAUSE-002` ("bắt buộc chọn thời lượng, không có tuỳ chọn vô thời hạn").
- ADR-106: Khi chuyển sang `Running·Paused`, `Service` đồng thời gửi `OverlayRectListCommand{rects=[]}` giải phóng toàn bộ overlay đang che (nếu có) — hệ quả tất yếu của "tạm dừng TOÀN BỘ giám sát" (`PAUSE-002b`): nếu không giải phóng, phụ huynh vẫn bị chặn bởi overlay cũ dù đã pause, mâu thuẫn mục đích chức năng; đúng cơ chế đã dự đoán sẵn ở `ROADMAP.md` mục 2 ("`Overlay` chỉ nhận danh sách rect cần che ... tính năng pause chỉ cần thay đổi logic phía `Service` lọc danh sách trước khi gửi xuống").

## 4. Heartbeat & phát hiện crash (`BE-040`)

| Kênh | Chu kỳ | Ngưỡng coi là crash | Hành động |
|---|---|---|---|
| `Service` ↔ `Vision` | **1 giây** khi `Running·Monitoring`; **10 giây** khi `Running·Paused` (`PAUSE-031`, ADR-104 mục 3a.4 — đổi cadence + reset bộ đếm miss ngay lúc chuyển trạng thái) | mất 3 heartbeat liên tiếp **theo cadence hiện hành** (3s bình thường / 30s khi Paused) | `Service` kill (nếu còn treo) + spawn lại `Vision` trong ≤ 3s (`BE-023`, chỉ áp dụng khi đang `Running·Monitoring` — Pause hợp lệ không bao giờ kích hoạt nhánh này, mục 3a.6), ghi audit log |
| `Service` ↔ `Overlay` | 2 giây | mất 3 heartbeat liên tiếp | tương tự, restart `Overlay` |
| `Watchdog` ↔ `Service` | 3 giây (`09-anti-tamper-architecture.md` mục 3.3) | mất 3 heartbeat liên tiếp (9s), hoặc pipe vỡ ngay lập tức | Stop graceful→force-kill nếu treo→Start (kèm re-register qua `sc.exe create` nếu registry service bị xoá) — chi tiết đầy đủ `09-anti-tamper-architecture.md` mục 3.4 |

Heartbeat mang theo state hiện tại (ví dụ `Vision` báo "đang xử lý frame thứ N") chỉ để chẩn đoán/log — không phải cơ chế truyền lệnh nghiệp vụ (lệnh nghiệp vụ đi qua message IPC riêng, xem `03-ipc-communication.md`).

## 5. Nguyên tắc thiết kế đảm bảo mở rộng linh hoạt

Đây là quyết định kiến trúc quan trọng nhất của file này (bám `GEN-004`, `SEC-002` least-privilege, và thoả thuận ở `ROADMAP.md` mục 2 với chủ dự án):

1. **`Overlay` là hàm render thuần theo danh sách rect, không mang nghiệp vụ.** `Service` gửi xuống `[{windowHandle, rect, reason?}]` — `Overlay` chỉ vẽ, không quan tâm `reason`. Hệ quả: tính năng sau này (whitelist app, chế độ overlay gộp `BE-088`, pause tạm ẩn overlay...) chỉ cần đổi logic **phía `Service`** khi build danh sách này, `Overlay` không đổi 1 dòng code.
   - Nguyên tắc "render thuần" này áp dụng cả cho hành động force-close (mục 2.4, `BE-032`): `Overlay` chỉ **thực thi cơ học** lệnh đóng cửa sổ (`PostMessage(WM_CLOSE)` lên đúng `window_handle` mà `Service` đã đưa vào danh sách) khi user bấm nút — nó không tự quyết định window nào bị đóng hay khi nào đóng, quyết định "window nào cần đóng" vẫn nằm ở `Service` (qua việc đưa handle vào danh sách rect). `Service` là bên duy nhất cập nhật state/audit log khi nhận `ForceCloseRequest`, chỉ khác là không tự gọi API `user32` (không khả thi do Session 0 Isolation, `BE-023a` — xem mục 2.4).
2. **`Vision` là hàm phân loại thuần theo từng frame, không giữ state nghiệp vụ giữa các lần gọi** (ngoại trừ state kỹ thuật thuần tuý như perceptual-hash frame trước để so sánh, `IMG-011` — đây là tối ưu hiệu năng, không phải nghiệp vụ). Hệ quả: Adaptive frame rate (`PERF-*`), Pause/Resume chỉ thay đổi **tần suất `Service` gọi `Vision`**, không sửa logic bên trong `Vision`.
3. **`Service` giữ 1 state trung tâm, chia theo domain ngay từ Đợt 0** dù ban đầu chỉ `MonitoringState` (state machine mục 3) có dữ liệu thật:
   - `MonitoringState` — state machine mục 3 (đã có).
   - `AuthState` — RAM-only, không có bảng trong `config.db` (`PWD-013` yêu cầu tách biệt vật lý, xem `04-data-architecture.md` mục 4). Thiết kế đầy đủ ở `08-password-authentication-architecture.md` mục 7: `PendingSetup` (chờ xác nhận Recovery Key lúc Onboarding), `PendingActionTokens` (cầu nối ngắn hạn cho Auth Modal), và bản sao RAM của `rate_limit` đọc từ `auth.dat` lúc boot.
   - `PauseState` — persist (`config.db.pause_state`, `04-data-architecture.md` mục 3.4): `is_paused`/`pause_started_at_unix_ms`/`pause_expires_at_unix_ms`. RAM-only (không persist): `LastBannerShownAtUnixMs` (mốc lần cuối gửi banner nhắc, mục 3a.3). Số lần/tổng thời lượng pause (`PAUSE-020`) suy ra từ `audit.log`, KHÔNG cache riêng trong `PauseState` (đã quyết định ở `04` mục 3.4). Thiết kế đầy đủ transition/auto-resume/banner ở mục 3a (Đợt 5).
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
| ADR-101 | Pause (`PAUSE-001`) và resume sớm (`PAUSE-004`) dùng CHUNG 1 `action_context="pause_monitoring"` | Đúng dự kiến sẵn ở `08` mục 7.2 bảng hằng số; tránh thêm `action_context` không cần thiết cho 2 hành động cùng nhóm |
| ADR-102 | Auto-resume dùng `PauseMonitor` tick 30 giây, không dùng `Timer` bắn đúng 1 lần | Tránh trôi lịch khi máy sleep/hibernate; đơn giản hoá; chi phí CPU không đáng kể |
| ADR-103 | Banner nhắc (`PAUSE-011`) tần suất 10 phút/lần, không gửi ngay lúc kích hoạt Pause | Đúng số ví dụ minh hoạ trong spec; thuần UX không phải ngưỡng bảo mật, tự quyết định trực tiếp (cùng tinh thần N=50 ở `04` ADR-28) |
| ADR-104 | Cadence heartbeat `Service`↔`Vision` giảm còn 10s khi `Running·Paused`, reset bộ đếm miss ngay lúc đổi cadence | Tuân đúng chữ "tần suất thấp hơn" của `PAUSE-031`; tránh false-positive crash-detect lúc chuyển trạng thái |
| ADR-105 | Áp dụng lại "trusted_now" (monotonic anchor, `08` ADR-77) cho tính/kiểm tra `pause_expires_at_unix_ms` | Nhất quán chống bypass đổi giờ hệ thống; ngăn né giám sát bằng cách lùi giờ để kéo dài Pause quá thời lượng đã chọn (`PAUSE-002`) |
| ADR-106 | Lúc chuyển `Running·Paused`, `Service` gửi `OverlayRectListCommand{rects=[]}` giải phóng overlay đang che | Hệ quả tất yếu của "tạm dừng TOÀN BỘ giám sát" (`PAUSE-002b`); đúng cơ chế đã dự đoán ở `ROADMAP.md` mục 2 |

## 8. Câu hỏi mở

- [x] ~~Cơ chế suspend cụ thể cho `Vision` lúc Pause (`PAUSE-031`) — `SuspendThread`, Job Object, hay tín hiệu IPC tự nguyện dừng vòng lặp.~~ — **Đã xong** (`05-image-pipeline-architecture.md` mục 3.4, ADR-40: tái dùng `ControlVisionCommand.MonitoringEnabled`, không `SuspendThread`/message mới). Cụ thể hoá đầy đủ luồng dùng lại quyết định này ở mục 3a (Đợt 5, v0.2.0).
- [x] ~~Giao thức `Watchdog` ↔ `Service` cụ thể (Named Pipe riêng hay Service Control Manager query) — để ở `09-anti-tamper-architecture.md`.~~ — **Đã xong** (`09` v0.1.0, Đợt 4: Named Pipe riêng `ParentalGuard.Svc.Watchdog`, heartbeat 3s/3-miss, xem mục 4 bảng trên).
- [ ] **`PAUSE-021` (cảnh báo tần suất pause bất thường) — gap trạng thái spec, CẦN `spec-maintainer`/chủ dự án quyết định TRƯỚC khi `feature-dev` implement phần này** (không blocking phần còn lại của Đợt 5 — mục 3a.1-3a.6 ở trên implement được đầy đủ độc lập với mục này). Chi tiết: `Specification/07-pause-resume-spec.md` `PAUSE-021` còn mang tag **"(PROPOSED)"** ngay trong chính văn bản requirement ("Nếu tần suất tạm dừng bất thường cao... (ví dụ > 5 lần/ngày) → hiện cảnh báo nhẹ trên Dashboard") — theo đúng quy tắc workflow ở `Specification/00-INDEX.md` mục 1 ("Mỗi requirement có trạng thái: `PROPOSED` → `APPROVED` → ... Trước khi bước sang giai đoạn 'Thiết kế hệ thống', toàn bộ requirement `PROPOSED` phải được review và chuyển thành `APPROVED` hoặc `REJECTED`"), `PAUSE-021` **chưa từng được chuyển trạng thái tường minh** (khác `PAUSE-002a`/`PAUSE-002b` đã có dòng "ĐÃ CHỐT v0.2.0" rõ ràng trong changelog file đó) dù bản thân file `07-pause-resume-spec.md` đã đóng dấu "Trạng thái: Approved" ở cấp toàn file và "không còn câu hỏi mở nào" ở mục 6 — đây là 2 tầng trạng thái (toàn file vs từng requirement) không khớp nhau, không phải lỗi archive mà là sót bước chuyển trạng thái từng dòng. Ngưỡng ví dụ "> 5 lần/ngày" **chưa từng `ĐÃ CHỐT`** (giống hệt tình huống `ANTI-060` trước khi chủ dự án xác nhận trực tiếp N=5/T=30 phút — `09-anti-tamper-architecture.md` mục 6.1) — `architecture-writer` **không tự bịa số liệu** cho 1 cơ chế có tính chất cảnh báo bảo mật (phát hiện khả năng mật khẩu bị lộ/dùng sai mục đích). Đề nghị: (a) `spec-maintainer` chính thức đổi `PAUSE-021` từ `PROPOSED` → `APPROVED` (giữ nguyên hoặc điều chỉnh ngưỡng) hoặc `REJECTED` (ghi lý do) qua đúng quy trình archive ở `Specification/`; (b) nếu `APPROVED`, chủ dự án xác nhận trực tiếp số N lần/khoảng thời gian cụ thể (không dùng nguyên số ví dụ minh hoạ nếu chưa qua xác nhận). Cơ chế kỹ thuật bên dưới (nếu được duyệt) **không cần thiết kế thêm gì mới**: dữ liệu nguồn (`audit.log` event `PauseActivated`, đã đủ từ mục 3a.1) và điểm tính toán (Dashboard `10-ui-architecture.md`, Đợt 6, hoặc quét lúc `Service` khởi động — như `04` mục 3.4 đã dự kiến) đã sẵn sàng, chỉ còn thiếu đúng 1 con số ngưỡng.

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.0 | 2026-09-20 | MINOR — Đợt 5 (`ROADMAP.md`, Pause/Resume, `PAUSE-001`–`031`). Thêm mục 3a cụ thể hoá đầy đủ state machine Pause/Resume: luồng kích hoạt (3a.1, qua `action_token`/`08` mục 7.2), resume sớm (3a.2, cùng `action_context`), auto-resume + banner nhắc qua `PauseMonitor` tick 30s không dùng Timer chính xác (3a.3, tránh trôi lịch sleep/hibernate), khôi phục đúng sau restart `Service` giữa lúc Pause kể cả trường hợp hết hạn lúc offline (3a.3), cadence heartbeat Paused 10s + reset bộ đếm miss lúc chuyển trạng thái (3a.4, cập nhật bảng mục 4), xác nhận Vision chỉ đổi tần suất hiệu dụng không dừng hẳn (3a.5, đúng cam kết `ROADMAP.md` mục 2), xác nhận tách biệt hoàn toàn với `ANTI-060` — Pause hợp lệ không sinh sự kiện nào bị đếm (3a.6). 6 ADR mới (101-106). Cập nhật mục 5 (field RAM mới `LastBannerShownAtUnixMs`), đóng 1 câu hỏi mở cũ đã được `05` giải quyết từ trước nhưng chưa tick ở đây (cơ chế suspend `Vision`). **1 câu hỏi mở CHÍNH cần `spec-maintainer`/chủ dự án xử lý trước khi implement riêng phần này** (`PAUSE-021`: còn tag "(PROPOSED)" chưa từng chuyển trạng thái theo đúng workflow `Specification/00-INDEX.md` mục 1, ngưỡng ví dụ "> 5 lần/ngày" chưa `ĐÃ CHỐT` — không tự bịa số liệu cảnh báo bảo mật, giống tinh thần đã áp dụng cho `ANTI-060` trước khi chủ dự án xác nhận) — không blocking phần còn lại của Đợt 5. Amendment cùng lượt: `03-ipc-communication.md` (MINOR, 6 message mới field 92-97 khối UI), `04-data-architecture.md` (PATCH, định nghĩa `detail` schema cho `PauseActivated`/`PauseResumed`), `07-overlay-architecture.md` (PATCH, thiết kế banner `S9` tái dùng `ShowToastCommand`, vẫn giữ Draft). Theo chỉ đạo — không dừng chờ review từng file, dừng lại báo cáo sau khi xong đủ file + liệt kê rõ câu hỏi mở cần chủ dự án quyết định |
| v0.1.4 | 2026-09-19 | PATCH — amendment cùng lượt viết `09-anti-tamper-architecture.md` (Đợt 4). Điền dòng heartbeat `Watchdog ↔ Service` ở bảng mục 4 (trước để trống "để ở `09`") = 3 giây/3-miss/hành động khôi phục, trích dẫn `09` mục 3.3/3.4. Đóng câu hỏi mở mục 8 về giao thức `Watchdog↔Service` (đã quyết định: Named Pipe riêng, không phải SCM query thuần). Không đổi nội dung lifecycle/state machine đã chốt |
| v0.1.3 | 2026-09-19 | PATCH — cụ thể hoá con trỏ `AuthState` (mục 5, trước là placeholder rỗng) trỏ sang `08-password-authentication-architecture.md` mục 7 (Đợt 3 vừa thiết kế xong); đổi 2 tham chiếu `08-anti-tamper-architecture.md` thành `09-anti-tamper-architecture.md` (mục 4, mục 8), theo renumbering ở `00-INDEX.md` khi chèn `08-password-authentication-architecture.md` mới cho Đợt 3. Không đổi nội dung quyết định state machine |
| v0.1.2 | 2026-09-19 | PATCH — đổi 2 tham chiếu `07-anti-tamper-architecture.md` thành `08-anti-tamper-architecture.md` (mục 4, mục 8), theo renumbering ở `00-INDEX.md` (chèn `07-overlay-architecture.md` mới cho Đợt 2, dồn `07`→`08`/`08`→`09`/`09`→`10`/`10`→`11` — không có file nào trong các số cũ từng được viết thật, xem lý do đầy đủ ở changelog `00-INDEX.md`). Không đổi nội dung quyết định |
| v0.1.0 | 2026-09-17 | Khởi tạo — lifecycle 5 process, state machine trung tâm `Service` (Starting/Running·Monitoring/Running·Paused/Degraded·FailSecure/Stopping), heartbeat, nguyên tắc thiết kế đảm bảo mở rộng linh hoạt (Overlay declarative, Vision stateless, Service domain-state), trình tự khởi động lúc boot |
| v0.1.1 | 2026-09-18 | PATCH — sửa lỗi thiết kế phát hiện lúc `feature-dev` implement Đợt 1 (Image Processing Pipeline): mục 2.4 (và mục 5.1) trước đây ghi "Overlay gửi window handle về `Service` để `Service` force-close" — bất khả thi vì `Service` chạy Session 0 (`BE-023a`), không có quyền gọi `user32` lên HWND thuộc session tương tác. Sửa lại: `Overlay` (đã ở đúng session tương tác) tự gọi `PostMessage(WM_CLOSE)` cục bộ, đồng thời vẫn gửi `ForceCloseRequest` lên `Service` để `Service` cập nhật state (danh sách overlay active) + audit log — không đổi IPC message schema (`03-ipc-communication.md` giữ nguyên `ForceCloseRequest`), không đổi hành vi sản phẩm (`BE-032` không đổi). Không cần sửa `Specification/` vì đây là chi tiết HOW thuần kỹ thuật, không phải WHAT. Xem code thật đã áp dụng: `src/ParentalGuard.Overlay/Rendering/OverlayCoordinator.cs`, `src/ParentalGuard.Service/Ipc/OverlayDecisionCoordinator.cs` |

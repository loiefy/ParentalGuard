# 03 — IPC Communication (Named Pipe Contract)

> Version: v0.7.0 | Trạng thái: Approved | Cập nhật: 2026-09-20

## 1. Mục đích

File này trả lời **HOW** cho toàn bộ giao tiếp liên tiến trình đã được mô tả sơ bộ ở `02-process-architecture.md` (mục 2, 4, 5, 6): named pipe transport cụ thể (tên pipe, ACL, framing), message schema Protobuf, thứ tự gọi lúc connect, cơ chế ký HMAC, và xử lý lỗi/timeout. Không phát minh yêu cầu sản phẩm mới — mọi quyết định ở đây phải trích được về Requirement ID trong `Specification/` (chủ yếu `BE-050`/`BE-051` ở `02-backend-spec.md`, `SEC-011`/`SEC-012` ở `04-security-spec.md`) hoặc là ADR thuần kỹ thuật.

Nguyên tắc xuyên suốt file này (bám `ROADMAP.md` mục 2, thoả thuận với chủ dự án): message schema phải **mở rộng được ngay từ đầu** — thêm message type mới hoặc field mới ở các Đợt sau (Password, Pause, Overlay gộp, Perf adaptive...) không được phá vỡ tương thích ngược với client cũ.

## 2. Named Pipe transport

### 2.1 Số lượng pipe và tên pipe

**Quyết định: 3 pipe riêng biệt** thay vì 1 pipe dùng chung cho mọi cặp process (ADR-15, mục 7):

| Pipe name | Server | Client | Heartbeat cadence (nguồn) |
|---|---|---|---|
| `ParentalGuard.Svc.Vision` | `Service` | `Vision` | 1 giây (`BE-040`, `02-process-architecture.md` mục 4) |
| `ParentalGuard.Svc.Overlay` | `Service` | `Overlay` | 2 giây (`BE-040`) |
| `ParentalGuard.Svc.UI` | `Service` | `UI` (0..N phiên phụ huynh mở Dashboard) | không có heartbeat định kỳ — request/response theo nhu cầu |
| `ParentalGuard.Svc.Watchdog` | `Service` | `Watchdog` | 3 giây (`09-anti-tamper-architecture.md` mục 3.3, Đợt 4) |
| `ParentalGuard.Svc.Uninstaller` | `Service` | `ParentalGuard.Uninstaller.exe` | không định kỳ, ephemeral — giống `UI` (`09-anti-tamper-architecture.md` mục 5.2, Đợt 4) |

**Bổ sung Đợt 4** (đóng câu hỏi mở trước đó ở đây và ở `02-process-architecture.md` mục 8): `Service ↔ Watchdog` dùng Named Pipe riêng (không phải Service Control Manager query thuần) — thiết kế đầy đủ (ACL, handshake, heartbeat, hành động khôi phục) ở `09-anti-tamper-architecture.md` mục 3. 2 pipe mới ở bảng trên (`Watchdog`, `Uninstaller`) dùng chung transport/framing/HMAC đã chốt ở file này (mục 2.3, mục 5) — chỉ khác ACL (mục 2.2) và tập message được whitelist (mục 3.1).

Mỗi pipe chỉ chấp nhận đúng nhóm message type thuộc kênh đó (xem mục 3.3) — server từ chối bất kỳ message nào không thuộc whitelist của pipe đang nhận, kể cả khi HMAC hợp lệ. Đây là lớp least-privilege bổ sung ở tầng IPC (liên hệ `SEC-002`): `Overlay` dù có bị compromise cũng không thể gửi lệnh có dạng `ControlVisionCommand` vì pipe `ParentalGuard.Svc.Overlay` không bao giờ định tuyến message đó tới logic xử lý tương ứng.

### 2.2 Security Descriptor / ACL (`SEC-011`, `BE-050`)

Vấn đề cần giải quyết: `Vision`, `Overlay`, và `UI` (khi phụ huynh dùng chung 1 tài khoản Windows với việc đang bị giám sát) có thể chạy dưới **cùng 1 SID người dùng** (token của session tương tác) — nên ACL thuần theo SID không đủ để phân biệt "process ParentalGuard thật" với "tiến trình giả mạo cùng chạy dưới user đó". Vì vậy `SEC-011` yêu cầu tường minh: ACL là lớp lọc thô đầu tiên, **định danh thật phải qua kiểm tra chữ ký code-signing** (mục 4.2).

- **Pipe `Vision`/`Overlay`** (chỉ có 1 client hợp lệ tại 1 thời điểm — đúng process do `Service` vừa spawn vào session tương tác đang active, `BE-023a`):
  - `PipeSecurity` DACL: **Deny** tường minh `Everyone`/`ANONYMOUS LOGON`/`Guests` (đặt trước, để loại trừ rõ ràng thay vì chỉ dựa vào việc không cấp Allow); **Allow** `NT AUTHORITY\SYSTEM` (Full Control); **Allow** đúng SID của user đang ở session tương tác hiện tại (tra qua `WTSQueryUserToken`/`LookupAccountSid`, cùng nguồn xác định session mà `Service` dùng để spawn `Vision`/`Overlay` — `BE-023a`) — chỉ Read + Write, không đổi được ACL của pipe.
  - **Hệ quả cần lưu ý với đổi session (`02-process-architecture.md` mục 2.3)**: khi active console session đổi (khoá màn hình, Fast User Switching, RDP), `Service` không chỉ dừng/spawn lại `Vision`/`Overlay` — còn phải **tái tạo pipe instance với ACL trỏ đúng SID user mới**, nếu không SID của user cũ vẫn còn quyền connect dù không còn là session đang giám sát (lỗ hổng residual access). Recreate pipe đồng thời với recreate process, không tách rời hai thao tác này.
- **Pipe `UI`**: khác với `Vision`/`Overlay`, `UI` do phụ huynh **tự mở**, và phụ huynh có thể dùng 1 tài khoản Windows Administrator **riêng biệt** với tài khoản trẻ đang bị giám sát (khuyến nghị đã chốt ở `SEC-006`) — nghĩa là SID mở `UI` **không nhất thiết trùng** SID của session đang chạy `Vision`/`Overlay`. Do đó ACL pipe `UI` dùng **Allow `NT AUTHORITY\INTERACTIVE`** (bất kỳ user nào đang đăng nhập tương tác cục bộ trên máy) + `SYSTEM`, thay vì khoá cứng 1 SID cụ thể. Việc phân biệt "đúng là phụ huynh" **không phải việc của IPC ACL** — đó là trách nhiệm của cơ chế xác thực mật khẩu (`PWD-0xx`, Đợt 3) ở tầng nghiệp vụ, không thiết kế ở file này.
- **Pipe `Watchdog`** (Đợt 4, `09-anti-tamper-architecture.md` mục 3.2): cả `Service` lẫn `Watchdog` đều chạy `LocalSystem` — ACL **chỉ Allow `NT AUTHORITY\SYSTEM`** (Full Control), **Deny** tường minh `Everyone`/`ANONYMOUS LOGON`/`Guests`/`INTERACTIVE` — khác hẳn 3 pipe trên, không có SID người dùng nào (kể cả phụ huynh Administrator) được phép connect vào pipe này.
- **Pipe `Uninstaller`** (Đợt 4, `09-anti-tamper-architecture.md` mục 5.2): ACL **giống hệt policy pipe `UI`** (`INTERACTIVE` + `SYSTEM`) — `ParentalGuard.Uninstaller.exe` chạy elevated (Administrator) nhưng vẫn trong cùng session tương tác (UAC không đổi session, chỉ đổi token), nên vẫn thuộc nhóm `INTERACTIVE`. Giới hạn 1 kết nối đồng thời, đúng mẫu hình pipe `UI` (mục 6).

### 2.3 Framing (length-prefixed)

Mỗi message trên wire có cấu trúc:

```
[4 byte, little-endian, uint32: độ dài payload]
[N byte: IpcPayload đã serialize bằng Protobuf]
[32 byte: HMAC-SHA256(payload, signing_key) — xem mục 5]
```

- Chữ ký **không nằm bên trong chính message Protobuf** mà là trailer ngoài (ADR-17) — tránh vấn đề tự tham chiếu (field chữ ký nằm trong chính nội dung đang được ký).
- Giới hạn kích thước tối đa: **65536 byte (64 KB)** cho phần payload (`SEC-012`). Nếu 4 byte độ dài đọc được vượt ngưỡng này, bên nhận đóng kết nối ngay lập tức **không đọc tiếp phần còn lại** — tránh cấp phát buffer theo kích thước do bên gửi tự khai (chặn resource-exhaustion/buffer overflow kinh điển, đúng tinh thần `SEC-012`). 64 KB đủ dư cho toàn bộ payload nghiệp vụ hiện tại (ví dụ `OverlayRectListCommand` với tối đa 10 rect theo giới hạn `BE-088`/`PERF-060` chỉ vài trăm byte) — truy vấn có thể trả dữ liệu lớn trong tương lai (ví dụ lịch sử audit log cho `UI`, Đợt 6) phải phân trang ở tầng message (field `page`/`page_size` trong request, nhiều response nhỏ), không nhồi vào 1 message khổng lồ (ADR-21).

## 3. Message schema Protobuf

### 3.1 Envelope (`IpcPayload`) — wrapper mở rộng được

Toàn bộ message thực tế đi qua 1 message bao ngoài duy nhất, dùng `oneof` cho phần thân — cho phép thêm message type mới (field number mới trong `oneof`) ở các Đợt sau mà không phá vỡ client cũ (client cũ không nhận diện được field mới trong `oneof` sẽ coi như "chưa set", bỏ qua an toàn theo đúng ngữ nghĩa proto3, không crash) — đúng nguyên tắc mở rộng ở `ROADMAP.md` mục 2 (ADR-16).

```protobuf
syntax = "proto3";
package parentalguard.ipc.v1;

message IpcPayload {
  uint64 message_id          = 1;  // sinh tăng dần theo từng kết nối, dùng để đối chiếu log/debug
  uint64 correlation_id      = 2;  // 0 nếu không phải phản hồi; ngược lại = message_id của message đang được đáp lại
  int64  timestamp_unix_ms   = 3;
  ProcessType sender         = 4;
  uint32 schema_version      = 5;  // hiện tại = 1 — bump khi có breaking change ở envelope hoặc 1 message con

  oneof body {
    // --- Handshake (10-19) ---
    Hello              hello       = 10;
    HelloAck           hello_ack   = 11;

    // --- Heartbeat (20-29) ---
    HeartbeatPing      heartbeat_ping = 20;
    HeartbeatAck       heartbeat_ack  = 21;

    // --- Lifecycle (30-39) ---
    GracefulStopCommand graceful_stop = 30;

    // --- Kênh Vision (40-59, dành 42-59 cho Đợt 5 Pause / Đợt 7 Perf adaptive) ---
    ControlVisionCommand   control_vision  = 40;
    VisionInferenceResult  vision_result   = 41;

    // --- Kênh Overlay (60-79, dành 66-79 cho nhu cầu tương lai) ---
    OverlayRectListCommand overlay_rects        = 60;
    ForceCloseRequest      force_close          = 61;
    ShowToastCommand       show_toast           = 62; // dùng từ Đợt 0 (BE-061b) — xem 3.2; tổng quát cho mọi cảnh báo Overlay-channel, không riêng fail-secure
    MonitoringStatusUpdate monitoring_status     = 63; // Đợt 2, Service → Overlay — trạng thái icon (FE-021), 07-overlay-architecture.md mục 4.1.2
    IconPositionUpdate     icon_position_update = 64; // Đợt 2, Overlay → Service — kết quả kéo-thả icon (FE-020a), 07-overlay-architecture.md mục 4.1.4
    IconLayoutSync         icon_layout_sync     = 65; // Đợt 2, Service → Overlay — đẩy lại toàn bộ vị trí icon đã lưu lúc connect

    // --- Kênh UI (80-99) ---
    // Password & Authentication (80-91, Đợt 3) — 08-password-authentication-architecture.md mục 7
    SetInitialPasswordRequest        set_initial_password_req  = 80;
    SetInitialPasswordResponse       set_initial_password_resp = 81;
    ConfirmRecoveryKeySavedRequest   confirm_recovery_req      = 82;
    ConfirmRecoveryKeySavedResponse  confirm_recovery_resp     = 83;
    AuthVerifyRequest                auth_verify_req           = 84;
    AuthVerifyResponse               auth_verify_resp          = 85;
    ChangePasswordRequest            change_password_req       = 86;
    ChangePasswordResponse           change_password_resp      = 87;
    RecoveryResetRequest             recovery_reset_req        = 88;
    RecoveryResetResponse            recovery_reset_resp       = 89;
    AuthStatusQuery                  auth_status_query         = 90;
    AuthStatusResponse               auth_status_resp          = 91;
    // Pause/Resume (92-97, Đợt 5) — 02-process-architecture.md mục 3a
    PauseMonitoringRequest           pause_monitoring_req      = 92;
    PauseMonitoringResponse          pause_monitoring_resp     = 93;
    ResumeMonitoringRequest          resume_monitoring_req     = 94;
    ResumeMonitoringResponse         resume_monitoring_resp    = 95;
    PauseStatusQuery                 pause_status_query        = 96;
    PauseStatusResponse              pause_status_resp         = 97;
    // 98-99 dành cho Đợt 6 (Dashboard query khác — audit log history, config query...),
    // chi tiết hoá khi thiết kế 10-ui-architecture.md

    // --- Kênh Watchdog (100-119), Đợt 4 — 09-anti-tamper-architecture.md mục 3 ---
    WatchdogReportEvent    watchdog_report_event     = 100;
    WatchdogReportEventAck watchdog_report_event_ack = 101;

    // --- Kênh Uninstaller (120-139), Đợt 4 — 09-anti-tamper-architecture.md mục 5 ---
    // Lưu ý: pipe Uninstaller CÒN whitelist AuthVerifyRequest/AuthVerifyResponse (field 84/85,
    // định nghĩa gốc ở khối UI trên) qua cơ chế whitelist message theo pipe (mục 3.1a) —
    // KHÔNG định nghĩa lại message đó ở khối 120-139 này.
    UninstallExecuteRequest  uninstall_execute_req  = 120;
    UninstallExecuteResponse uninstall_execute_resp = 121;
  }
}

enum ProcessType {
  PROCESS_TYPE_UNSPECIFIED = 0;
  VISION      = 1;
  OVERLAY     = 2;
  UI          = 3;
  WATCHDOG    = 4;  // dùng thật từ Đợt 4 — 09-anti-tamper-architecture.md mục 3
  SERVICE     = 5;  // bổ sung v0.2.0 — Service là sender khi tự chủ động gửi (HelloAck/HeartbeatPing/
                     // GracefulStopCommand/ControlVisionCommand/OverlayRectListCommand/ShowToastCommand...);
                     // trước đó thiếu case này, code Đợt 0 phải tạm lách bằng PROCESS_TYPE_UNSPECIFIED (mục 9)
  UNINSTALLER = 6;  // bổ sung Đợt 4 — ParentalGuard.Uninstaller.exe, 09-anti-tamper-architecture.md mục 5
}
```

Quy ước đánh field number: mỗi nhóm nghiệp vụ có 1 khối 20 số, chừa khoảng trống ở cuối khối cho các message chưa biết trước nhưng đã có thể dự đoán domain sẽ thêm ở Đợt sau (Pause, Perf, Overlay gộp, UI query) — đúng yêu cầu "message type dạng có version/field mở rộng được ngay từ đầu, không hard-code cấu trúc chỉ đủ cho use-case hiện tại" ở `ROADMAP.md` mục 2. Quy tắc bổ sung bắt buộc khi sửa `.proto` sau này: **không bao giờ đổi ý nghĩa hoặc xoá 1 field/oneof case đã dùng** — chỉ được `reserved` field number đó và thêm field/case mới; nếu cần đổi ý nghĩa (breaking change thật sự), bump `schema_version` và giữ cả 2 message type song song trong 1 khoảng thời gian chuyển tiếp.

### 3.1a Whitelist message theo pipe (bổ sung Đợt 4, cụ thể hoá nguyên tắc mục 2.1)

Mục 2.1 đã nêu nguyên tắc "mỗi pipe chỉ chấp nhận đúng nhóm message type thuộc kênh đó" từ Đợt 0 — bảng dưới đây cụ thể hoá lần đầu tiên đầy đủ cho cả 5 pipe (bổ sung khi thiết kế 2 pipe mới ở `09-anti-tamper-architecture.md`, đồng thời hệ thống hoá lại cho 3 pipe cũ):

| Pipe | `Hello.process_type` bắt buộc | Message field-block được whitelist |
|---|---|---|
| `ParentalGuard.Svc.Vision` | `VISION` | Handshake (10-19), Heartbeat (20-29), Lifecycle (30-39), Vision (40-59) |
| `ParentalGuard.Svc.Overlay` | `OVERLAY` | Handshake, Heartbeat, Lifecycle, Overlay (60-79) |
| `ParentalGuard.Svc.UI` | `UI` | Handshake, Heartbeat, UI (80-99) |
| `ParentalGuard.Svc.Watchdog` | `WATCHDOG` | Handshake, Heartbeat, Lifecycle, Watchdog (100-119) |
| `ParentalGuard.Svc.Uninstaller` | `UNINSTALLER` | Handshake, `AuthVerifyRequest`/`AuthVerifyResponse` (field 84/85 — tái dùng từ khối UI, không whitelist các message Password/Auth khác), Uninstaller (120-139) |

Server (`Service`) từ chối (đóng kết nối, ghi audit log, không phản hồi — đúng ADR-22) bất kỳ message nào ngoài whitelist của pipe đang nhận, kể cả khi HMAC hợp lệ và `Hello.process_type` đã đúng.

### 3.2 Message con — Đợt 0 (handshake, heartbeat, lifecycle, control)

```protobuf
message Hello {
  ProcessType process_type   = 1;
  uint32      pid            = 2;
  string      executable_path = 3; // dùng đối chiếu lại với kết quả GetNamedPipeClientProcessId, mục 4.2
  uint32      protocol_version = 4;
}

message HelloAck {
  bool   accepted            = 1;
  bytes  session_key         = 2; // chỉ set khi kênh dùng ephemeral key (UI/Watchdog/Uninstaller, mục 5.3) — rỗng với Vision/Overlay
  int64  server_time_unix_ms = 3;
}

message HeartbeatPing {
  uint64 sequence       = 1;
  int64  sent_at_unix_ms = 2;
}

message HeartbeatAck {
  uint64 sequence         = 1; // echo lại sequence của Ping tương ứng
  string diagnostic_state = 2; // CHỈ phục vụ chẩn đoán (02-process-architecture.md mục 4) — không mang lệnh nghiệp vụ
}

message GracefulStopCommand {
  uint32 deadline_ms = 1; // thời gian tối đa cho phép tự thoát trước khi Service force-kill (TerminateProcess)
  string reason       = 2; // "shutdown" | "session_change" | "restart" — chỉ để ghi audit log, không đổi hành vi client
}

message ControlVisionCommand {
  bool     monitoring_enabled     = 1;
  uint32   capture_interval_ms    = 2; // tần suất Service muốn Vision áp dụng ngay bây giờ (PERF-010)
  float    risk_threshold         = 3; // BE-090
  repeated string exclude_process_names = 4; // BE-073/073a

  reserved 20 to 29; // Đợt 5 (PAUSE-*): ví dụ cờ suspend/resume tần suất heartbeat riêng khi Pause (PAUSE-031)
  reserved 30 to 39; // Đợt 7 (PERF-*): ví dụ tham số adaptive frame rate chi tiết hơn ngoài capture_interval_ms
}

message ShowToastCommand {
  uint32        toast_id             = 1; // định danh tăng dần theo Service, dùng đối chiếu log/debug — không phải khoá nghiệp vụ
  string        text                 = 2; // nội dung hiển thị SẴN, Service dựng đầy đủ (kể cả dịch ngôn ngữ hiện tại nếu áp dụng sau này)
  ToastSeverity severity             = 3;
  string        reason_code          = 4; // mã lý do machine-readable, vd "CONFIG_FALLBACK_TRIGGERED" — chỉ phục vụ audit log/test, KHÔNG dùng để Overlay tự dịch/suy luận lại nội dung
  int64         generated_at_unix_ms = 5;

  reserved 10 to 19; // để ngỏ cho nhu cầu tương lai (vd duration_ms hiển thị tuỳ biến, action button, icon) — không phát minh trước ở đây
}

enum ToastSeverity {
  TOAST_SEVERITY_UNSPECIFIED = 0;
  INFO    = 1;
  WARNING = 2;
}
```

Bổ sung (v0.2.0, phát sinh từ code thật Đợt 0): envelope ban đầu không có message type nào cho `Service` yêu cầu `Overlay` hiển thị Toast — gap bị `feature-dev` phát hiện khi hiện thực hoá `FailSecureConfigLoader` (bước 7 luồng 8 bước ở `04-data-architecture.md` mục 6.2, requirement `BE-061b`). `ShowToastCommand` lấp gap này, gửi qua pipe `ParentalGuard.Svc.Overlay` cùng chiều với `OverlayRectListCommand` (Service → Overlay), không phải handshake bắt buộc mà là "message nghiệp vụ theo sự kiện" (mục 4.3) — chỉ gửi khi có sự kiện cần cảnh báo.

- **`text` đã dựng sẵn, không phải `reason_code` để Overlay tự dịch**: đúng nguyên tắc "Overlay là hàm render thuần, không mang nghiệp vụ, không tự suy luận" đã chốt ở `02-process-architecture.md` mục 5.1 — Service (nơi giữ state trung tâm, ADR-12) phải gửi sẵn nội dung đủ dùng để hiển thị ngay, Overlay không rẽ nhánh theo `reason_code`. `reason_code` đi kèm chỉ để phục vụ audit log/test đối chiếu (liên hệ mẫu hình `BE-014`), không phải kênh để Overlay tự dựng lại text hiển thị — nếu sau này có nhu cầu đa ngôn ngữ động ở `Overlay` (hiện `Specification/` chưa có yêu cầu này), đó là quyết định sản phẩm mới, phải bổ sung vào `Specification/03-frontend-ui-spec.md` trước, không tự phát minh ở đây.
- **Không hard-code riêng cho fail-secure**: `severity` (info/warning) và cấu trúc tổng quát cho phép tái dùng `ShowToastCommand` cho bất kỳ cảnh báo Overlay-channel nào ở các Đợt sau (ví dụ cảnh báo anti-tamper khác ngoài `BE-061b`), đúng nguyên tắc mở rộng ở `ROADMAP.md` mục 2 — field number 62 nằm trong khối domain Overlay (60-79, mục 3.1) dù lần dùng đầu tiên là ở Đợt 0.

### 3.3 Message con — Đợt 1 (kết quả inference, lệnh overlay, force-close)

```protobuf
message Rect {
  int32 x      = 1;
  int32 y      = 2;
  int32 width  = 3;
  int32 height = 4;
}

message VisionInferenceResult {
  uint64  frame_id             = 1;
  fixed64 window_handle        = 2; // HWND — 64-bit trên nền tảng x64
  uint32  monitor_id           = 3;
  float   risk_score           = 4;
  Rect    bbox                 = 5; // có thể rỗng nếu risk_score dưới ngưỡng đáng ghi toạ độ
  int64   captured_at_unix_ms  = 6;

  reserved 10 to 19; // để ngỏ nếu IMG-031 (phân tích temporal nhiều frame) được mở lại phạm vi ngoài Phase 1
}

message OverlayRectListCommand {
  repeated OverlayRect rects              = 1;
  int64                generated_at_unix_ms = 2;

  reserved 10 to 19; // Đợt 2: BE-088/089 chế độ overlay gộp có thể cần thêm field cấp danh sách (vd merged=true)
}

message OverlayRect {
  fixed64      window_handle = 1;
  Rect         rect          = 2; // BỎ QUA nếu is_merged=true (mục 7 dưới) — Overlay tự resolve bounds toàn màn hình cục bộ
  uint32       monitor_id    = 3; // gán bởi Vision theo thứ tự enumerate IDXGIOutput lúc khởi động (05-image-pipeline-architecture.md
                                   // mục 3.5, Đợt 2) — ổn định trong 1 phiên chạy, KHÔNG phải định danh vật lý bền vững qua các lần
                                   // khởi động. CHỈ dùng để Service NHÓM các cửa sổ vi phạm theo cùng 1 màn hình (vd chế độ gộp mục 7)
                                   // — Overlay KHÔNG được dùng giá trị số này để tự suy ra toạ độ màn hình, luôn resolve qua Win32
                                   // (MonitorFromWindow/GetMonitorInfo) cục bộ khi cần vẽ overlay toàn màn hình (07-overlay-architecture.md)
  uint32       overlay_id    = 4; // định danh instance overlay, dùng khi force-close (BE-032); ở chế độ gộp (is_merged=true) là id ổn
                                   // định theo monitor_id, KHÔNG trùng namespace với overlay_id của overlay đơn (07-overlay-architecture.md mục 3)
  OverlayReason reason       = 5; // CHỈ để log/debug phía Service — Overlay KHÔNG được rẽ nhánh logic theo field
                                   // này (nguyên tắc "Overlay là hàm render thuần", 02-process-architecture.md mục 5.1)

  // --- Đợt 2, BE-088/089: chế độ overlay gộp khi vượt quá 10 overlay đồng thời ---
  bool             is_merged             = 6; // true = overlay này che TOÀN màn hình chứa window_handle (degraded mode, BE-088) —
                                               // Overlay bỏ qua field `rect`, tự tính bounds qua MonitorFromWindow(window_handle)+
                                               // GetMonitorInfo (07-overlay-architecture.md mục 3.3); vùng loại trừ nút đóng (FE-016)
                                               // KHÔNG áp dụng cho overlay gộp (07-overlay-architecture.md mục 3.4)
  repeated fixed64 merged_window_handles = 7; // chỉ set khi is_merged=true — TOÀN BỘ handle đang vi phạm trên HỆ THỐNG (không chỉ
                                               // riêng màn hình này, đúng nghĩa đen BE-089), Overlay PostMessage(WM_CLOSE) lần lượt
                                               // lên từng handle khi bấm nút, rồi gửi 1 ForceCloseRequest riêng cho mỗi handle
}

enum OverlayReason {
  OVERLAY_REASON_UNSPECIFIED = 0;
  CONTENT_VIOLATION          = 1;
  reserved 2 to 9; // để dành cho nhu cầu tương lai (vd debug/test overlay) — KHÔNG phát minh giá trị cụ thể ở đây
}

message ForceCloseRequest {
  fixed64     window_handle      = 1;
  uint32      overlay_id         = 2;
  int64       clicked_at_unix_ms = 3;
  CloseSource source             = 4; // bổ sung v0.4.0 (Đợt 2, BE-089b) — Overlay LUÔN set tường minh (MANUAL ở chế độ đơn/khi
                                       // bấm nút chế độ gộp; AUTO_TIMEOUT khi hết 30s không thao tác ở overlay gộp,
                                       // 07-overlay-architecture.md mục 3.4.2/3.4.4). Service map sang chuỗi "manual"/"auto-timeout"
                                       // trong audit.log (04-data-architecture.md mục 5.1) — không để mặc định UNSPECIFIED.
}

enum CloseSource {
  CLOSE_SOURCE_UNSPECIFIED = 0;
  MANUAL       = 1;
  AUTO_TIMEOUT = 2;
}

// --- Đợt 2, FE-020/021/022: icon trạng thái multi-monitor (07-overlay-architecture.md mục 4.1) ---

message MonitoringStatusUpdate {
  IconState state                    = 1;
  int64     pause_expires_at_unix_ms = 2; // chỉ có ý nghĩa khi state == PAUSED, 0 nếu không áp dụng
  int64     generated_at_unix_ms     = 3;
}

enum IconState {
  ICON_STATE_UNSPECIFIED = 0;
  ACTIVE = 1; // Running·Monitoring (kể cả Degraded·FailSecure, ADR-14 02-process-architecture.md)
  PAUSED = 2; // Running·Paused
  ERROR  = 3; // Starting, hoặc đang respawn Vision/Overlay sau mất heartbeat (BE-023) — hiếm gặp, đúng FE-021
}

message IconPositionUpdate {
  string device_name = 1; // MONITORINFOEX.szDevice, vd "\\.\DISPLAY1" — KHÔNG phải monitor_id (field 3, OverlayRect)
  int32  x            = 2; // toạ độ tuyệt đối góc trên-trái icon form sau khi thả
  int32  y            = 3;
}

message IconLayoutSync {
  repeated IconPositionUpdate positions = 1; // toàn bộ vị trí đã lưu, push ngay sau handshake (mục 4.3 file này)
}
```

Ghi chú traceability: `VisionInferenceResult` hiện thực hoá "gửi risk score + bbox, không phải ảnh" (`BE-021`, `BE-060` — tuyệt đối không có field nào chứa pixel/byte ảnh trong toàn bộ schema này, đây là bất biến kiến trúc, không phải tối ưu). `OverlayRect`/`ForceCloseRequest` hiện thực hoá `BE-030`–`BE-033`. `ShowToastCommand` (mục 3.2) hiện thực hoá `BE-061b`. 2 field `is_merged`/`merged_window_handles` bổ sung ở v0.3.0 hiện thực hoá `BE-088`/`BE-089` (chế độ overlay gộp) — thiết kế đầy đủ (ngưỡng vào/ra, thuật toán nhóm theo màn hình, xử lý phía Overlay) ở `07-overlay-architecture.md` mục 3, file này chỉ định nghĩa đúng phần schema on-the-wire.

**Bổ sung v0.4.0** (Đợt 2, phát sinh khi `architecture-writer` viết `07-overlay-architecture.md` mục 3.4/4.1): (1) field `source`/enum `CloseSource` trong `ForceCloseRequest` hiện thực hoá yêu cầu audit log phân biệt nguồn ở `BE-089b`; (2) `MonitoringStatusUpdate`/`IconState` hiện thực hoá `FE-021` (3 trạng thái icon), `IconPositionUpdate`/`IconLayoutSync` hiện thực hoá `FE-020a` (lưu vị trí kéo-thả bền vững per-monitor) — cả 2 thuộc `BE-033`/`BE-083` (icon trạng thái). Thiết kế đầy đủ (thời điểm push, ánh xạ state machine, cơ chế timer, lưu trữ) ở `07-overlay-architecture.md` mục 3.4/4.1, file này chỉ định nghĩa schema on-the-wire.

### 3.4 Message con — Đợt 3 (Password & Authentication, field 80-91 khối UI)

Thiết kế nghiệp vụ đầy đủ (luồng Setup/Auth Modal/Đổi mật khẩu/Khôi phục, `action_token` gate, rate-limit) ở `08-password-authentication-architecture.md` mục 7 — file này chỉ định nghĩa schema on-the-wire, giữ nguyên tắc chung "`bytes` cho mọi field credential, không dùng `string`" (memory hygiene, `08` mục 5.2) đã áp dụng nhất quán trong toàn bộ khối message dưới đây:

```protobuf
message AuthStatusQuery {}

message AuthStatusResponse {
  bool password_configured = 1;
}

message SetInitialPasswordRequest {
  bytes password = 1;
  reserved 10 to 15; // Phase 2 (PWD-034), 08 mục 7.6
}

message SetInitialPasswordResponse {
  SetupResult result                 = 1;
  bytes       recovery_key_plaintext = 2; // UTF-8; chỉ set khi result=SUCCESS, hiển thị đúng 1 lần; zero theo 08 ADR-83/mục 5.5
  bytes       setup_token            = 3;
}

enum SetupResult {
  SETUP_RESULT_UNSPECIFIED = 0;
  SUCCESS           = 1;
  PASSWORD_TOO_LONG  = 2;
  ALREADY_CONFIGURED = 3;
}

message ConfirmRecoveryKeySavedRequest {
  bytes setup_token = 1;
  bool  confirmed    = 2;
}

message ConfirmRecoveryKeySavedResponse {
  ConfirmResult result = 1;
}

enum ConfirmResult {
  CONFIRM_RESULT_UNSPECIFIED = 0;
  PERSISTED       = 1;
  TOKEN_EXPIRED    = 2;
  TOKEN_NOT_FOUND  = 3;
}

message AuthVerifyRequest {
  bytes  password       = 1;
  string action_context = 2; // hằng số quy ước, 08 mục 7.2
  reserved 10 to 15; // Phase 2 (PWD-034)
}

message AuthVerifyResponse {
  AuthResult result                         = 1;
  bytes      action_token                    = 2; // chỉ set khi result=SUCCESS
  int64      action_token_expires_at_unix_ms = 3;
  int64      lockout_until_unix_ms           = 4;
  uint32     consecutive_failures            = 5;
}

enum AuthResult {
  AUTH_RESULT_UNSPECIFIED = 0;
  SUCCESS        = 1;
  WRONG_PASSWORD = 2;
  LOCKED_OUT     = 3;
}

message ChangePasswordRequest {
  bytes old_password            = 1;
  bytes new_password            = 2;
  bool  regenerate_recovery_key = 3;
  reserved 10 to 15; // Phase 2 (PWD-034)
}

message ChangePasswordResponse {
  ChangeResult result                     = 1;
  bytes        new_recovery_key_plaintext = 2; // UTF-8; chỉ set nếu regenerate_recovery_key=true và result=SUCCESS; zero theo 08 ADR-83/mục 5.5
}

enum ChangeResult {
  CHANGE_RESULT_UNSPECIFIED = 0;
  SUCCESS              = 1;
  WRONG_OLD_PASSWORD    = 2;
  LOCKED_OUT            = 3;
  NEW_PASSWORD_TOO_LONG = 4;
}

message RecoveryResetRequest {
  bytes recovery_key = 1;
  bytes new_password = 2;
  reserved 10 to 15; // Phase 2 (PWD-034)
}

message RecoveryResetResponse {
  RecoveryResetResult result                     = 1;
  bytes               new_recovery_key_plaintext = 2; // UTF-8; chỉ set nếu result=SUCCESS (PWD-032: luôn sinh key mới); zero theo 08 ADR-83/mục 5.5
  int64               lockout_until_unix_ms       = 3;
}

enum RecoveryResetResult {
  RECOVERY_RESET_RESULT_UNSPECIFIED = 0;
  SUCCESS              = 1;
  WRONG_RECOVERY_KEY    = 2;
  LOCKED_OUT            = 3;
  NEW_PASSWORD_TOO_LONG  = 4;
}
```

Ghi chú traceability: toàn bộ message trên hiện thực hoá `PWD-001`–`004`/`020`–`023`/`030`–`033`/`040`/`041`. Field 92-99 (khối UI) vẫn để trống cho Đợt 6 (`10-ui-architecture.md`).

**Sửa v0.6.0 (phát hiện khi viết `09-anti-tamper-architecture.md`, Đợt 4)**: 3 field `recovery_key_plaintext`/`new_recovery_key_plaintext` (×2) ở trên đổi từ `string` sang `bytes` — `08-password-authentication-architecture.md` v0.3.0 (2026-09-20, FAIL 1/ADR-83) đã quyết định đổi kiểu này nhưng bản sao `.proto` ở file này (v0.5.1 tại thời điểm đó) chưa được đồng bộ, dù changelog `08` v0.3.0 có ghi "đồng bộ `ipc.proto`" — gap thuần đồng bộ giữa 2 tài liệu, không phải quyết định mới, sửa lại cho khớp đúng bản `08` hiện hành, không đổi ý nghĩa field nào khác.

### 3.5 Message con — Đợt 4 (Watchdog report, Uninstaller execute)

Thiết kế nghiệp vụ đầy đủ ở `09-anti-tamper-architecture.md` mục 3 (Watchdog) và mục 5 (Uninstaller) — file này chỉ định nghĩa schema on-the-wire.

```protobuf
// --- Kênh Watchdog (100-119) ---
message WatchdogReportEvent {
  WatchdogEventType event_type          = 1;
  string            target_process      = 2; // "Service" | "Watchdog" — bên vừa được xử lý
  int64             detected_at_unix_ms = 3;
  string            action_taken        = 4; // vd "scm_start", "recreated_registration_then_start" — chỉ để audit log

  reserved 10 to 19; // để ngỏ nếu cần thêm chi tiết chẩn đoán sau này
}

message WatchdogReportEventAck {
  bool received = 1;
}

enum WatchdogEventType {
  WATCHDOG_EVENT_TYPE_UNSPECIFIED        = 0;
  PEER_MISSED_HEARTBEAT_RESTARTED        = 1; // 09 mục 3.4
  PEER_REGISTRATION_MISSING_RECREATED    = 2; // 09 mục 3.4 bước 4
  PEER_REGISTRY_TAMPER_DETECTED_RESTORED = 3; // 09 mục 4.2, khi Watchdog là bên phát hiện
  PEER_RESTART_THRESHOLD_EXCEEDED        = 4; // 09 mục 6.1 — Watchdog-side counter vượt ngưỡng N/T
}

// --- Kênh Uninstaller (120-139) ---
// Lưu ý: pipe Uninstaller CÒN whitelist AuthVerifyRequest/AuthVerifyResponse (field 84/85,
// định nghĩa ở mục 3.4) qua cơ chế whitelist message theo pipe (mục 3.1a) — KHÔNG định
// nghĩa lại ở đây.
message UninstallExecuteRequest {
  bytes action_token   = 1;
  bool  keep_audit_log = 2; // true = copy audit.log ra Desktop của user hiện tại trước khi xoá %ProgramData%
}

message UninstallExecuteResponse {
  UninstallResult result              = 1;
  string          audit_log_copy_path = 2; // chỉ set nếu keep_audit_log=true và copy thành công
}

enum UninstallResult {
  UNINSTALL_RESULT_UNSPECIFIED = 0;
  SUCCESS         = 1;
  INVALID_TOKEN   = 2; // hết hạn / sai action_context / đã dùng / không tồn tại
  PARTIAL_FAILURE = 3; // 1+ bước dọn dẹp lỗi — best-effort, KHÔNG rollback (09 mục 5.5)
}
```

Ghi chú traceability: `WatchdogReportEvent`/`Ack` hiện thực hoá `ANTI-010` (mục 3.6 file `09`). `UninstallExecuteRequest`/`Response` hiện thực hoá `ANTI-020` (mục 5.3-5.5 file `09`).

### 3.6 Message con — Đợt 5 (Pause/Resume, field 92-97 khối UI)

Thiết kế nghiệp vụ đầy đủ (luồng kích hoạt/resume sớm/auto-resume/banner nhắc, `action_token` gate dùng chung `action_context="pause_monitoring"`) ở `02-process-architecture.md` mục 3a — file này chỉ định nghĩa schema on-the-wire, cùng nguyên tắc "`Service` là nguồn sự thật duy nhất" (không có field nào để `UI` tự tính lại `pause_expires_at_unix_ms`, luôn nhận nguyên giá trị từ `Service`).

```protobuf
// --- Pause/Resume (92-97, Đợt 5) — 02-process-architecture.md mục 3a ---
message PauseMonitoringRequest {
  bytes         action_token = 1; // từ AuthVerifyResponse{action_context="pause_monitoring"}, 08 mục 7.2
  PauseDuration duration     = 2;
}

enum PauseDuration {
  PAUSE_DURATION_UNSPECIFIED = 0;
  FIFTEEN_MINUTES = 1;
  THIRTY_MINUTES  = 2;
  ONE_HOUR        = 3;
  FOUR_HOURS      = 4;
  END_OF_DAY      = 5; // 23:59:59.999 giờ hệ thống LOCAL, PAUSE-002a — Service tính, KHÔNG phải UI
}

message PauseMonitoringResponse {
  PauseResult result                  = 1;
  int64       pause_expires_at_unix_ms = 2; // chỉ set khi result=SUCCESS
}

enum PauseResult {
  PAUSE_RESULT_UNSPECIFIED = 0;
  SUCCESS        = 1;
  INVALID_TOKEN  = 2; // hết hạn / sai action_context / đã dùng / không tồn tại (đúng nguyên tắc 08 mục 7.2)
  ALREADY_PAUSED = 3; // idempotent guard — 2 phiên UI thao tác gần như đồng thời
}

message ResumeMonitoringRequest {
  bytes action_token = 1; // action_context="pause_monitoring" — CÙNG hằng số với Pause, 02 mục 3a.2/ADR-101
}

message ResumeMonitoringResponse {
  ResumeResult result = 1;
}

enum ResumeResult {
  RESUME_RESULT_UNSPECIFIED = 0;
  SUCCESS      = 1;
  INVALID_TOKEN = 2;
  NOT_PAUSED   = 3; // idempotent guard
}

message PauseStatusQuery {} // UI đọc trạng thái hiện hành khi vừa mở Dashboard, không chờ push MonitoringStatusUpdate

message PauseStatusResponse {
  bool  is_paused                = 1;
  int64 pause_expires_at_unix_ms = 2; // 0 nếu không áp dụng
}
```

Ghi chú traceability: hiện thực hoá `PAUSE-001`–`004`, `PAUSE-030` (đọc lại trạng thái đúng qua `PauseStatusQuery` khi `UI` vừa kết nối, không cần round-trip `AuthVerifyRequest` chỉ để xem trạng thái). Field `pause_expires_at_unix_ms` dùng lại đúng kiểu dữ liệu/đơn vị đã có sẵn ở `MonitoringStatusUpdate` (mục 3.3) — `Overlay` và `UI` cùng nhận 1 nguồn giá trị nhất quán từ `Service`, không có 2 công thức tính khác nhau. `ControlVisionCommand.reserved 20 to 29` (mục 3.2, để ngỏ từ Đợt 0 "cờ suspend/resume tần suất heartbeat riêng khi Pause") **cố tình vẫn để trống, không dùng ở Đợt 5** — cơ chế suspend/tần suất heartbeat khi Pause đã giải quyết đầy đủ mà không cần field IPC mới (`Vision` suspend qua `MonitoringEnabled` đã có sẵn, `05` ADR-40; cadence heartbeat do chính `Service` tự đổi lịch gửi `HeartbeatPing`, không cần báo cho `Vision` biết — `02` mục 3a.4), tránh gây hiểu nhầm cho người đọc sau này tưởng đây vẫn là gap chưa đóng.

## 4. Thứ tự gọi / handshake khi connect

### 4.1 Thời điểm connect

Theo trình tự khởi động đã chốt ở `02-process-architecture.md` mục 6: `Service` khởi tạo cả 3 pipe server (ở trạng thái `WaitForConnectionAsync`) **trước khi** spawn `Vision`/`Overlay` — nên về lý thuyết pipe luôn sẵn sàng trước khi client tồn tại. Tuy nhiên client vẫn phải tự implement retry (không giả định pipe luôn sẵn sàng ngay lần thử đầu — ví dụ pipe server đang trong chu kỳ recreate sau khi 1 kết nối trước đó vừa đứt, mục 6.2):

- `Vision`/`Overlay` thử connect ngay khi khởi động (sau khi đọc xong dữ liệu bootstrap, mục 5.2); nếu thất bại (timeout hoặc "All pipe instances are busy"), retry với backoff **200ms → 400ms → 800ms → 1600ms → 3200ms (giữ nguyên từ lần thứ 5 trở đi)**, lặp vô hạn — vì không có `Service` để báo cáo thì tiến trình con vô nghĩa, không có lý do dừng hẳn việc thử lại.
- Ngân sách thời gian: `Service` kỳ vọng 1 tiến trình vừa spawn connect + hoàn tất `Hello`/`HelloAck` (mục 4.2) trong vòng **2 giây** kể từ lúc spawn — nằm trong ngân sách `≤ 3 giây` phục hồi crash đã chốt ở `BE-023`/`02-process-architecture.md` mục 4 (chừa ~1 giây cho thời gian khởi động process + JIT/warm-up runtime). Quá hạn mà chưa thấy `Hello` hợp lệ → `Service` coi lần spawn đó thất bại, kill tiến trình (nếu còn treo) và spawn lại, ghi audit log nếu thất bại liên tiếp (liên hệ mẫu hình `ANTI-060` rate-limit sự kiện bất thường).
- `UI` không nằm trong ngân sách 3 giây này (không được `Service` spawn, không có deadline phục hồi crash — user tự mở lại khi cần).
- **Bổ sung Đợt 4**: `Watchdog` khởi động song song `Service` qua SCM (không phụ thuộc thứ tự, `02-process-architecture.md` mục 6) — áp dụng đúng cơ chế retry backoff giống `Vision`/`Overlay` ở trên (lặp vô hạn), nhưng **không** tính vào ngân sách `≤3 giây` (khác bản chất — không phải "vừa được spawn lại sau crash" mà là "khởi động độc lập lúc boot", `09-anti-tamper-architecture.md` mục 3.2). `Uninstaller.exe` là ephemeral như `UI` — không có deadline phục hồi crash, nhưng có timeout UX riêng (10 giây hiển thị lỗi nếu không connect được, 30 giây chờ `UninstallExecuteResponse` — `09` mục 5.3/5.5) vì đây là hành động người dùng đang chủ động chờ kết quả, không phải tiến trình nền.

### 4.2 Xác thực danh tính tại thời điểm connect (trước khi đọc bất kỳ message nghiệp vụ nào)

Áp dụng đồng nhất cho cả 5 pipe, thực hiện **ngay sau khi OS accept connection, trước khi đọc byte đầu tiên của `Hello`**:

1. `Service` gọi `GetNamedPipeClientProcessId` lấy PID của tiến trình vừa connect.
2. Mở process bằng PID đó (`OpenProcess` quyền tối thiểu `PROCESS_QUERY_LIMITED_INFORMATION`), lấy đường dẫn executable đầy đủ.
3. Kiểm tra: (a) đường dẫn nằm trong thư mục cài đặt hợp lệ (`%ProgramFiles%\ParentalGuard\`), và (b) chữ ký Authenticode của file đó khớp thumbprint chứng chỉ ký code của dự án (SignPath, `SEC-030`) đã pin sẵn trong `Service`.
4. Nếu (3) thất bại ở bất kỳ điều kiện nào → `Service` đóng kết nối ngay lập tức, **không đọc/xử lý bất kỳ byte nào đã hoặc sẽ gửi**, ghi audit log (loại sự kiện Spoofing, metadata: PID, đường dẫn phát hiện được, tên pipe, timestamp — không log nội dung message vì chưa từng đọc nó).
5. **Bổ sung Đợt 4 (ADR-94)**: sau khi bước 1-4 hợp lệ, `Service` đọc `Hello.process_type` (message đầu tiên, đã đến lúc này mới thực sự đọc byte) và đối chiếu đúng giá trị mong đợi của pipe đang nhận (bảng mục 3.1a — vd pipe `ParentalGuard.Svc.Watchdog` chỉ chấp nhận `WATCHDOG`). Sai loại → xử lý giống hệt bước 4 (đóng kết nối, ghi audit log Spoofing, không phản hồi). Áp dụng hồi tố cho cả 3 pipe Đợt 0 (`Vision`/`Overlay`/`UI`) — trước đó chỉ kiểm tra đường dẫn/chữ ký (bước 1-4), chưa đối chiếu `process_type` khai báo trong `Hello`; bổ sung này không đổi hành vi của bất kỳ luồng hợp lệ hiện tại nào (client hợp lệ vốn luôn set đúng `process_type` của chính nó).

Đây là cách hiện thực hoá đúng nghĩa đen `SEC-011`: ACL (mục 2.2) là lớp lọc theo lớp người dùng (thô), còn bước 1-5 ở đây mới là "xác định qua chữ ký code-signing, không chỉ tên process" mà `SEC-011` yêu cầu.

### 4.3 Trình tự sau khi xác thực danh tính thành công

```
Client                                          Service (server, đã accept + xác thực danh tính ở 4.2)
  │──── Hello {process_type, pid, exe_path, │
  │      protocol_version}, ký HMAC nếu có   │
  │      khoá sẵn (Vision/Overlay), rỗng     │
  │      chữ ký nếu chưa có khoá (UI) ──────▶│
  │                                           │ verify chữ ký Hello (nếu có) — mục 5
  │◀──── HelloAck {accepted, session_key     │
  │       (chỉ set cho UI), server_time} ────│
  │                                           │
  │  (chỉ kênh Vision) ◀───────────────────── │ push ngay ControlVisionCommand hiện hành
  │                                           │ (phản ánh MonitoringState/PauseState hiện tại
  │                                           │  của state machine trung tâm — 02 mục 3/5)
  │  (chỉ kênh Overlay) ◀────────────────────│ push ngay OverlayRectListCommand hiện hành
  │                                           │ (danh sách rect đang active, kể cả khi Overlay
  │                                           │  vừa crash-restart giữa chừng — fail-secure:
  │                                           │  tiếp tục che đúng những gì đang vi phạm,
  │                                           │  không reset về danh sách rỗng)
  │  (chỉ kênh Overlay) ◀────────────────────│ push ngay MonitoringStatusUpdate hiện hành
  │                                           │ (trạng thái icon FE-021, 07-overlay-architecture.md 4.1.2)
  │  (chỉ kênh Overlay) ◀────────────────────│ push ngay IconLayoutSync (toàn bộ vị trí icon đã lưu,
  │                                           │  FE-020a, 07-overlay-architecture.md 4.1.4)
  │                                           │
  │◀════ HeartbeatPing (định kỳ) ════════════│
  │═════ HeartbeatAck ═══════════════════════▶│
  │                                           │
  │ (message nghiệp vụ theo sự kiện, 2 chiều, │
  │  cả 2 bên đều có thể chủ động gửi)        │
```

`Service` là bên **chủ động đẩy cấu hình xuống** ngay sau handshake, không chờ `Vision`/`Overlay` chủ động hỏi — đúng nguyên tắc "Service là nguồn sự thật duy nhất, Vision/Overlay chỉ phản ứng theo lệnh" đã chốt ở `02-process-architecture.md` mục 3/5 (ADR-12).

## 5. HMAC signing (`BE-051`)

### 5.1 Trường bị ký

Toàn bộ `N` byte của `IpcPayload` đã serialize (đúng phần nằm giữa 4-byte length-prefix và 32-byte trailer, mục 2.3) — **không** ký riêng từng field, ký nguyên khối bytes đã serialize. Thuật toán: HMAC-SHA256, khoá 256-bit.

### 5.2 Khoá cho kênh `Vision`/`Overlay` — persistent, lưu qua DPAPI (liên hệ ADR-06)

- 1 khoá HMAC 32 byte sinh ngẫu nhiên **1 lần duy nhất lúc cài đặt** bởi `Service`, lưu trong `config.db`, mã hoá bằng Windows DPAPI machine-scope — đúng cơ chế đã chốt cho mọi secret khác ở `ADR-06`/`SEC-040`/`BE-060` (bảng "chỉ SYSTEM đọc được"). Không tạo cơ chế lưu khoá riêng biệt cho IPC.
- **Vấn đề cần giải**: `Vision`/`Overlay` bị cấm tuyệt đối tự đọc `config.db` (`SEC-017`, `BE-060`) — nên không thể tự giải mã lấy khoá này. `Service` phải chủ động đẩy khoá xuống cho đúng tiến trình con nó vừa spawn, tại đúng thời điểm spawn, qua kênh mà **không tiến trình nào khác trên máy đọc được**.
- **Giải pháp (ADR-18)**: `Service` dùng **anonymous pipe kế thừa qua handle** (`CreatePipe` + đánh dấu handle inheritable + `STARTUPINFOEX` handle list truyền cho `CreateProcessAsUser`) — không dùng biến môi trường hay tham số dòng lệnh (2 kênh này có thể bị tiến trình khác cùng user đọc được qua `ReadProcessMemory`/liệt kê process, nên không đủ an toàn cho 1 bí mật dùng để ký toàn bộ IPC). Ngay sau khi spawn, `Service` ghi vào đầu ghi của anonymous pipe: `{hmac_key (32 byte), pipe_name (kênh tương ứng), protocol_version}`, rồi đóng đầu ghi phía mình. Tiến trình con đọc dữ liệu này từ handle kế thừa được **duy nhất 1 lần lúc khởi động**, sau đó đóng hẳn handle bootstrap — không giữ lại, không log ra bất kỳ đâu.

### 5.3 Khoá cho kênh `UI` — ephemeral, thương lượng trong chính phiên kết nối

`UI` không có quan hệ cha-con với `Service` (user tự mở, không qua `CreateProcessAsUser`) nên không có cách bootstrap khoá bền vững an toàn như mục 5.2 (ADR-19). Thay vào đó:

- `UI` gửi `Hello` đầu tiên với chữ ký rỗng (chưa có khoá nào để ký).
- `Service`, sau khi đã xác thực danh tính ở mục 4.2 (ACL + chữ ký code-signing — đây mới là lớp xác thực chính cho `UI`, không phải HMAC), sinh 1 khoá ngẫu nhiên 32 byte **riêng cho phiên kết nối đó** (không lưu, không dùng lại giữa các lần connect khác nhau), gửi trong `HelloAck.session_key`.
- Từ message tiếp theo trở đi trong cùng kết nối, cả 2 bên ký bằng khoá phiên này. Khoá bị huỷ khi kết nối đóng; lần connect sau (kể cả cùng 1 tiến trình `UI` nếu user mở lại) thương lượng khoá mới từ đầu.

**Cụ thể hoá bit-level "chữ ký rỗng" (ADR-82, bổ sung v0.5.1)**: văn bản trên chỉ nói "chữ ký rỗng" ở mức khái niệm, chưa đặc tả `UI` thực sự đặt gì vào 32 byte trailer HMAC (mục 2.3) của đúng 2 frame chưa có khoá phiên — `Hello` đầu tiên (`UI` gửi) và `HelloAck` phản hồi (`Service` gửi). Quy ước chính thức: **cả 2 phía ký/verify đúng 2 frame này bằng 1 khoá hằng số 32 byte toàn số 0 (`0x00 × 32`), hard-code sẵn ở cả `Service` lẫn client `UI`** — không phải "bỏ trailer" hay thêm 1 chế độ framing riêng "không HMAC" vào `IpcFrameTransport` (transport dùng chung cho cả 3 kênh, `mục 2.3`). Từ `HelloAck` trở đi, chuyển hẳn sang khoá phiên ephemeral thật vừa nhận ở trên.

Lý do chọn cách này thay vì thêm 1 chế độ framing riêng: `IpcFrameTransport` giữ nguyên **đúng 1 định dạng frame duy nhất** (length-prefix + payload + 32-byte HMAC trailer) cho mọi message ở mọi kênh/mọi giai đoạn kết nối — không cần dạy transport code hiểu 2 "hình dạng" frame khác nhau (có HMAC/không có HMAC) chỉ để phục vụ đúng 2 message của riêng kênh `UI` lúc mới connect. Đủ an toàn dù khoá là hằng số công khai (biết trước, không bí mật): bước xác thực danh tính thật của `UI` đã hoàn tất **trước khi** `Service` đọc byte đầu tiên của `Hello` (mục 4.2 — `GetNamedPipeClientProcessId` + đối chiếu đường dẫn cài đặt + chữ ký code-signing), nên chữ ký HMAC trên đúng 2 frame chưa có khoá phiên **chưa từng đóng vai trò security boundary** — chỉ là giá trị placeholder để khung dữ liệu tuân thủ đúng 1 format framing thống nhất, đúng nghĩa "ephemeral, thương lượng trong chính phiên kết nối" đã chốt ở ADR-19 (không đổi ý nghĩa ADR-19, chỉ cụ thể hoá bit-level).

**Mở rộng phạm vi ADR-19 sang pipe `Watchdog`/`Uninstaller` (Đợt 4)**: cả `Watchdog` lẫn `Uninstaller.exe` **không** có quan hệ cha-con với `Service` (giống hệt tình huống của `UI` mà ADR-19 đã giải quyết — `Watchdog` tự khởi động qua SCM song song `Service`, `Uninstaller.exe` do user/Windows invoke) nên không thể dùng bootstrap khoá bền vững qua anonymous pipe kế thừa handle (mục 5.2, chỉ khả thi cho tiến trình con spawn qua `CreateProcessAsUser`). Áp dụng **nguyên xi** cơ chế ephemeral session key + khoá hằng số 32-byte-zero cho `Hello`/`HelloAck` đầu tiên (mục 5.3 ở trên) cho cả 2 pipe mới này — không cần thiết kế cơ chế bootstrap thứ 3. `HelloAck.session_key` (mục 3.2) từ nay set cho **3 kênh** (`UI`, `Watchdog`, `Uninstaller`), rỗng chỉ với `Vision`/`Overlay` (2 kênh duy nhất có quan hệ cha-con thật, dùng khoá persistent DPAPI ở mục 5.2).

### 5.4 Xử lý khi verify fail

- Verify dùng so sánh **constant-time** (tránh timing attack dò khoá qua độ trễ so sánh).
- HMAC sai (đã qua được bước xác thực danh tính ở mục 4.2, nghĩa là danh tính process đúng nhưng nội dung/khoá không khớp — ví dụ bootstrap lỗi, hoặc cố tình giả mạo nội dung sau khi đã bị impersonate ở tầng khác) → **đóng kết nối ngay, không gửi bất kỳ phản hồi lỗi nào về phía bên kia** (tránh tạo oracle cho kẻ tấn công dò đúng/sai khoá qua nội dung phản hồi, ADR-22), ghi audit log (metadata only: tên pipe, PID nếu còn lấy được, timestamp — theo đúng mẫu hình `BE-014`).
- **Không crash** tiến trình `Service` ở bất kỳ trường hợp verify fail nào — đây là input không tin cậy từ 1 kênh IPC nội bộ, xử lý như bất kỳ input không tin cậy nào khác (liên hệ `SEC` mục 2 threat "Elevation of Privilege": *"Service không parse dữ liệu phức tạp từ input không tin cậy"* — ở đây cụ thể hoá thành: parse xong mới verify hay verify xong mới parse đều phải bọc try/catch, lỗi parse cũng xử lý như HMAC fail, không để exception lan ra ngoài làm crash toàn bộ `Service`).
- File này **không** định nghĩa thêm cơ chế cảnh báo phụ huynh (Toast) riêng cho lỗi HMAC — `BE-061b` chỉ áp dụng cho sự kiện mất toàn vẹn `config.db`, không mở rộng phạm vi sang lỗi IPC ở đây để tránh phát minh thêm requirement sản phẩm ngoài spec. Sự kiện được ghi đủ vào audit log, phụ huynh xem được qua Dashboard (`BE-014`) là đủ ở mức thiết kế hiện tại.

## 6. Timeout & xử lý lỗi

| Tình huống | Phát hiện bằng | Xử lý |
|---|---|---|
| `Vision`/`Overlay` crash (process chết hẳn) | Pipe vỡ (`IOException` khi đọc/ghi — "Pipe is broken"/"Pipe has been ended") | Coi là tín hiệu crash **ngay lập tức**, không chờ đủ 3 heartbeat miss (nhanh hơn ngân sách `BE-023` `≤ 3s`) — `Service` kill nếu còn treo, spawn lại, tái thực hiện toàn bộ trình tự mục 4 |
| `Vision`/`Overlay` treo (process còn sống nhưng không phản hồi) | Mất 3 `HeartbeatAck` liên tiếp theo cadence ở mục 2.1 (`BE-040`) | Giống hệt xử lý ở trên |
| `Service` restart/crash (từ góc nhìn `Vision`/`Overlay`) | Pipe vỡ phía client | Client vào lại vòng lặp retry-connect (mục 4.1); trong lúc chờ kết nối lại: `Vision` **dừng vòng lặp capture/inference** (không có nơi báo cáo kết quả, và không tự có nguồn cấu hình để tự quyết — `SEC-017`); `Overlay` **giữ nguyên các overlay đang hiển thị** (không tự ý ẩn) — đúng nguyên tắc fail-secure (`BE-061a`/`ANTI-070`: gián đoạn kênh điều khiển không được hiểu là "đã hết vi phạm"), chỉ cập nhật lại khi nhận `OverlayRectListCommand` mới sau khi reconnect thành công |
| Pipe server bận (`UI` cố connect khi đã có 1 phiên `UI` khác đang giữ pipe) | `IOException`/timeout phía client khi connect | ADR: pipe `UI` giới hạn **1 kết nối đồng thời** (đơn giản hoá Đợt 0 — Dashboard không có yêu cầu nhiều cửa sổ mở đồng thời ở spec hiện tại); client `UI` thứ 2 nhận lỗi connect timeout, tự hiển thị thông báo "đang có phiên khác" ở tầng `UI` (chi tiết UX để ở `10-ui-architecture.md`, Đợt 6) |
| Message vượt giới hạn 64 KB (mục 2.3) | Đọc 4-byte length-prefix trước khi đọc payload | Đóng kết nối ngay, không đọc phần payload, ghi audit log |
| HMAC verify fail | Mục 5.4 | Đóng kết nối, ghi audit log, không phản hồi |
| Xác thực danh tính (ACL/chữ ký) fail | Mục 4.2 | Đóng kết nối ngay từ trước khi đọc `Hello`, ghi audit log |

Sau mỗi lần 1 pipe instance bị đóng (do client tự ngắt, do lỗi ở bảng trên, hay do `Service` chủ động đóng) — server phải **ngay lập tức** tạo lại 1 `NamedPipeServerStream` mới ở trạng thái `WaitForConnectionAsync` cho đúng pipe đó, không để khoảng trống thời gian nào mà pipe không sẵn sàng nhận kết nối kế tiếp (đây là điều kiện để retry-connect ở mục 4.1 thực sự hội tụ nhanh).

## 7. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-15 | 3 pipe riêng biệt (Vision/Overlay/UI) thay vì 1 pipe dùng chung, mỗi pipe chỉ chấp nhận đúng nhóm message type của nó | Least-privilege ở tầng IPC (`SEC-002`); cadence heartbeat độc lập; sự cố 1 kênh không lan sang kênh khác |
| ADR-16 | Envelope `IpcPayload` dùng `oneof` cho phần thân, đánh field number theo khối 20 số/domain | Thêm message type mới không phá vỡ client cũ (proto3 forward-compat), đúng nguyên tắc mở rộng `ROADMAP.md` mục 2 |
| ADR-17 | Chữ ký HMAC là trailer ngoài message (length-prefixed framing), không nhúng vào bên trong chính message Protobuf | Tránh vấn đề tự tham chiếu (field chữ ký nằm trong nội dung đang được ký) |
| ADR-18 | Bootstrap khoá HMAC cho `Vision`/`Overlay` qua anonymous pipe kế thừa handle lúc `CreateProcessAsUser`, không qua env var/tham số dòng lệnh | Handle kế thừa chỉ tiến trình con đó đọc được, không tiến trình nào khác trên máy truy cập được — an toàn hơn env var (đọc được qua `ReadProcessMemory` bởi tiến trình khác cùng user) |
| ADR-19 | Kênh `UI` dùng session key ephemeral thương lượng ngay trong phiên kết nối, không dùng khoá persistent qua DPAPI như `Vision`/`Overlay` | `UI` không có quan hệ cha-con với `Service` để bootstrap khoá bền vững an toàn; xác thực danh tính chính của `UI` dựa vào ACL + chữ ký code-signing (mục 4.2), HMAC ephemeral chỉ là lớp phòng thủ bổ sung cho riêng phiên đó |
| ADR-20 | Dùng Named Pipe thô + Protobuf cho cả kênh `Service↔UI`, không dùng gRPC over Named Pipe binding | Giữ đồng nhất 1 stack công nghệ cho cả 3 kênh, giảm dependency; nhu cầu request/response của Dashboard đáp ứng đủ bằng `correlation_id` trong envelope, không cần framework RPC đầy đủ |
| ADR-21 | Giới hạn kích thước message tối đa 64 KB | Đủ dư cho toàn bộ payload nghiệp vụ hiện tại (tối đa 10 overlay rect — `BE-088`); dữ liệu lớn hơn (lịch sử audit log) phải phân trang ở tầng message |
| ADR-22 | Thất bại xác thực danh tính hoặc HMAC → đóng kết nối ngay, không gửi phản hồi lỗi nào | Tránh tạo oracle cho kẻ tấn công dò khoá/định danh qua nội dung phản hồi lỗi |
| ADR-23 (v0.2.0) | Thêm case `SERVICE = 5` vào enum `ProcessType`, giữ nguyên số hiệu các case đã có | `Service` cần tự khai báo tường minh là sender ở field `sender` của envelope khi chính nó chủ động gửi (`HelloAck`, `HeartbeatPing`, `GracefulStopCommand`, `ControlVisionCommand`, `OverlayRectListCommand`, `ShowToastCommand`...) — enum thiếu case này lộ ra khi `feature-dev` code Đợt 0, phải tạm lách bằng `PROCESS_TYPE_UNSPECIFIED`. Chỉ thêm case mới, không đổi số case cũ để tránh vỡ wire-compat |
| ADR-52 (v0.3.0) | Chế độ overlay gộp (`BE-088`/`BE-089`) hiện thực hoá bằng 2 field mới cộng thêm vào `OverlayRect` (`is_merged`/`merged_window_handles`), tái dùng nguyên `OverlayRectListCommand`/`ForceCloseRequest` đã có — không tạo message type mới | Additive, không phá vỡ client cũ (đúng ADR-16); giữ đúng 1 shape danh sách duy nhất mà `Overlay` đã biết cách diff (dictionary theo `window_handle`, xem `Architecture/07-overlay-architecture.md` mục 3) thay vì dạy `Overlay` hiểu thêm 1 loại message hoàn toàn khác cho cùng 1 khái niệm "danh sách rect cần che" |
| ADR-68 (v0.4.0) | Thêm field `source`/enum `CloseSource` vào `ForceCloseRequest` (field 4) thay vì tạo message riêng cho auto-timeout | Additive (đúng ADR-16); `Service`-side `HandleForceCloseAsync` không cần phân nhánh loại message, chỉ đọc thêm 1 field để ghi audit log (`BE-089b`) |
| ADR-69 (v0.4.0) | 3 message mới cho icon trạng thái (`MonitoringStatusUpdate`/`IconPositionUpdate`/`IconLayoutSync`) đặt field 63-65, dùng field number rời thay vì gộp chung 1 message tổng hợp | Mỗi message có chiều gửi và tần suất khác nhau (`MonitoringStatusUpdate`/`IconLayoutSync`: Service→Overlay, theo sự kiện; `IconPositionUpdate`: Overlay→Service, theo hành động kéo-thả) — tách riêng giữ đúng nguyên tắc 1 message = 1 sự kiện nghiệp vụ, nhất quán với các message khác trong kênh Overlay |
| ADR-80 (v0.5.0) | Password/Auth (Đợt 3) chiếm field 80-91 trong khối UI 80-99 đã dành sẵn, để 92-99 cho Đợt 6 | Định nghĩa đầy đủ + lý do ở `08-password-authentication-architecture.md` mục 8 — amendment tối thiểu, không cần khối field number mới |
| ADR-82 (v0.5.1) | Cụ thể hoá "chữ ký rỗng" (ADR-19) của khung `Hello`/`HelloAck` đầu tiên kênh `UI` bằng 1 khoá HMAC hằng số 32-byte-zero biết trước cả 2 phía, thay vì thêm 1 chế độ framing "không HMAC" riêng vào `IpcFrameTransport` | Giữ `IpcFrameTransport` chỉ hiểu đúng 1 định dạng frame cho mọi kênh/mọi giai đoạn; an toàn vì xác thực danh tính thật của `UI` đã xảy ra ở mục 4.2 trước khi đọc `Hello`, HMAC trên 2 frame này chưa từng là security boundary |
| ADR-85 (v0.6.0) | 2 pipe mới `ParentalGuard.Svc.Watchdog`/`ParentalGuard.Svc.Uninstaller`, field block mới 100-119/120-139, `ProcessType.UNINSTALLER=6` | Thiết kế đầy đủ + lý do ở `09-anti-tamper-architecture.md` mục 3/5/8.3 (ADR-85/91/92/93 ở file đó) |
| ADR-94 (v0.6.0) | Thêm bước 5 vào mục 4.2 — đối chiếu `Hello.process_type` khớp đúng loại tiến trình dự kiến của từng pipe, áp dụng hồi tố cho cả 3 pipe Đợt 0 | Defense-in-depth bổ sung khi thiết kế 2 pipe mới cho `Watchdog`/`Uninstaller` (`09-anti-tamper-architecture.md` ADR-94) — không đổi hành vi luồng hợp lệ hiện tại |
| ADR-107 (v0.7.0) | Pause/Resume (Đợt 5) chiếm field 92-97 trong khối UI 80-99 đã dành sẵn (thu hẹp từ "92-99 dành cho Đợt 6" xuống còn "98-99"), 6 message riêng (không gộp Pause+Resume+Status vào 1 message tổng hợp) | Đúng quy ước "khối field/domain" đã có (ADR-16/ADR-80); mỗi message vẫn giữ 1 sự kiện nghiệp vụ riêng (activate/resume-sớm/query trạng thái), nhất quán cách tách message đã áp dụng cho icon trạng thái (ADR-69) |

## 8. Câu hỏi mở

- [x] ~~Giao thức `Watchdog ↔ Service` (Named Pipe riêng hay SCM query) — đã ghi nhận là thuộc `09-anti-tamper-architecture.md`, không lặp lại ở đây (kế thừa từ câu hỏi mở ở `02-process-architecture.md` mục 8).~~ — **Đã xong** (`09` v0.1.0, Đợt 4: Named Pipe riêng, xem mục 2.1/3.1a/5.2).
- [x] ~~Message type cụ thể cho phần còn lại của kênh `Service ↔ UI` (field 92-99 — query cấu hình, lịch sử audit log, trạng thái pause...)~~ — **Phần "trạng thái pause" đã xong** (field 92-97, Đợt 5, mục 3.6 — `PauseMonitoringRequest`/`Response`, `ResumeMonitoringRequest`/`Response`, `PauseStatusQuery`/`Response`). Còn lại field **98-99** (query cấu hình, lịch sử audit log) vẫn để quyết định khi thiết kế `10-ui-architecture.md` ở Đợt 6, không phải thiếu sót của file này.

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.7.0 | 2026-09-20 | MINOR — Đợt 5 (`ROADMAP.md`, Pause/Resume), amendment cùng lượt viết `02-process-architecture.md` mục 3a. Thêm mục 3.6 (6 message mới, field 92-97 khối UI): `PauseMonitoringRequest`/`Response`, `ResumeMonitoringRequest`/`Response`, `PauseStatusQuery`/`Response` — enum `PauseDuration` (5 lựa chọn `PAUSE-002`), `PauseResult`/`ResumeResult` (kèm `ALREADY_PAUSED`/`NOT_PAUSED` idempotent guard). Additive, không đổi field/message cũ (đúng ADR-16). Thu hẹp comment "92-99 dành cho Đợt 6" xuống còn "98-99" (đóng 1 phần open question mục 8 — phần "trạng thái pause" đã xong, phần audit log/config query vẫn để Đợt 6). Làm rõ tường minh `ControlVisionCommand.reserved 20 to 29` (để ngỏ từ Đợt 0 cho "cờ suspend/tần suất heartbeat Pause") **cố tình không dùng** ở Đợt 5 — cơ chế đã giải quyết đầy đủ không cần field IPC mới (`05` ADR-40 + `02` mục 3a.4), tránh hiểu nhầm là gap còn sót. 1 ADR mới (107). Theo chỉ đạo — không dừng chờ review |
| v0.6.0 | 2026-09-19 | MINOR — Đợt 4 (`ROADMAP.md`), amendment cùng lượt viết `09-anti-tamper-architecture.md`. Thêm 2 pipe mới (`ParentalGuard.Svc.Watchdog`, `ParentalGuard.Svc.Uninstaller` — mục 2.1/2.2), field block mới 100-119 (Watchdog: `WatchdogReportEvent`/`Ack`)/120-139 (Uninstaller: `UninstallExecuteRequest`/`Response` — mục 3.5 mới), `ProcessType.UNINSTALLER=6` (`WATCHDOG=4` từ "reserved" chuyển sang dùng thật). Thêm mục 3.1a (bảng whitelist message theo pipe, hệ thống hoá lần đầu đầy đủ cho cả 5 pipe — trước đó chỉ có nguyên tắc chung ở mục 2.1). Thêm bước 5 vào mục 4.2 (đối chiếu `Hello.process_type` khớp đúng pipe, ADR-94, áp dụng hồi tố 3 pipe cũ). Mở rộng phạm vi ADR-19 (session key ephemeral) sang pipe `Watchdog`/`Uninstaller` (mục 5.3) — không cần bootstrap thứ 3. Sửa lỗi đồng bộ phát hiện khi viết `09`: 3 field `recovery_key_plaintext`/`new_recovery_key_plaintext`×2 (mục 3.4) đổi `string`→`bytes` cho khớp đúng `08-password-authentication-architecture.md` v0.3.0 (ADR-83, FAIL 1) — bản sao `.proto` ở file này trước đó chưa được đồng bộ dù changelog `08` có ghi đã đồng bộ. Đóng câu hỏi mở giao thức `Watchdog↔Service` (mục 8). 2 ADR mới (85, 94 — chi tiết đầy đủ ở `09`). Theo chỉ đạo — không dừng chờ review |
| v0.5.1 | 2026-09-20 | PATCH — cụ thể hoá mục 5.3 (ADR-82): "chữ ký rỗng" của khung `Hello`/`HelloAck` đầu tiên kênh `UI` (trước khi có session key) nay đặc tả rõ bit-level = khoá HMAC hằng số 32-byte-zero biết trước cả 2 phía, không phải bỏ hẳn trailer HMAC hay thêm 1 chế độ framing riêng cho `IpcFrameTransport`. Gap `feature-dev` báo cáo lại sau khi implement `UiSessionServer` Đợt 3 (`docs/dependency-map.md` mục "Khoảng trống đã biết — Đợt 3") — xác nhận cách `UiSessionServer._unsignedHelloKey` đã tự chọn là lựa chọn kỹ thuật hợp lý (transport giữ đúng 1 định dạng frame duy nhất; an toàn vì xác thực danh tính thật của `UI` đã xảy ra ở mục 4.2 trước khi đọc `Hello`). Không đổi ý nghĩa ADR-19, không đổi hành vi/code, chỉ chính thức hoá quy ước để `ParentalGuard.UI` (Đợt 6) implement đúng khớp. Không kéo theo sửa `Specification/` hay file `Architecture/` khác |
| v0.5.0 | 2026-09-19 | MINOR — Đợt 3 (`ROADMAP.md`), viết `08-password-authentication-architecture.md`: thêm 12 message mới (field 80-91, khối UI 80-99) cho Password & Authentication (`SetInitialPasswordRequest/Response`, `ConfirmRecoveryKeySavedRequest/Response`, `AuthVerifyRequest/Response`, `ChangePasswordRequest/Response`, `RecoveryResetRequest/Response`, `AuthStatusQuery/Response` — mục 3.4 mới), additive, không đổi field/message cũ (đúng ADR-16). Sửa 1 comment field-number sót lại từ trước ("khối UI... Đợt 6 (Architecture/08)" — tham chiếu số cũ trước renumbering Đợt 2, chưa từng được cập nhật — nay sửa đúng + tách rõ 80-91 đã dùng/92-99 còn trống). Đổi 2 tham chiếu `08-anti-tamper-architecture.md`/`09-ui-architecture.md` thành `09-anti-tamper-architecture.md`/`10-ui-architecture.md` theo renumbering ở `00-INDEX.md` khi chèn `08-password-authentication-architecture.md` mới. 1 ADR mới (80, định nghĩa đầy đủ ở `08`) |
| v0.4.0 | 2026-09-19 | MINOR — amendment cùng lượt viết lại `07-overlay-architecture.md` v0.2.0 (2 quyết định sản phẩm mới chốt: `BE-088a`/`BE-089a`/`BE-089b`, `FE-016f`/`FE-016g`). Thêm field `source`/enum `CloseSource` (field 4) vào `ForceCloseRequest` — phân biệt "manual"/"auto-timeout" cho audit log (`BE-089b`, ADR-68). Thêm 3 message mới cho UX icon trạng thái multi-monitor mở rộng đầy đủ trong Đợt 2 (`FE-020`–`022`): `MonitoringStatusUpdate` (field 63, Service→Overlay, `IconState` 3 trạng thái), `IconPositionUpdate` (field 64, Overlay→Service, kết quả kéo-thả), `IconLayoutSync` (field 65, Service→Overlay, đẩy lại toàn bộ vị trí đã lưu lúc connect) — ADR-69. Cập nhật diagram handshake mục 4.3 (push thêm `MonitoringStatusUpdate`/`IconLayoutSync` ngay sau `OverlayRectListCommand`). Toàn bộ additive, không đổi field/message cũ (đúng ADR-16) |
| v0.3.0 | 2026-09-19 | MINOR — Đợt 2 (`ROADMAP.md`), viết `07-overlay-architecture.md`: thêm 2 field mới `is_merged`/`merged_window_handles` vào `OverlayRect` (field 6/7, additive, không đổi field cũ) để hiện thực hoá chế độ overlay gộp `BE-088`/`BE-089` khi vượt quá 10 overlay đồng thời — không tạo message type mới (ADR-52), tái dùng nguyên `OverlayRectListCommand`/`ForceCloseRequest`. Làm rõ tường minh ngữ nghĩa `monitor_id` (chỉ dùng để nhóm, không dùng để suy toạ độ — Overlay luôn tự resolve qua Win32 cục bộ) và `overlay_id` ở chế độ gộp. Đổi 4 tham chiếu `07-anti-tamper-architecture.md`/`08-ui-architecture.md` thành `08-anti-tamper-architecture.md`/`09-ui-architecture.md` theo renumbering ở `00-INDEX.md`. Đồng bộ với đổi 4 tham chiếu tương tự ở `02-process-architecture.md` v0.1.2 và `06-security-architecture.md` v0.1.1 |
| v0.1.0 | 2026-09-17 | Khởi tạo — 3 Named Pipe (Vision/Overlay/UI), ACL theo SID + xác thực chữ ký code-signing lúc connect, framing length-prefixed + HMAC-SHA256 trailer, envelope Protobuf mở rộng được (`oneof` theo khối field number), message schema Đợt 0 (Hello/Heartbeat/GracefulStop/ControlVision) + Đợt 1 (VisionInferenceResult/OverlayRectListCommand/ForceCloseRequest), bootstrap khoá HMAC qua anonymous pipe kế thừa handle cho Vision/Overlay, session key ephemeral cho UI, bảng xử lý timeout/lỗi, 8 ADR (15-22) |
| v0.2.0 | 2026-09-17 | Amendment phát sinh từ code thật Đợt 0 (2 gap `feature-dev` báo cáo lại lúc implement `FailSecureConfigLoader`), không đổi kiến trúc lớn: (1) thêm message `ShowToastCommand` + enum `ToastSeverity` vào field 62 (khối domain Overlay 60-79, mục 3.1/3.2) — lấp thiếu message type cho Service ra lệnh Overlay hiển thị Toast (`BE-061b`, bước 7 luồng fail-secure ở `04-data-architecture.md` mục 6.2), thiết kế tổng quát tái dùng được cho các cảnh báo khác, không hard-code riêng fail-secure; (2) thêm case `SERVICE = 5` vào enum `ProcessType` (ADR-23) — trước đó thiếu case cho chính Service tự làm sender, code phải tạm lách bằng `PROCESS_TYPE_UNSPECIFIED`, giữ nguyên số hiệu các case cũ. Cập nhật ghi chú field-number comment khối Overlay (63-79 vẫn dành cho Đợt 2 overlay gộp), ghi chú traceability mục 3.3 |

# 09 — Anti-Tamper Architecture

> Version: v0.1.1 | Trạng thái: Approved | Cập nhật: 2026-09-20

## 1. Mục đích và phạm vi

File này trả lời **HOW** cho toàn bộ `ANTI-010`–`ANTI-070b` (`Specification/05-anti-uninstall-tamper-spec.md`, Approved): Dual Watchdog (`ANTI-010`/`011`), custom uninstaller chặn gỡ cài đặt qua Control Panel/Settings (`ANTI-020`), ACL/giám sát tamper file & registry (`ANTI-030`/`031`), rate-limit & cảnh báo tấn công liên tục (`ANTI-060`/`061`), và phạm vi mở rộng của fail-secure (`ANTI-070`) cho riêng domain anti-tamper. Không phát minh yêu cầu sản phẩm mới — mọi quyết định trích dẫn ngược `ANTI-0xx`, hoặc là ADR thuần kỹ thuật (mục 9).

File này đóng 2 câu hỏi mở đã treo từ Đợt 0 (`02-process-architecture.md` mục 8, `03-ipc-communication.md` mục 8): giao thức `Service ↔ Watchdog` cụ thể, và tên pipe tương ứng.

File này **không** thiết kế lại: (a) ACL nền tảng cho `%ProgramFiles%`/`%ProgramData%`/registry Service key — đã chốt đủ ở `06-security-architecture.md` mục 4, chỉ bổ sung 1 executable mới (`Uninstaller.exe`) vào bảng đã có và thiết kế phần **giám sát chủ động** thay đổi bất thường (phần `06` mục 4.3 để ngỏ tường minh cho file này); (b) cơ chế `AuthVerifyRequest`/`action_token` — đã chốt đủ ở `08-password-authentication-architecture.md` mục 7.2 (hằng số `action_context="uninstall"` đã dành sẵn), file này chỉ **tái sử dụng** nguyên trạng; (c) đóng gói installer MSI/MSIX thật (ký SignPath, GitHub Release) — thuộc `11-deployment-release-architecture.md` (Đợt 9, chưa viết); file này chỉ thiết kế **hành vi của chính `ParentalGuard.Uninstaller.exe`** (độc lập với cách nó được đóng gói/phân phối) vì đây là logic nghiệp vụ anti-tamper cốt lõi, cần có ngay ở Đợt 4 để `feature-dev` implement và test được (không phải chờ Đợt 9).

## 2. Xác nhận các file đã có đủ cho Đợt 4 (không cần amendment nội dung lớn)

| Nhu cầu Đợt 4 | Đã có ở đâu | Kết luận |
|---|---|---|
| ACL `%ProgramFiles%`/`%ProgramData%`/registry Service key | `06` mục 4 | Đủ nền tảng — bổ sung 1 dòng (Uninstaller.exe) vào bảng 4.1, sửa 1 tham chiếu số file còn sót (mục 8.4) |
| Cổng xác thực mật khẩu dùng chung + `action_token` | `08` mục 7.2, hằng số `action_context="uninstall"` đã dành sẵn | Đủ — tái dùng nguyên vẹn, không đổi |
| `AuthState` RAM-only, rate-limit dùng chung 1 bộ đếm | `08` mục 3/7.7 | Đủ — luồng uninstall verify password đi đúng qua `AuthVerifyRequest`, tự động hưởng rate-limit đã có |
| Named Pipe transport (framing, HMAC, xác thực danh tính qua code-signing) | `03` mục 2-5 | Đủ hạ tầng — cần **thêm 2 pipe mới** + field block mới trong `oneof` (mục 8 file này) |
| `Watchdog` chạy LocalSystem, là 1 trong 5 process cốt lõi | `01-tong-quan-kien-truc.md` mục 3, ADR-05 | Đủ — file này chỉ cụ thể hoá giao thức |
| Audit log tamper-evident, event_type mở rộng được | `04` mục 5 | Đủ khung — cần thêm 6 dòng `event_type` mới (mục 8 file này) |

Không có gap WHAT nào cần `spec-maintainer` cho phần này.

## 3. Dual Watchdog (`ANTI-010`, `ANTI-011`)

### 3.1 Nguyên tắc tối giản của `Watchdog` (ADR-86)

`Watchdog` là "người gác cuối cùng" — nếu chính nó có nhiều dependency/bug, toàn bộ lớp phòng thủ kép mất ý nghĩa. Vì vậy **`Watchdog` cố tình KHÔNG đọc `config.db`/`auth.dat`, không dùng SQLite/DPAPI, không tự ghi `audit.log`** (khác hẳn `Service`) — bề mặt lỗi tối thiểu, chỉ 3 việc: (1) heartbeat với `Service` qua Named Pipe, (2) gọi Service Control Manager (SCM) để start/stop/tạo lại service, (3) báo cáo sự kiện về `Service` qua IPC để `Service` ghi `audit.log` (single-writer, đúng nguyên tắc "1 nguồn sự thật" đã áp dụng cho state nghiệp vụ ở `02-process-architecture.md` ADR-12 — nay áp dụng luôn cho quyền ghi audit log).

`Watchdog` chạy `LocalSystem` (giống `Service`, đúng bảng `01-tong-quan-kien-truc.md` mục 3) — cần quyền SYSTEM để gọi SCM start/stop/create trên cả chính nó lẫn `Service`.

### 3.2 Giao thức `Service ↔ Watchdog` (đóng câu hỏi mở `02`/`03`, ADR-85)

**Quyết định: Named Pipe riêng `ParentalGuard.Svc.Watchdog`** (không dùng thuần SCM status polling) — lý do: heartbeat qua pipe phát hiện được cả "process còn sống nhưng treo" (không chỉ "process đã chết hẳn" mà SCM query mới thấy), nhất quán với cơ chế đã dùng cho `Vision`/`Overlay` (`03` mục 6), tái dùng gần như nguyên vẹn hạ tầng framing/HMAC/xác thực danh tính đã có — không phát minh cơ chế IPC thứ 2 chỉ cho riêng cặp process này.

- **Server**: `Service` (giống mô hình `Vision`/`Overlay`) — tạo `NamedPipeServerStream` lúc `Starting`, `Watchdog` là client, tự retry-connect theo đúng backoff đã chốt ở `03` mục 4.1 (200ms→3200ms, lặp vô hạn) vì 2 service khởi động song song không có thứ tự phụ thuộc (`02` mục 6).
- **ACL pipe** (mở rộng mục 2.2 của `03`): cả 2 process đều `LocalSystem` — chỉ cần **Allow `NT AUTHORITY\SYSTEM`** (Full Control), **Deny** tường minh `Everyone`/`ANONYMOUS LOGON`/`Guests`/`INTERACTIVE` (khác hẳn pipe `Vision`/`Overlay`/`UI` — pipe này **không** có SID người dùng nào được phép, kể cả phụ huynh Administrator). Không cần logic "recreate theo session đổi" như `Vision`/`Overlay` (SYSTEM không gắn với session tương tác).
- **Xác thực danh tính**: áp dụng đúng quy trình `03` mục 4.2 (đối chiếu đường dẫn cài đặt + chữ ký Authenticode), cộng thêm ràng buộc mới (mục 8.4 file này): pipe `Watchdog` chỉ chấp nhận `Hello.process_type == WATCHDOG`.
- **Handshake**: dùng lại nguyên `Hello`/`HelloAck` (không cần `session_key`, giống `Vision`/`Overlay` — phòng chống giả mạo đã đủ bằng ACL SYSTEM-only + code-signing, không cần thêm lớp HMAC ephemeral).

### 3.3 Heartbeat cadence & ngưỡng phát hiện (ADR-87)

| Kênh | Chu kỳ | Ngưỡng coi là crash | Ghi chú |
|---|---|---|---|
| `Service` ↔ `Watchdog` | **3 giây** | Mất 3 heartbeat liên tiếp (9 giây), HOẶC pipe vỡ ngay lập tức (đúng mẫu hình `03` mục 6 — không chờ đủ ngưỡng nếu tín hiệu rõ ràng hơn) | Cadence chậm hơn `Vision` (1s)/`Overlay` (2s) có chủ đích: khởi động lại 1 Windows Service (qua SCM, có thể phải tự đăng ký lại) nặng hơn nhiều so với respawn 1 child process — tránh false positive trong lúc chính `Service` đang bận xử lý khối lượng công việc lớn lúc `Starting` (đọc config, áp WFP, ACL self-heal) |

Bổ sung dòng heartbeat này vào bảng mục 4 của `02-process-architecture.md` (đang để trống "để ở `09`" — xem mục 8.1 file này).

### 3.4 Hành động khôi phục khi phát hiện peer mất tích (ADR-88)

Đối xứng cho cả 2 chiều (`Service` phát hiện `Watchdog` mất tích, và ngược lại) — bên còn sống thực hiện đúng trình tự sau đối với bên kia:

```
1. Thử dừng "sạch" qua SCM: ServiceController(peerName).Stop(), chờ tối đa 5 giây.
2. Nếu sau 5 giây vẫn không chuyển Stopped: lấy PID qua ServiceController hoặc
   Process.GetProcessesByName(peerExeName), Process.Kill() cưỡng bức (peer đã treo,
   không phản hồi SCM — không có gì để mất khi force-kill).
3. ServiceController(peerName).Start() — truyền startup args [ "--restarted-by-watchdog" ]
   qua ServiceBase.OnStart(string[] args) để bên vừa khởi động lại BIẾT nó vừa được
   bên kia hồi sinh (dùng để ghi audit log đúng nguyên nhân, mục 3.6).
4. Nếu bước 3 thất bại với lỗi "service không tồn tại" (registry/service definition
   bị xoá hoàn toàn, đúng ANTI-010 điểm 3): gọi CreateService() (P/Invoke advapi32,
   hoặc spawn `sc.exe create` — xem ADR-88 lý do chọn `sc.exe`) với bộ tham số tối
   thiểu HARD-CODE sẵn trong chính binary của bên đang thực hiện khôi phục:
     - ServiceName/DisplayName: hằng số đã biết trước (không đọc từ đâu khác)
     - BinaryPathName: <thư mục cài đặt của chính process đang chạy>\<tên exe của
       peer> — suy ra RUNTIME qua Process.GetCurrentProcess().MainModule.FileName
       lấy thư mục cha (2 exe luôn cùng thư mục cài đặt, 04-data-architecture.md
       mục 2), KHÔNG hard-code đường dẫn tuyệt đối (nhất quán nguyên tắc đã áp dụng
       cho WFP App ID, 06-security-architecture.md mục 3.1)
     - StartType = Automatic (SERVICE_AUTO_START, không Delayed — 02 mục 2.1)
     - Account = LocalSystem
   Sau khi tạo xong, quay lại bước 3 (Start với cùng startup args).
5. Ghi nhận kết quả (thành công/thất bại) — bên "Service" tự ghi thẳng vào audit.log
   (đã có quyền); bên "Watchdog" gửi WatchdogReportEvent (mục 8.3) cho Service ngay
   khi kết nối lại được (không tự ghi audit.log, đúng mục 3.1).
```

**Vì sao dùng `sc.exe create` (spawn subprocess) thay vì P/Invoke `CreateService` trực tiếp (bổ sung ADR-88)**: nhất quán tinh thần thận trọng dependency/native API đã áp dụng xuyên suốt dự án (`06` ADR-30, `08` ADR-71 — ưu tiên API ít rủi ro implement sai hơn cho dự án 1 người maintain) — `sc.exe` là công cụ built-in Windows, đã được kiểm chứng hàng thập kỷ, tránh phải tự quản lý đúng struct `SERVICE_STATUS`/marshalling phức tạp của `CreateServiceW` qua P/Invoke. Chi phí spawn thêm 1 process cho 1 sự kiện **cực hiếm gặp** (chỉ khi registry bị xoá hoàn toàn) là chấp nhận được.

### 3.5 Windows Service Recovery Options (`ANTI-011`, ADR-89)

Lớp bảo vệ bổ sung (built-in Windows, theo đúng chữ nghĩa `ANTI-011`) — **không chờ đến installer Đợt 9**: cả `Service` và `Watchdog` tự cấu hình Recovery Options của chính mình mỗi lần `Starting`, idempotent (đúng mẫu hình self-heal ACL đã có ở `06` ADR-37), qua spawn `sc.exe failure <ServiceName> reset= 86400 actions= restart/60000/restart/120000/restart/300000`:

| Tham số | Giá trị | Ý nghĩa |
|---|---|---|
| `reset` | 86400 giây (1 ngày) | Sau 1 ngày không có failure mới, đếm lại từ lần fail thứ 1 |
| Lần fail 1 | Restart sau 60 giây | |
| Lần fail 2 | Restart sau 120 giây | |
| Lần fail 3+ | Restart sau 300 giây | |

Số liệu này **không phải ngưỡng bảo mật cốt lõi** (đúng như spec đã mô tả: "lớp bảo vệ bổ sung miễn phí" — cơ chế chính là heartbeat mục 3.2/3.3) — chỉ là tham số độ tin cậy hệ điều hành, quyết định trực tiếp ở đây, không cần chủ dự án xác nhận. Recovery Options **chỉ kích hoạt khi service tự thoát bất thường** (exit code khác 0, crash) — hành động `Stop()` chủ động của Watchdog/Service ở mục 3.4 bước 1 (dừng "sạch") không kích hoạt Recovery, tránh xung đột 2 cơ chế khôi phục chạy chồng lên nhau.

### 3.6 Ghi nhận sự kiện restart (`WatchdogReportEvent`, mục 8.3) và liên kết `ANTI-060`

- Bên vừa khởi động lại (nhận thấy startup arg `--restarted-by-watchdog`) tự ghi `audit.log` event `ProcessRestarted{process: "Service"|"Watchdog", trigger: "watchdog_peer_recovery"}` (mở rộng phạm vi event đã có, mục 8.2) — **chỉ áp dụng cho bên "Service"** (có quyền ghi trực tiếp); bên "Watchdog" không tự ghi (mục 3.1), thay vào đó gửi `WatchdogReportEvent` cho `Service` ngay khi 2 bên kết nối lại (dù ai vừa restart ai) để `Service` là nơi duy nhất ghi audit log cho cả 2 chiều.
- Mỗi sự kiện restart (dù `Service` restart `Watchdog` hay ngược lại) đều tính vào bộ đếm `ANTI-060` — thiết kế đầy đủ ở mục 6.

## 4. Giám sát chủ động thay đổi registry bất thường (`ANTI-031` phần "watchdog phát hiện", ADR-90)

`06-security-architecture.md` mục 4.3 đã chốt baseline ACL (registry key Service chỉ `SYSTEM`/`Administrators` ghi được, để ngỏ tường minh phần "chủ động giám sát" cho file này.

### 4.1 Cơ chế: `RegNotifyChangeKeyValue`, không polling

Cả `Service` lẫn `Watchdog` đăng ký giám sát **CẢ 2** registry key (`HKLM\SYSTEM\CurrentControlSet\Services\ParentalGuardService` và `...\ParentalGuardWatchdog`) qua `RegNotifyChangeKeyValue` (Win32 API, `REG_NOTIFY_CHANGE_LAST_SET`, cờ `bWatchSubtree=false`, chạy bất đồng bộ trên 1 thread nền chờ blocking, không polling — nhất quán nguyên tắc "dùng cơ chế OS notification, không tự viết vòng lặp polling" đã áp dụng cho `EventLogWatcher` ở `06` ADR-33). Giám sát cả key của chính mình lẫn key của bên kia (4 monitor tổng cộng — 2 process × 2 key) — chấp nhận trùng lặp phát hiện (cả 2 bên có thể cùng thấy 1 thay đổi gần như đồng thời), có chủ đích: nếu 1 bên bị kill ngay trước khi kịp phản ứng, bên còn lại vẫn phát hiện được.

### 4.2 Phản ứng khi phát hiện `Start` value đổi khỏi giá trị mong đợi

- Giá trị mong đợi: `Start = 0x00000002` (`SERVICE_AUTO_START`) cho cả 2 key. Dấu hiệu tấn công điển hình: đổi thành `0x00000004` (`SERVICE_DISABLED`) hoặc `0x00000003` (`SERVICE_DEMAND_START`) để chặn tự khởi động ở lần boot kế tiếp.
- Khi phát hiện lệch giá trị: **tự phục hồi ngay lập tức** (`RegSetValueEx` ghi lại `0x00000002`) — đúng mẫu hình self-heal đã có ở `06` ADR-37, áp dụng lần đầu tiên cho **registry value** thay vì ACL.
- Ghi sự kiện `TamperDetected{key, old_value, new_value, detected_by: "Service"|"Watchdog"}` (event mới, mục 8.2) — bên "Service" ghi trực tiếp; bên "Watchdog" gửi `WatchdogReportEvent` như mục 3.6.
- Tính vào bộ đếm `ANTI-060` (mục 6) — cùng họ hành vi "cố gắng vô hiệu hoá giám sát" như kill-restart process.
- **Không** cố phát hiện "ai" đã đổi giá trị (không cần audit truy vết danh tính user, ngoài phạm vi `MISC-010` hiện tại) — chỉ cần phát hiện + phục hồi + ghi log đủ dùng theo đúng tinh thần tối giản đã áp dụng xuyên suốt dự án.

## 5. Custom Uninstaller (`ANTI-020`)

### 5.1 Kiến trúc tổng thể (ADR-91)

**Quyết định: `ParentalGuard.Uninstaller.exe` — 1 executable RIÊNG BIỆT**, không tái dùng `ParentalGuard.UI.exe` (Dashboard) cho luồng gỡ cài đặt. Lý do: xoá file trong `%ProgramFiles%` đòi hỏi quyền Administrator (`06` mục 4.1 — chỉ Administrators có Modify) — nếu `UI.exe` mang manifest `requireAdministrator` để phục vụ riêng luồng uninstall, MỌI lần mở Dashboard bình thường (không liên quan gỡ cài đặt) cũng sẽ bị UAC prompt làm phiền phụ huynh — vi phạm nguyên tắc UX không cần thiết. Windows manifest `requestedExecutionLevel` là tĩnh theo từng exe, không thể "chỉ elevate khi cần" trong cùng 1 file — buộc phải tách exe.

- `ParentalGuard.Uninstaller.exe` mang manifest `<requestedExecutionLevel level="requireAdministrator" uiAccess="false"/>` — Windows tự động hiện UAC prompt **trước khi** bất kỳ dòng code nào của app chạy, mỗi lần bị gọi (kể cả gọi từ Control Panel/Settings → Apps, đúng attack vector A2). Đây là **lớp gate đầu tiên, thuộc OS, ngoài tầm kiểm soát của app**: nếu tài khoản trẻ là Standard User không biết mật khẩu Administrator (khuyến nghị `SEC-006`), UAC đã chặn hoàn toàn tại đây, `Uninstaller.exe` không có cơ hội chạy dòng code nào.
- **Lớp gate thứ 2 (do app kiểm soát, đúng yêu cầu tường minh `ANTI-020`)**: sau khi elevate xong (có thể vẫn là phụ huynh dùng chung 1 tài khoản Administrator với việc bị giám sát, hoặc trẻ biết mật khẩu Windows Admin — `A6`, ngoài phạm vi bảo vệ nhưng app-level password vẫn là rào cản bổ sung), `Uninstaller.exe` hiện modal yêu cầu **mật khẩu ParentalGuard** (không phải mật khẩu Windows) trước khi cho phép tiếp tục — đây chính là lớp `ANTI-020` bảo vệ.
- Đăng ký trong registry: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard` với `UninstallString = "<installdir>\ParentalGuard.Uninstaller.exe"`, `DisplayName`, `Publisher`, `NoModify=1`, `NoRepair=1`. **Cố tình KHÔNG đăng ký `QuietUninstallString`** (ADR, không phải câu hỏi mở) — nếu có, 1 số luồng uninstall tự động/silent (vd MDM, script) có thể bỏ qua UI và gọi thẳng cờ silent, phá vỡ chính lớp gate `ANTI-020` đang cố xây; việc thiếu `QuietUninstallString` khiến các luồng đó thất bại đóng (fail-closed) thay vì âm thầm cho qua — đúng nguyên tắc fail-secure.

### 5.2 Pipe mới `ParentalGuard.Svc.Uninstaller` (ADR-92)

- **Server**: `Service`. **Client**: `Uninstaller.exe` (chỉ chạy khi được invoke, ephemeral — giống `UI`, không nằm trong ngân sách phục hồi crash 3 giây của `03` mục 4.1).
- **ACL**: giống hệt pipe `UI` (`03` mục 2.2) — Allow `NT AUTHORITY\INTERACTIVE` + `SYSTEM` (Uninstaller chạy elevated nhưng vẫn trong cùng session tương tác, UAC không đổi session — chỉ đổi token).
- **Giới hạn 1 kết nối đồng thời** (đúng mẫu hình `UI`, `03` mục 6).
- **Message được whitelist trên pipe này** (mục 8.4): `Hello`/`HelloAck` (dùng chung), `AuthVerifyRequest`/`AuthVerifyResponse` (field 84/85, **tái sử dụng nguyên vẹn từ `08`, không định nghĩa lại** — đúng đúng thiết kế "cổng chung" đã có sẵn ở `08` mục 7.2), `UninstallExecuteRequest`/`UninstallExecuteResponse` (field mới, mục 8.3). **Không** whitelist bất kỳ message Password/Auth nào khác (`SetInitialPasswordRequest`, `ChangePasswordRequest`...) — `Uninstaller.exe` không có lý do nghiệp vụ nào dùng tới, đúng nguyên tắc least-privilege ở tầng IPC (`03` mục 2.1).
- Pipe chỉ chấp nhận `Hello.process_type == UNINSTALLER` (enum mới, mục 8.4).

### 5.3 Luồng end-to-end

```
[Trẻ/ai đó bấm "Uninstall" ở Control Panel/Settings → Apps]
        │
        ▼
[Windows UAC — lớp gate #1, ngoài tầm app]
   Standard User không có mật khẩu Admin → CHẶN TẠI ĐÂY, Uninstaller.exe
   không bao giờ chạy. Có mật khẩu Admin (A6, ngoài phạm vi bảo vệ) → tiếp tục.
        │
        ▼
[ParentalGuard.Uninstaller.exe khởi động, đã elevated]
   Connect pipe ParentalGuard.Svc.Uninstaller (retry giống 03 mục 4.1,
   timeout hiển thị lỗi sau 10s nếu Service không phản hồi — KHÔNG có
   đường tắt nào bỏ qua Service nếu không kết nối được, xem ADR-98)
   Hello{process_type=UNINSTALLER} ──────────────────────────────▶ Service
                                    ◀────────────────────────────  HelloAck
        │
        ▼
[Hiện modal mật khẩu ParentalGuard — lớp gate #2, do app kiểm soát]
   AuthVerifyRequest{password, action_context="uninstall"} ──────▶ Service
     (Service: kiểm tra lockout, verify Argon2id — ĐÚNG luồng đã
      có sẵn ở 08 mục 7.2, rate-limit dùng chung bộ đếm mật khẩu)
                                    ◀──── AuthVerifyResponse{result, action_token?}
        │
        ├─ WRONG_PASSWORD/LOCKED_OUT → hiện lỗi, cho thử lại (tôn trọng
        │  lockout_until_unix_ms) hoặc user bấm Huỷ → Uninstaller thoát,
        │  KHÔNG xoá gì cả (AuthAttempt đã ghi audit.log qua luồng có sẵn,
        │  đúng "Sai mật khẩu hoặc huỷ → không gỡ gì cả, ghi log" — ANTI-020)
        │
        ▼ SUCCESS
[Màn hình xác nhận cuối: checkbox "Giữ lại nhật ký hoạt động cuối
 cùng" (mặc định KHÔNG tick), nút "Gỡ cài đặt" / "Huỷ"]
        │
        ▼ user bấm "Gỡ cài đặt" (trong vòng 15s kể từ action_token, PWD-020)
   UninstallExecuteRequest{action_token, keep_audit_log} ────────▶ Service
     (Service verify token: còn hạn + action_context="uninstall" +
      chưa dùng — dùng 1 lần, xoá khỏi PendingActionTokens ngay,
      đúng nguyên tắc 08 mục 7.2)
        │
        ▼ token hợp lệ — Service THỰC THI (mục 5.5), sau đó:
                                    ◀──── UninstallExecuteResponse{result=SUCCESS,
                                            audit_log_copy_path?}
        │  (Service tự thoát NGAY SAU KHI response đã ghi xong vào pipe —
        │   mục 5.5 bước 9)
        ▼
[Uninstaller: chờ ParentalGuard.Service.exe thoát hẳn (poll, timeout 30s)]
        │
        ▼
[Uninstaller (elevated Administrator) dọn %ProgramFiles% + tự xoá — mục 5.5
 bước 10-13]
        │
        ▼
[Hiện màn hình hoàn tất: "Đã gỡ cài đặt ParentalGuard. Khởi động lại máy để
 dọn dẹp hoàn toàn." + đường dẫn audit_log_copy_path nếu có]
```

**Bất biến an toàn cốt lõi (ADR-98, KHÔNG được vi phạm khi implement)**: `Uninstaller.exe` **không bao giờ** tự xoá bất kỳ tài nguyên nào (`%ProgramFiles%`, registry, service) nếu chưa nhận được `UninstallExecuteResponse{result=SUCCESS}` từ `Service` — kể cả khi `Service` không phản hồi/pipe không kết nối được/timeout. Không có "đường tắt cục bộ" nào bỏ qua bước xác thực qua `Service`. Đây là hệ quả trực tiếp của việc `Service` (không phải `Uninstaller`) là nơi duy nhất verify mật khẩu (`08` mục 3 — "`Service` là nơi DUY NHẤT tính toán/so sánh Argon2id") — nếu `Uninstaller` tự cho phép xoá khi không liên lạc được `Service`, đó chính là lỗ hổng bypass hoàn chỉnh (kill `Service` trước, rồi chạy uninstaller sẽ "tự động" cho qua).

### 5.4 IPC schema mới — xem mục 8.3 (đặt tập trung cùng các message mới khác để tránh trùng lặp).

### 5.5 Thứ tự dọn dẹp tài nguyên khi `Service` xử lý `UninstallExecuteRequest` (ADR-96)

Nguyên tắc phân công: **những gì cần quyền SYSTEM (`%ProgramData%`, registry Service key) do `Service` tự làm** (đã có sẵn quyền, không cần thêm cơ chế); **những gì `Administrators` đủ quyền (`%ProgramFiles%`) để `Uninstaller.exe` làm sau khi `Service` đã thoát** (tránh 2 tiến trình cùng tranh chấp xoá file `Service.exe` đang bị khoá bởi chính process đang chạy nó).

```
Phía Service (SYSTEM), sau khi verify action_token hợp lệ:
 1. audit.log: "UninstallInitiated"{keep_audit_log}  (ghi TRƯỚC bất kỳ bước phá
    huỷ nào — nếu crash giữa chừng, ít nhất biết đã có nỗ lực uninstall)
 2. NẾU keep_audit_log=true: resolve Desktop path của user đang ở session
    tương tác hiện tại (WTSQueryUserToken + SHGetKnownFolderPath(FOLDERID_Desktop,
    userToken) — đúng kỹ thuật "mượn token user" đã dùng cho CreateProcessAsUser,
    06 mục 2.1) → copy audit.log (plaintext, không cần giải mã, SEC-041) sang
    "<Desktop>\ParentalGuard_AuditLog_<yyyyMMdd_HHmmss>.jsonl".
    NẾU copy thất bại (không xác định được session, hoặc lỗi I/O): KHÔNG xoá
    audit.log gốc ở bước 6 (an toàn hơn mất dữ liệu phụ huynh muốn giữ), ghi
    detail={copy_failed:true} vào response.
 3. Gỡ WFP filter (đảo ngược 06 mục 3.1): FwpmFilterDeleteByKey0 ×2 (outbound +
    inbound), FwpmSubLayerDeleteByKey0, FwpmProviderDeleteByKey0 — best-effort,
    lỗi "not found" bỏ qua (idempotent).
 4. Dừng Watchdog: ServiceController("ParentalGuardWatchdog").Stop(), chờ tối
    đa 10 giây (dài hơn ngưỡng mục 3.4 vì đây là dừng có chủ đích, không phải
    phát hiện treo); quá hạn → force-kill process (Process.Kill()).
    QUAN TRỌNG: bước này PHẢI hoàn tất (Watchdog đã Stopped hẳn) TRƯỚC bước 9
    — nếu Watchdog vẫn còn sống khi Service tự thoát ở bước 9, Watchdog sẽ hiểu
    nhầm là Service bị tấn công và cố khởi động lại nó giữa/sau khi các bước
    xoá dữ liệu đã chạy → phá hỏng toàn bộ trình tự uninstall (ADR-95 — giải
    quyết bằng ĐÚNG THỨ TỰ, không cần thêm cờ "reason=uninstall" đặc biệt nào).
 5. DeleteService() (P/Invoke advapi32, hoặc `sc.exe delete ParentalGuardWatchdog`)
    — đánh dấu xoá registry key Watchdog (hoàn tất khi handle cuối đóng, tức
    ngay lập tức vì Watchdog đã Stopped ở bước 4).
 6. Xoá %ProgramData%\ParentalGuard\*: config.db(+wal/shm), auth.dat, và
    audit.log (TRỪ khi bước 2 thất bại — xem ghi chú ở đó). icon_positions
    nằm trong config.db, xoá theo, không cần bước riêng.
 7. DeleteService() cho chính "ParentalGuardService" (an toàn gọi khi đang
    chạy — SCM hoàn tất xoá thật sự sau khi process thoát hẳn ở bước 9).
 8. Build UninstallExecuteResponse{result=SUCCESS, audit_log_copy_path}, AWAIT
    ghi xong hoàn toàn vào NamedPipeServerStream (đúng nguyên tắc "đã gửi" ở
    08 mục 5.5 điểm 2 — áp dụng lại ở đây cho response cuối cùng của kênh này).
 9. Service tự kích hoạt trình tự graceful-stop của chính nó (giống hệt khi
    nhận SCM stop — 02-process-architecture.md mục 2.1: gửi GracefulStopCommand
    cho Vision/Overlay, chờ timeout ngắn, rồi thoát process). KHÔNG gọi qua
    SCM Stop (Uninstaller không có vai trò gì ở bước này) — Service tự exit().

Phía Uninstaller (Administrator), sau khi nhận result=SUCCESS:
10. Poll đến khi ParentalGuard.Service.exe thoát hẳn (Process.GetProcessesByName
    rỗng, hoặc ServiceController.WaitForStatus(Stopped)), timeout 30 giây — quá
    hạn: hiện cảnh báo "gỡ cài đặt có thể chưa hoàn tất, vui lòng khởi động lại
    máy và thử lại" (không phải lỗi bảo mật, chỉ là timeout kỹ thuật hiếm gặp).
11. Xoá %ProgramFiles%\ParentalGuard\*.exe (Service/Vision/Overlay/UI/Watchdog
    — TRỪ chính Uninstaller.exe), \models\*.onnx.
12. Xoá registry key HKLM\SOFTWARE\...\Uninstall\ParentalGuard (đăng ký uninstall
    của chính app — ACL mặc định Windows đã cho Administrators quyền ghi vùng
    này, không cần chỉnh ACL riêng như 06 mục 4.3).
13. Self-delete: MoveFileEx(ParentalGuard.Uninstaller.exe, NULL,
    MOVEFILE_DELAY_UNTIL_REBOOT) + MoveFileEx(%ProgramFiles%\ParentalGuard\,
    NULL, MOVEFILE_DELAY_UNTIL_REBOOT) — kỹ thuật self-delete kinh điển của
    installer Windows (không thể xoá file thực thi đang chạy chính nó ngay
    lập tức). Hiển thị rõ cho user: các file còn lại (chính Uninstaller.exe,
    thư mục rỗng) sẽ biến mất hoàn toàn sau khi khởi động lại máy.
```

Mỗi bước (3-7, 11-13) tự kiểm tra tồn tại trước khi thao tác (idempotent theo từng bước riêng lẻ) — nếu 1 bước lỗi giữa chừng (vd 1 file bị khoá bởi tiến trình khác ngoài ý muốn), tiếp tục các bước còn lại thay vì dừng hẳn (best-effort, `PARTIAL_FAILURE` — mục 8.3), không rollback (rollback 1 quy trình xoá dữ liệu là vô nghĩa và rủi ro hơn tiếp tục).

### 5.6 Sai mật khẩu / huỷ giữa chừng

Không cần thiết kế thêm — đã hoàn toàn phủ bởi luồng `AuthVerifyRequest` có sẵn (`08` mục 7.2/7.7): sai mật khẩu ghi `AuthAttempt{result=wrong_password, action_context="uninstall"}`, tôn trọng đúng bảng rate-limit chung (`08` mục 7.7). Huỷ ở bất kỳ bước nào trước khi `UninstallExecuteRequest` được gửi → không có tác dụng phụ nào (chưa từng verify token hoặc token chưa từng dùng, tự hết hạn sau 15 giây theo đúng cơ chế `action_token` đã có).

## 6. Rate-limit & cảnh báo tấn công liên tục (`ANTI-060`, `ANTI-061`)

### 6.1 Thiết kế bộ đếm (lần đầu tiên cụ thể hoá — chưa có ở bất kỳ file nào trước đây)

`06-security-architecture.md` ADR-34 đã nhắc tới "bộ đếm rate-limit của `ANTI-060`" khi xử lý `VisionNetworkBlocked`, nhưng chưa file nào định nghĩa nó thực sự sống ở đâu/hoạt động ra sao — file này là nơi thiết kế lần đầu (ADR-99).

**Vấn đề cốt lõi**: nếu bộ đếm chỉ sống trong RAM của `Service`, và 1 trong các sự kiện cần đếm chính là "`Service` bị kill-restart", thì mỗi lần `Service` khởi động lại, bộ đếm tự về 0 — vô hiệu hoá khả năng phát hiện đúng kịch bản nghiêm trọng nhất (kẻ tấn công lặp lại kill `Service`). Giải pháp: **tách 2 bộ đếm độc lập theo bên nào "sống sót" xuyên suốt chuỗi sự kiện cần đếm**:

| Bộ đếm | Sống ở đâu | Đếm sự kiện gì | Tồn tại qua |
|---|---|---|---|
| `Service`-side | RAM `Service` (domain-state mới `AttackPatternState`, cùng mẫu hình `AuthState`/`PauseState` — `02` mục 5.3) | `ProcessRestarted{Vision}`, `ProcessRestarted{Overlay}`, `VisionNetworkBlocked` (đã nối sẵn ở `06` ADR-34), `TamperDetected` (registry, khi chính `Service` là bên phát hiện, mục 4.2) | 1 vòng đời `Service` (chấp nhận reset nếu `Service` tự restart — mất vài phút lịch sử của NHÓM sự kiện này là rủi ro dư chấp nhận được, vì đây không phải sự kiện "Service bị giết", xem cột kế bên) |
| `Watchdog`-side | RAM `Watchdog` | Số lần `Watchdog` phải khởi động lại `Service` (mục 3.4) | 1 vòng đời `Watchdog` — **sống sót xuyên suốt nhiều lần `Service` bị kill-restart liên tiếp**, đúng mục tiêu cốt lõi của `ANTI-060` |

Cả 2 bộ đếm dùng **chung 1 công thức cửa sổ trượt** (sliding window: giữ danh sách timestamp các sự kiện trong T phút gần nhất, N = số phần tử) và **chung 1 cặp giá trị N=5 lần / T=30 phút** (**ĐÃ CHỐT bởi chủ dự án 2026-09-20** — áp dụng đồng nhất cho cả `Service`-side lẫn `Watchdog`-side, không tách riêng; lỏng hơn ví dụ minh hoạ gốc "5 lần/10 phút" ở `Specification/05-anti-uninstall-tamper-spec.md` mục 7 — ví dụ đó chỉ mang tính minh hoạ, chưa từng `ĐÃ CHỐT`; chủ dự án chọn ưu tiên giảm false positive trên máy yếu/cũ, nơi `Vision`/`Overlay` có thể tự crash lặp lại vì lý do khác ngoài tấn công, hơn là tốc độ phát hiện tức thời) — độc lập kích hoạt cảnh báo (bên nào vượt ngưỡng trước, bên đó kích hoạt, không cần đồng bộ 2 bộ đếm với nhau).

- **Không persist xuống `config.db`/`auth.dat`** (khác `rate_limit` mật khẩu ở `08` ADR-26) — có chủ đích: persist đòi hỏi cả `Service` lẫn `Watchdog` cùng đọc/ghi 1 nguồn chung, nhưng `Watchdog` bị cấm đụng `config.db` (mục 3.1); tách 2 bộ đếm độc lập theo "bên nào sống sót" đã giải quyết đúng vấn đề gốc (đếm xuyên suốt việc `Service` bị restart) mà không cần thêm I/O đĩa cho 1 tính năng chỉ kích hoạt khi có tấn công thật (không phải hot-path).
- **Rủi ro dư chấp nhận** (ghi nhận minh bạch, đúng tinh thần đã áp dụng xuyên suốt dự án): nếu chính `Watchdog` cũng bị restart (dù hiếm, chỉ khi `Service` khôi phục `Watchdog`), bộ đếm `Watchdog`-side cũng mất — kịch bản "cả 2 cùng bị tấn công luân phiên" vẫn có 1 khe hở lý thuyết. Không thiết kế thêm cơ chế persist chỉ cho khe hở hiếm/khó khai thác này (đòi hỏi cả 2 bên cùng bị kill chính xác luân phiên đúng lúc, ồn ào/dễ bị chú ý nếu xảy ra thật) — chấp nhận đánh đổi đơn giản hoá, nhất quán mức độ rủi ro dư đã chấp nhận ở nơi khác (vd `08` mục 7.7 "đổi giờ + reboot đồng thời").

### 6.2 Hành động khi vượt ngưỡng (`ANTI-061`, "banner on-screen, KHÔNG Toast chủ động")

**Quyết định (ADR-100): tái dùng nguyên `MonitoringStatusUpdate.state = ERROR` đã có sẵn từ Đợt 2** (`03-ipc-communication.md` mục 3.3, `IconState.ERROR` — mô tả gốc ở `Specification/03-frontend-ui-spec.md` `FE-021`: *"Lỗi-gián đoạn (đỏ, hiếm khi xảy ra, watchdog nên khắc phục nhanh)"*) — **không tạo message/schema IPC mới nào** cho riêng "banner" này. Lý do khớp tự nhiên: bản chất kỹ thuật của việc vượt ngưỡng `ANTI-060` chính là chuỗi sự kiện "process bị gián đoạn lặp lại" — đúng ngữ nghĩa gốc của trạng thái `ERROR` đã định nghĩa, chỉ khác ở **thời lượng hiển thị**: thay vì chỉ đỏ thoáng qua trong lúc đang respawn (vài giây), khi vượt ngưỡng `ANTI-060`, `Service` **giữ nguyên `state=ERROR`** liên tục cho tới khi hết "cửa sổ nghi vấn" (không có sự kiện mới nào trong cùng 1 khoảng T tính từ sự kiện gần nhất) — bản thân icon đổi màu đỏ VÀ Ở LẠI màu đỏ chính là "thông báo rõ ràng trên màn hình, không ẩn giấu" theo đúng chữ nghĩa `ANTI-060`, đồng thời rõ ràng khác biệt với `ShowToastCommand` (popup chủ động, dùng riêng cho `ANTI-070b`) — khớp đúng phân biệt đã có sẵn ở `06` ADR-34 ("banner on-screen, KHÔNG Toast chủ động").

- `Service` ghi `AttackPatternDetected{trigger, count, window_started_at_unix_ms}` vào `audit.log` (event đã có sẵn ở `04` mục 5.1, mở rộng `detail` để bao quát cả nhóm sự kiện mới của file này, không chỉ Vision/Overlay như trước — mục 8.2).
- Không thêm state/màu mới vào `IconState` (giữ đúng 3 giá trị đã `Approved` ở `FE-021`) — tránh sửa `Specification/03-frontend-ui-spec.md` cho 1 quyết định thuần HOW (chọn kênh hiển thị nào cho 1 yêu cầu đã `ĐÃ CHỐT`).
- Tooltip hover (`FE-022`, "chỉ trạng thái chung, không chi tiết") giữ nguyên nội dung tĩnh đã có cho `ERROR` — không đổi để lộ thêm thông tin (đúng ràng buộc `FE-022`).
- `07-overlay-architecture.md` (còn `Draft`) cần amendment nhỏ mở rộng phần mô tả `IconState.ERROR` để bao quát rõ 2 nguyên nhân (respawn thoáng qua VÀ attack-pattern kéo dài) — xem mục 8.5.

## 7. Mở rộng Fail-secure cho domain anti-tamper (`ANTI-070`)

`ANTI-070`/`070a`/`070b` đã thiết kế đầy đủ ở `04-data-architecture.md` mục 6 cho đúng phạm vi **`config.db`** (mất khả năng đọc cấu hình giám sát). Câu hỏi đặt ra cho Đợt 4: có cần 1 trạng thái fail-secure MỚI khi chính `Watchdog` (không phải `config.db`) gặp sự cố liên tục không?

**Kết luận: KHÔNG cần trạng thái fail-secure mới trong state machine trung tâm** (`02-process-architecture.md` mục 3 giữ nguyên, không sửa) — vì:

- **`Watchdog` là lớp phòng thủ THỨ CẤP** (dự phòng cho trường hợp `Service` bị kill) — `Watchdog` biến mất/bị vô hiệu hoá hoàn toàn **không** làm gián đoạn chức năng giám sát chính (`Vision`/`Overlay` vẫn do `Service` quản lý độc lập, không phụ thuộc `Watchdog` để hoạt động). Hệ quả duy nhất: quay lại single point of failure cho riêng kịch bản "chính `Service` bị kill" (đúng rủi ro mà Dual Watchdog được thiết kế để giảm, không phải loại bỏ tuyệt đối 100%) — đây là **suy giảm khả năng phục hồi (resilience)**, không phải **dừng giám sát** — không kích hoạt fail-secure theo đúng định nghĩa ở `Architecture/01-tong-quan-kien-truc.md` mục 5 (fail-secure áp dụng khi có nguy cơ **dừng giám sát**).
- **Chiều ngược lại** (`Service` bị kill, chỉ còn `Watchdog` sống) **có** 1 khoảng trống bảo vệ thật sự: trong khoảng thời gian từ lúc `Service` (và do đó `Vision`/`Overlay` — con của nó) chết tới lúc `Watchdog` khôi phục xong (mục 3.4, mục tiêu ~9 giây phát hiện + thời gian SCM start + `Vision`/`Overlay` respawn theo ngân sách `≤3s` đã có — tổng cộng có thể tới hơn 10 giây), **không có giám sát/chặn nội dung nào đang hoạt động**. Đây là **rủi ro dư ghi nhận minh bạch** (cùng tinh thần các rủi ro dư khác đã chấp nhận xuyên suốt dự án), không phải gap cần vá bằng kiến trúc phức tạp hơn — bản chất tương đương (cùng độ lớn thời gian) với khoảng trống đã chấp nhận từ Đợt 0 cho crash-restart `Vision`/`Overlay` (`BE-023`, ngân sách `≤3s`), chỉ khác đây là kịch bản lồng 2 tầng (khôi phục `Service` RỒI mới tới khôi phục `Vision`/`Overlay`) nên tổng thời gian dài hơn. Không có Requirement ID nào yêu cầu 0% khoảng trống tuyệt đối cho kịch bản kill toàn bộ `Service` — spec đã chấp nhận watchdog "phát hiện và khôi phục" (`ANTI-010`) chứ không cam kết "không có khoảng trống nào".
- `Degraded·FailSecure` (state machine `02` mục 3) tiếp tục **chỉ** mang ý nghĩa "đang dùng cấu hình mặc định hard-code do `config.db` không đọc được" — không mở rộng ý nghĩa sang "Watchdog có vấn đề", giữ đúng 1 khái niệm 1 tên gọi (tránh caáu trúc state bị pha trộn 2 domain không liên quan).

## 8. Amendment tổng hợp — thực hiện cùng lượt ở các file khác

### 8.1 `02-process-architecture.md`

- Mục 4 (bảng heartbeat): điền dòng `Watchdog ↔ Service` (đang ghi "để ở `Architecture/09-anti-tamper-architecture.md`") = 3 giây / mất 3 lần / hành động ở mục 3.4 file này.
- Mục 8 (câu hỏi mở): xoá dòng "Giao thức `Watchdog` ↔ `Service`..." — đã đóng ở mục 3.2 file này.
- Bump PATCH.

### 8.2 `04-data-architecture.md` — mục 5.1, thêm 6 dòng `event_type`

| `event_type` | Khi nào ghi | Nguồn |
|---|---|---|
| `TamperDetected` | Phát hiện + tự phục hồi thay đổi bất thường registry `Start` value (mục 4.2) | `ANTI-031` |
| `UninstallInitiated` | Bắt đầu thực thi gỡ cài đặt sau khi `action_token` hợp lệ, trước bất kỳ bước phá huỷ nào (mục 5.5 bước 1) | `ANTI-020` |
| `UninstallPartialFailure` | 1+ bước dọn dẹp thất bại (best-effort, không rollback) | `ANTI-020` |
| `WFPFiltersRemoved` | Gỡ thành công Provider/Sublayer/Filter WFP lúc uninstall (mục 5.5 bước 3) | `ANTI-020`, liên hệ `06` mục 3 |

Đồng thời mở rộng ghi chú (không đổi field) cho 2 dòng đã có: `ProcessRestarted` nay bao gồm `process ∈ {"Vision","Overlay","Service","Watchdog"}` (trước chỉ Vision/Overlay); `AttackPatternDetected` nay bao gồm `trigger ∈ {process_restart_loop, registry_tamper_loop}` ngoài kill-restart Vision/Overlay gốc — thiết kế đầy đủ ở mục 3.6/4.2/6 file này. Bump MINOR (thêm dòng bảng + mở rộng phạm vi ngữ nghĩa 2 event đã có, không đổi cấu trúc).

### 8.3 `03-ipc-communication.md` — schema mới

```protobuf
enum ProcessType {
  // ... giữ nguyên 0-5 đã có ...
  UNINSTALLER = 6;  // mới — ParentalGuard.Uninstaller.exe (09-anti-tamper-architecture.md mục 5)
}
```

Xoá ghi chú "reserved — chi tiết giao thức ở Architecture/07, không dùng ở file này" trên case `WATCHDOG = 4` (nay đã dùng thật, mục 3.2 file `09`).

```protobuf
// --- Kênh Watchdog (100-119), Đợt 4 — 09-anti-tamper-architecture.md mục 3 ---
message WatchdogReportEvent {                                  // field 100
  WatchdogEventType event_type          = 1;
  string             target_process      = 2; // "Service" | "Watchdog" — bên vừa được xử lý
  int64              detected_at_unix_ms = 3;
  string             action_taken        = 4; // vd "scm_start", "recreated_registration_then_start" — chỉ để audit log

  reserved 10 to 19; // để ngỏ nếu cần thêm chi tiết chẩn đoán sau này
}

message WatchdogReportEventAck {                                // field 101
  bool received = 1;
}

enum WatchdogEventType {
  WATCHDOG_EVENT_TYPE_UNSPECIFIED       = 0;
  PEER_MISSED_HEARTBEAT_RESTARTED       = 1; // mục 3.4
  PEER_REGISTRATION_MISSING_RECREATED   = 2; // mục 3.4 bước 4
  PEER_REGISTRY_TAMPER_DETECTED_RESTORED = 3; // mục 4.2, khi Watchdog là bên phát hiện
  PEER_RESTART_THRESHOLD_EXCEEDED       = 4; // mục 6.1 — Watchdog-side counter vượt ngưỡng N/T
}

// --- Kênh Uninstaller (120-139), Đợt 4 — 09-anti-tamper-architecture.md mục 5 ---
// Lưu ý: pipe Uninstaller CÒN dùng lại AuthVerifyRequest/AuthVerifyResponse (field 84/85,
// khối UI 80-99, định nghĩa gốc ở 08-password-authentication-architecture.md mục 7) qua
// whitelist message theo pipe (mục 3.1 file này) — KHÔNG định nghĩa lại ở đây.
message UninstallExecuteRequest {                               // field 120
  bytes action_token   = 1;
  bool  keep_audit_log = 2;
}

message UninstallExecuteResponse {                              // field 121
  UninstallResult result               = 1;
  string          audit_log_copy_path  = 2; // chỉ set nếu keep_audit_log=true và copy thành công
}

enum UninstallResult {
  UNINSTALL_RESULT_UNSPECIFIED = 0;
  SUCCESS         = 1;
  INVALID_TOKEN   = 2; // hết hạn / sai action_context / đã dùng / không tồn tại
  PARTIAL_FAILURE = 3; // 1+ bước dọn dẹp lỗi — best-effort, KHÔNG rollback (mục 5.5 file 09)
}
```

**Mục 2.1 (bảng pipe)** — thêm 2 dòng:

| Pipe name | Server | Client | Heartbeat cadence |
|---|---|---|---|
| `ParentalGuard.Svc.Watchdog` | `Service` | `Watchdog` | 3 giây (`09-anti-tamper-architecture.md` mục 3.3) |
| `ParentalGuard.Svc.Uninstaller` | `Service` | `ParentalGuard.Uninstaller.exe` | không định kỳ, ephemeral (giống `UI`) |

**Mục 2.2** — thêm 2 đoạn ACL (nội dung đầy đủ ở mục 3.2/5.2 file `09`, tóm tắt: pipe `Watchdog` chỉ Allow SYSTEM; pipe `Uninstaller` giống hệt policy pipe `UI`).

**Mục 4.2 (bổ sung, ADR-94)** — thêm ràng buộc mới sau bước 4 hiện có: *"5. Ngoài xác thực danh tính qua đường dẫn + chữ ký, mỗi pipe còn kiểm tra `Hello.process_type` khớp đúng loại tiến trình dự kiến của pipe đó (`Vision`→`VISION`, `Overlay`→`OVERLAY`, `UI`→`UI`, `Watchdog`→`WATCHDOG`, `Uninstaller`→`UNINSTALLER`) — sai loại thì xử lý giống hệt bước 4 (đóng kết nối, ghi audit log Spoofing, không phản hồi). Bổ sung Đợt 4, áp dụng hồi tố cho cả 3 pipe Đợt 0 (trước đó chỉ kiểm tra đường dẫn/chữ ký, chưa đối chiếu `process_type` khai báo trong `Hello` — khớp chặt hơn, không đổi hành vi các luồng hợp lệ hiện tại vì `Hello.process_type` vốn luôn được set đúng bởi client hợp lệ)."*

**Mục 8** (câu hỏi mở) — xoá dòng "Giao thức `Watchdog ↔ Service`..." (đã đóng).

Bump MINOR.

### 8.4 `06-security-architecture.md`

- Mục 4.1 (bảng ACL `%ProgramFiles%`): đổi "5 executable" → "6 executable (`Service`/`Watchdog`/`Vision`/`Overlay`/`UI`/`Uninstaller`)" — `Uninstaller.exe` áp dụng đúng cùng hàng ACL (`Modify` cho Administrators, `Read+Execute` cho user thường — user thường cần Execute để Windows có thể CHẠY nó khi bấm Uninstall, dù sau đó UAC + password chặn tiếp).
- Mục 4.2 (sửa lỗi tham chiếu còn sót, phát hiện khi viết file này): dòng *"...`Service`/uninstaller chạy dưới ngữ cảnh SYSTEM cho thao tác này, chi tiết ở `07`)"* — số `07` là tham chiếu cũ chưa được cập nhật qua 2 lần renumbering trước (`07`→`08`→`09`, xem `00-INDEX.md` changelog 2026-09-19 hai dòng) — sửa thành `09` (đúng file này).
- Bump PATCH.

### 8.5 `07-overlay-architecture.md` (còn Draft)

- Mở rộng mô tả ngữ nghĩa `MonitoringStatusUpdate.IconState.ERROR` (mục 4.1.2 hoặc tương đương): nay bao gồm cả 2 nguyên nhân — (a) đang respawn `Vision`/`Overlay` sau mất heartbeat (thoáng qua, như bản gốc `FE-021`), và (b) đang trong "cửa sổ nghi vấn tấn công" theo `ANTI-060` (giữ đỏ liên tục cho tới khi hết cửa sổ T phút không có sự kiện mới — mục 6.2 file `09`). `Overlay` không cần phân biệt 2 nguyên nhân này (vẫn đúng nguyên tắc "hàm render thuần", `02` mục 5.1) — chỉ hiển thị đúng state nhận được.
- Bump PATCH (giữ trạng thái Draft, không đổi thành Approved — vẫn chờ review tuần tự theo đúng thứ tự `00-INDEX.md`).

## 9. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-85 | `Service ↔ Watchdog` dùng Named Pipe riêng (`ParentalGuard.Svc.Watchdog`), tái dùng `Hello`/`HeartbeatPing`/`HeartbeatAck` đã có, không dùng thuần SCM status polling | Phát hiện được cả "treo" (không chỉ "chết"); tái dùng hạ tầng framing/HMAC/xác thực danh tính đã có, không phát minh kênh IPC thứ 2 |
| ADR-86 | `Watchdog` cố tình tối giản: không đọc `config.db`/`auth.dat`, không dùng SQLite/DPAPI, không tự ghi `audit.log` — báo cáo sự kiện cho `Service` qua IPC | Giảm bề mặt lỗi cho vai trò "người gác cuối cùng"; giữ nguyên tắc single-writer cho `audit.log` (nhất quán ADR-12 ở `02`) |
| ADR-87 | Heartbeat `Watchdog↔Service`: 3 giây/3 lần miss (9s) | Chậm hơn `Vision`(1s)/`Overlay`(2s) có chủ đích — khôi phục 1 Windows Service nặng hơn respawn child process, tránh false positive lúc `Service` đang bận `Starting` |
| ADR-88 | Khôi phục peer: Stop graceful (5s) → force-kill nếu treo → Start (kèm arg `--restarted-by-watchdog`) → nếu registry mất, `CreateService` qua spawn `sc.exe create` (không P/Invoke trực tiếp) dùng tham số hard-code + đường dẫn resolve runtime | Nhất quán thận trọng dependency/native API đã áp dụng toàn dự án (`06` ADR-30, `08` ADR-71); đường dẫn runtime tránh hard-code (nhất quán WFP App ID, `06` mục 3.1) |
| ADR-89 | Cả `Service`/`Watchdog` tự cấu hình SCM Recovery Options (`sc.exe failure`, reset 1 ngày, restart 60s/120s/300s) idempotent mỗi lần `Starting`, không chờ installer Đợt 9 | `ANTI-011` là lớp bổ sung miễn phí, không phải cơ chế chính (heartbeat mới là chính) — số liệu không nhạy cảm bảo mật, quyết định trực tiếp được; cần sẵn sàng từ Đợt 4 để test, không chờ Đợt 9 |
| ADR-90 | Giám sát registry `Start` value qua `RegNotifyChangeKeyValue` (không polling), cả 2 bên giám sát cả 2 key (4 monitor), tự phục hồi giá trị khi lệch | Nhất quán "dùng OS notification, không polling" (`06` ADR-33); giám sát chéo giữ tính chất "không single point of failure" như chính nguyên tắc Dual Watchdog |
| ADR-91 | `ParentalGuard.Uninstaller.exe` — executable RIÊNG (6th binary), không tái dùng `UI.exe`, mang manifest `requireAdministrator`; không đăng ký `QuietUninstallString` | Manifest elevation là tĩnh theo exe — dùng chung `UI.exe` sẽ ép Dashboard luôn cần UAC, phá UX bình thường; thiếu `QuietUninstallString` ép mọi luồng gỡ về đúng 1 đường có gate mật khẩu, fail-closed cho luồng silent/tự động |
| ADR-92 | Pipe riêng `ParentalGuard.Svc.Uninstaller`, tái dùng `AuthVerifyRequest`/`AuthVerifyResponse` (field 84/85) qua whitelist theo pipe thay vì định nghĩa lại message | Đúng thiết kế "cổng chung" đã dành sẵn ở `08` mục 7.2; tránh trùng lặp schema, giữ least-privilege (Uninstaller không whitelist các message Password/Auth khác không liên quan) |
| ADR-93 | Field block mới trong `oneof`: Watchdog (100-119), Uninstaller (120-139); thêm `ProcessType.UNINSTALLER=6` | Theo đúng quy ước "khối 20 số/domain" đã có (`03` ADR-16), additive không phá vỡ client cũ |
| ADR-94 | Thêm bước xác thực `Hello.process_type` khớp đúng loại tiến trình dự kiến của từng pipe (áp dụng hồi tố cả 3 pipe Đợt 0) | Defense-in-depth bổ sung, phát hiện lúc thiết kế pipe mới cho `Watchdog`/`Uninstaller` — không đổi hành vi luồng hợp lệ hiện tại |
| ADR-95 | Giải quyết nguy cơ Watchdog "hồi sinh nhầm" `Service` giữa lúc uninstall bằng ĐÚNG THỨ TỰ (dừng hẳn Watchdog trước khi Service tự thoát), không cần thêm cờ "reason=uninstall" đặc biệt | Đơn giản hơn — loại bỏ hoàn toàn nguy cơ race condition bằng cách đảm bảo không còn ai giám sát Service tại thời điểm nó tự thoát, không cần logic "suppress restart" phức tạp thêm |
| ADR-96 | Phân công dọn dẹp: `Service` (SYSTEM) xử lý `%ProgramData%`+registry Service keys+WFP; `Uninstaller.exe` (Administrator, sau khi Service đã thoát hẳn) xử lý `%ProgramFiles%`+registry Uninstall key+tự xoá qua `MoveFileEx(...DELAY_UNTIL_REBOOT)` | Mỗi bên chỉ làm phần mình sẵn có đúng quyền, tránh 2 tiến trình tranh chấp file đang bị khoá (`Service.exe` không thể tự xoá chính nó khi đang chạy) |
| ADR-97 | `audit.log` giữ lại (tuỳ chọn, mặc định KHÔNG tick) được copy sang Desktop của user session hiện tại (qua `WTSQueryUserToken`+`SHGetKnownFolderPath`) trước khi xoá `%ProgramData%`, không giữ nguyên tại chỗ | `%ProgramData%\ParentalGuard\` bị xoá hoàn toàn nên không thể "giữ tại chỗ"; Desktop là vị trí chắc chắn phụ huynh (người vừa nhập đúng mật khẩu) tiếp cận được ngay |
| ADR-98 | `Uninstaller.exe` không bao giờ tự xoá tài nguyên nếu chưa nhận `UninstallExecuteResponse{SUCCESS}` từ `Service` — không có đường tắt khi `Service` không phản hồi | Bất biến an toàn cốt lõi: nếu cho phép bypass khi `Service` không liên lạc được, đó là lỗ hổng hoàn chỉnh (kill Service trước rồi uninstall "tự do") |
| ADR-99 | Bộ đếm `ANTI-060` tách 2 phía độc lập: `Service`-side (RAM, reset theo vòng đời `Service`) cho Vision/Overlay/WFP/registry-tamper-do-Service-phát-hiện; `Watchdog`-side (RAM, sống sót qua nhiều lần `Service` restart) riêng cho số lần `Watchdog` phải khôi phục `Service`. Không persist xuống đĩa | Giải đúng vấn đề gốc (đếm xuyên suốt việc `Service` bị kill lặp lại) mà không cần I/O đĩa thêm cho tính năng hiếm khi kích hoạt; `Watchdog` không được đụng `config.db` (ADR-86) nên không thể dùng persist chung |
| ADR-100 | "Banner on-screen" của `ANTI-060`/`ANTI-061` tái dùng nguyên `MonitoringStatusUpdate.IconState.ERROR` đã có (Đợt 2), giữ đỏ liên tục hết cửa sổ T thay vì chỉ thoáng qua — không tạo message/state UI mới | Khớp tự nhiên ngữ nghĩa `FE-021` (đã Approved) sẵn có; phân biệt rõ với `ShowToastCommand` (chủ động, dành riêng `ANTI-070b`) đúng như `06` ADR-34 đã nêu; không cần sửa `Specification/` cho 1 quyết định thuần chọn kênh hiển thị |

## 10. Câu hỏi mở

Không còn câu hỏi mở chính nào chặn code. Ngưỡng `ANTI-060` (N lần / T phút) đã được chủ dự án xác nhận trực tiếp — xem mục 6.1: **N=5 lần / T=30 phút**, ĐÃ CHỐT bởi chủ dự án 2026-09-20, áp dụng cho cả 2 bộ đếm RAM-only (`Service`-side và `Watchdog`-side).

- [ ] (Không blocking) Mặc định checkbox "Giữ lại nhật ký hoạt động cuối cùng" ở màn hình xác nhận uninstall (mục 5.3) hiện quyết định là **KHÔNG tick mặc định** (opt-in) — thuần UX, có thể đổi nếu chủ dự án muốn ngược lại, không phải quyết định bảo mật.

## 11. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.1.1 | 2026-09-20 | **Chủ dự án xác nhận/chốt câu hỏi mở chính duy nhất còn treo (mục 10 v0.1.0)** — ngưỡng `ANTI-060`: **N=5 lần / T=30 phút** (lỏng hơn ví dụ minh hoạ gốc "5 lần/10 phút" ở `Specification/05-anti-uninstall-tamper-spec.md` mục 7, vốn chưa từng `ĐÃ CHỐT`), áp dụng đồng nhất cho cả 2 bộ đếm RAM-only đã thiết kế ở mục 6.1 (`Service`-side: `ProcessRestarted{Vision}`/`ProcessRestarted{Overlay}`/`VisionNetworkBlocked`/`TamperDetected`-do-Service-phát-hiện; `Watchdog`-side: số lần `Watchdog` khôi phục `Service`). Cập nhật mục 6.1 (điền số liệu cụ thể thay chỗ trống) và mục 10 (xoá câu hỏi mở chính, chỉ còn 1 câu hỏi phụ không-blocking về mặc định checkbox giữ audit log). Trạng thái Draft→**Approved**, không kéo theo sửa `Specification/` hay file `Architecture/` khác (thuần chọn số liệu HOW trong khoảng WHAT đã chốt, đúng tinh thần đã áp dụng cho Argon2id/Recovery Key ở `08` mục 9). Theo chỉ đạo chủ dự án — không dừng chờ review, đủ điều kiện giao `feature-dev` code Đợt 4 |
| v0.1.0 | 2026-09-19 | Khởi tạo — Đợt 4 (`ROADMAP.md`). Đóng 2 câu hỏi mở treo từ Đợt 0 (giao thức `Watchdog↔Service`: Named Pipe riêng `ParentalGuard.Svc.Watchdog`, heartbeat 3s/3-miss). Dual Watchdog đầy đủ (`ANTI-010`/`011`): nguyên tắc tối giản `Watchdog` (không đụng `config.db`/`audit.log` trực tiếp), trình tự khôi phục peer (stop→force-kill→start→re-register qua `sc.exe create` nếu registry mất), SCM Recovery Options tự cấu hình idempotent. Giám sát chủ động registry (`ANTI-031`) qua `RegNotifyChangeKeyValue` 2 chiều + self-heal. Custom Uninstaller đầy đủ (`ANTI-020`): 6th executable `ParentalGuard.Uninstaller.exe` (manifest `requireAdministrator`, không `QuietUninstallString`), pipe mới `ParentalGuard.Svc.Uninstaller` tái dùng `AuthVerifyRequest`/`action_token` từ `08`, message mới `UninstallExecuteRequest`/`Response`, luồng end-to-end 13 bước phân công rõ `Service`(SYSTEM)/`Uninstaller`(Administrator), bất biến an toàn "không bao giờ tự xoá nếu Service không phản hồi SUCCESS" (ADR-98), self-delete qua `MoveFileEx DELAY_UNTIL_REBOOT`. Thiết kế lần đầu bộ đếm `ANTI-060` (2 bộ đếm độc lập `Service`-side/`Watchdog`-side, RAM-only, không persist) + cơ chế "banner on-screen" tái dùng `IconState.ERROR` có sẵn (không schema mới, ADR-100). Xác nhận phạm vi `ANTI-070` KHÔNG cần state fail-secure mới cho domain anti-tamper (Watchdog là lớp thứ cấp, mất không dừng giám sát) — chỉ ghi nhận minh bạch 1 rủi ro dư (khoảng trống bảo vệ ngắn khi `Service` bị kill, trước khi `Watchdog` khôi phục xong). 16 ADR (85-100). 1 câu hỏi mở CHÍNH cần chủ dự án quyết định trực tiếp (ngưỡng N/T `ANTI-060`, không tự bịa số liệu bảo mật), 1 câu hỏi phụ không-blocking (mặc định checkbox giữ audit log). Amendment cùng lượt: `02-process-architecture.md` (PATCH, điền heartbeat Watchdog + đóng câu hỏi mở), `03-ipc-communication.md` (MINOR, 2 pipe mới + field block 100-139 + `ProcessType.UNINSTALLER=6` + ràng buộc `Hello.process_type` theo pipe), `04-data-architecture.md` (MINOR, 4 `event_type` mới + mở rộng ngữ nghĩa 2 event đã có), `06-security-architecture.md` (PATCH, 6th executable vào bảng ACL + sửa 1 tham chiếu số file còn sót từ renumbering trước), `07-overlay-architecture.md` (PATCH, mở rộng mô tả `IconState.ERROR`, vẫn giữ Draft). Theo chỉ đạo — không dừng chờ review từng file, dừng lại báo cáo sau khi xong đủ file mới + toàn bộ amendment |

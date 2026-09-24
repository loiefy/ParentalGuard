# 10 — UI Architecture (Dashboard WinUI 3)

> Version: v0.1.0 | Trạng thái: Draft | Cập nhật: 2026-09-20

## 0. Ghi chú tổ chức tài liệu

`ROADMAP.md` mục 3 ghi Đợt 6 phụ thuộc file `08` — nhưng số đó đã bị dồn 2 lần qua các lượt trước (Đợt 2 chèn `07-overlay-architecture.md`, Đợt 3 chèn `08-password-authentication-architecture.md`). Đối chiếu `Architecture/00-INDEX.md` mục 2 (nguồn sự thật hiện hành): file dành cho UI Architecture là **`10-ui-architecture.md`** (không phải `08`/`09`) — đúng số đã xác nhận trước khi viết, không cần dồn số thêm lần nào nữa.

## 1. Mục đích và phạm vi

File này trả lời **HOW** cho toàn bộ `ParentalGuard.UI` (Dashboard WinUI 3, Đợt 6, `ROADMAP.md` mục 4) — lần đầu tiên dự án có UI client thật tương tác với phần lớn logic đã code sẵn ở `Service` (Password/Auth Đợt 3, Pause/Resume Đợt 5): cấu trúc ứng dụng (MVVM, project layout), cơ chế khởi động, tầng client IPC (khác biệt kiến trúc quan trọng so với `Vision`/`Overlay` — xem mục 3), navigation map `S1`-`S9`, thiết kế từng màn hình, đa ngôn ngữ, accessibility, và các trạng thái rỗng/lỗi. Không phát minh yêu cầu sản phẩm mới — mọi quyết định trích dẫn ngược `FE-0xx`/`PWD-0xx`/`PAUSE-0xx`/`MISC-0xx`/`BE-0xx`, hoặc là ADR thuần kỹ thuật (mục 10).

File này **không** thiết kế lại: (a) business logic Password/Auth/Pause — đã đầy đủ ở `08-password-authentication-architecture.md`/`02-process-architecture.md` mục 3a, file này chỉ viết **UI client gọi đúng** các message đã có; (b) Overlay/`S7`/`S8`/`S9` — đã thiết kế đầy đủ ở `07-overlay-architecture.md`, thuộc process `ParentalGuard.Overlay` (WinForms, đã code Đợt 1/2), **hoàn toàn tách biệt** khỏi `ParentalGuard.UI` (xem mục 2.4); (c) Uninstaller UI (`ConfirmUninstallForm`/`PasswordPromptForm`, WinForms) — thuộc `ParentalGuard.Uninstaller.exe`, 6th executable riêng theo `09-anti-tamper-architecture.md` ADR-91, **không tái dùng bất kỳ code UI nào** với `ParentalGuard.UI` (chỉ tái dùng chung **protocol** `AuthVerifyRequest`/`action_token` ở tầng IPC, không tái dùng dialog/form/namespace nào — xác nhận rõ ràng ở mục 2.4, đúng câu hỏi đã đặt ra lúc giao việc).

Amendment kéo theo cùng lượt (đọc kỹ trước khi tiếp tục, vì file này phụ thuộc trực tiếp): `02-process-architecture.md` v0.3.0 (mục 3a.7 mới — cơ chế `PAUSE-021`), `03-ipc-communication.md` v0.8.0 (mục 3.7 mới — 10 message Dashboard/Settings, field `process_name` ở `VisionInferenceResult`, message `OverlayMessageUpdate`), `04-data-architecture.md` v0.3.0 (3 field JSON mới + schema `ContentBlocked.detail`), `05-image-pipeline-architecture.md` v0.2.1 (field `ProcessName`), `07-overlay-architecture.md` v0.2.3 (mục 4.4 — thông điệp overlay tuỳ biến + sửa bug hardcode Đợt 1), `08-password-authentication-architecture.md` v0.3.1 (hằng số `action_context` mới `manage_whitelist`).

## 2. Kiến trúc tổng thể ứng dụng

### 2.1 Tech stack & project layout (ADR-114, ADR-115)

| Thành phần | Lựa chọn | Lý do |
|---|---|---|
| Framework | WinUI 3 (Windows App SDK, bản mới nhất ổn định) | `FE` mục 4, `GEN-003a` |
| Runtime | .NET 10, `net10.0-windows10.0.xxxxx.0` | Khớp `ADR-01` ở `01-tong-quan-kien-truc.md`, nhất quán 5 process còn lại |
| Deployment model | **Unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`, `OutputType=WinExe`) — Windows App SDK self-contained bootstrap (`Microsoft.WindowsAppSDK` NuGet, gọi `Bootstrap.TryInitialize`/dùng `Microsoft.WindowsAppSDK.Runtime` MSIX cài kèm installer ở Đợt 11) | Nhất quán với 5 executable còn lại (đều `net10.0-windows`, unpackaged Win32 `.exe`, `Service`/`Watchdog`/`Vision`/`Overlay`/`Uninstaller` — `06-security-architecture.md` mục 4.2 bảng ACL); MSIX packaging cho **cả app** chưa được quyết định ở bất kỳ đâu trong `Architecture/`, thuộc phạm vi `11-deployment-release-architecture.md` (chưa viết) — không tự phát minh trước ở đây, unpackaged là lựa chọn an toàn không khoá cứng quyết định đó |
| MVVM | `CommunityToolkit.Mvvm` (`ObservableObject`/`RelayCommand`/`ObservableProperty`, MIT) | Thư viện MVVM chuẩn cộng đồng .NET, nhẹ (source generator, không runtime dependency nặng), nhất quán mức độ thận trọng dependency đã áp dụng ở `08` ADR-71 (chọn managed/MIT, tránh native) |
| Namespace gốc | `ParentalGuard.UI` | — |

Cấu trúc thư mục (`src/ParentalGuard.UI/`):

```
ParentalGuard.UI/
├── App.xaml(.cs)              — entry point, khởi tạo UiIpcClient, kiểm tra single-instance (mục 2.3)
├── Views/                     — XAML: MainShellPage, OnboardingPage(s), DashboardPage, AuditLogPage,
│                                 SettingsPage, RecoveryPage, AuthPromptDialog
├── ViewModels/                — 1 ViewModel/screen, kế thừa CommunityToolkit ObservableObject
├── Services/
│   ├── IpcClient/              — KHÔNG chứa logic transport (đã ở ParentalGuard.Ipc.Client, mục 3) —
│   │                             chỉ chứa facade theo domain: AuthFacade, PauseFacade, DashboardFacade,
│   │                             AuditFacade, ConfigFacade (mục 2.2)
│   ├── NavigationService.cs    — điều hướng giữa Views, quản lý hiển thị AuthPromptDialog theo action_context
│   └── LocalizationService.cs  — wrapper mỏng quanh ResourceManager (mục 7)
├── Resources/
│   └── UiStrings.resx (+ .en.resx khi có ngôn ngữ thứ 2, Phase 2)
└── Assets/                     — icon, hình minh hoạ Onboarding
```

### 2.2 Layering: ViewModel → Facade → `UiIpcClient` (ADR-116)

ViewModel **không gọi trực tiếp** `UiIpcClient`/`IpcPayload` (kiểu Protobuf sinh mã không thân thiện với `INotifyPropertyChanged`/binding XAML, và trộn lẫn concern "gọi IPC" vào "logic màn hình" làm ViewModel khó test) — mỗi domain có 1 **Facade** mỏng (`IAuthFacade`, `IPauseFacade`, `IDashboardFacade`, `IAuditFacade`, `IConfigFacade`) nhận/trả về kiểu POCO đơn giản (không phải `IpcPayload` thô), tự xử lý build `IpcPayload`/đọc field response tương ứng. ViewModel inject Facade qua constructor (DI tối giản, không cần framework DI đầy đủ cho app nhỏ — `Microsoft.Extensions.DependencyInjection` container đơn giản khởi tạo 1 lần ở `App.xaml.cs` là đủ).

### 2.3 Cơ chế khởi động (ADR-117)

- **`ParentalGuard.UI` không được `Service` spawn** — đã chốt từ `02-process-architecture.md` mục 2.5 ("App WinUI 3 thông thường, user tự mở (Start Menu/Desktop shortcut) — **không** được `Service` tự động khởi chạy"). File này **xác nhận lại và không thay đổi** quyết định đó sau khi rà soát kỹ `StatusIconForm`/`StatusIconManager` (`src/ParentalGuard.Overlay/Icons/`) theo đúng yêu cầu đối chiếu lúc giao việc: code hiện tại **chỉ xử lý kéo-thả** (`OnMouseDown`/`OnMouseMove`/`OnMouseUp` → `_onPositionCommitted`), **không có bất kỳ handler click nào** để mở Dashboard — khớp đúng `Specification/03-frontend-ui-spec.md` `FE-022` ("không mô tả tương tác click nào khác ngoài hover") đã được `07-overlay-architecture.md` mục 4.1.3 xác nhận tường minh ("không phát minh hành vi click bổ sung vì không có căn cứ trong `Specification/`"). **Không thêm** cơ chế click-to-open ở lượt này (sẽ là thay đổi hành vi `Overlay`, ngoài phạm vi Đợt 6 và ngoài phạm vi yêu cầu — nếu cần, đó là quyết định sản phẩm mới, phải qua `Specification/03-frontend-ui-spec.md` trước).
- Icon Start Menu/Desktop shortcut trỏ thẳng `ParentalGuard.UI.exe` — tạo bởi installer (`11-deployment-release-architecture.md`, chưa viết, không thiết kế ở đây).
- **Single-instance enforcement (ADR-117a)**: pipe `ParentalGuard.Svc.UI` giới hạn 1 kết nối đồng thời (`03-ipc-communication.md` mục 6), nhưng nếu chỉ dựa vào đó, lần mở `ParentalGuard.UI.exe` thứ 2 sẽ hiện lỗi kết nối khó hiểu ("đang có phiên khác") thay vì trải nghiệm chuẩn "đưa cửa sổ đang mở lên trước". `App.xaml.cs` dùng **named `Mutex`** (`Global\ParentalGuard.UI.SingleInstance`) kiểm tra lúc khởi động: nếu đã tồn tại → gửi tín hiệu kích hoạt cửa sổ đang chạy (`Win32 FindWindow` + `SetForegroundWindow` theo class name cố định của `MainWindow`) rồi tự thoát ngay, không thử connect pipe. Đây là UX polish thuần, không phải yêu cầu bảo mật.

### 2.4 Quan hệ với `Overlay`/`Uninstaller` (xác nhận độc lập)

| | `ParentalGuard.UI` | `ParentalGuard.Overlay` | `ParentalGuard.Uninstaller` |
|---|---|---|---|
| Framework UI | WinUI 3 | WinForms | WinForms |
| Ai spawn | User tự mở | `Service` (`CreateProcessAsUser`) | User/Windows Add-Remove Programs invoke |
| Pipe | `ParentalGuard.Svc.UI` | `ParentalGuard.Svc.Overlay` | `ParentalGuard.Svc.Uninstaller` |
| Tái dùng gì với UI | — | **Không tái dùng code UI nào** (khác framework, khác process, khác vòng đời) | **Không tái dùng code UI nào** (`09` ADR-91: 6th executable riêng, có chủ đích không dùng chung `UI.exe` để tránh ép Dashboard luôn cần UAC) |
| Điểm chung duy nhất | — | `IpcFrameTransport`/`IpcEnvelope` (transport framing, `ParentalGuard.Ipc` project) + hợp đồng `AuthVerifyRequest`/`action_token` (protocol, không phải UI code) | Tương tự |

Không có gap nào ở đây — xác nhận tường minh theo đúng yêu cầu đối chiếu lúc giao việc.

## 3. IPC Client layer — khác biệt kiến trúc với `Vision`/`Overlay`

### 3.1 Vì sao không tái dùng `IpcChildClient`/`ChildIpcBootstrap` (ADR-118)

`IpcChildClient` (`src/ParentalGuard.Ipc/Client/IpcChildClient.cs`) dùng chung cho `Vision`/`Overlay` — đã đọc mã nguồn thật để xác nhận: **không tái dùng được nguyên trạng cho `UI`**, vì 2 khác biệt kiến trúc nền tảng:

1. **Bootstrap khoá HMAC**: `ChildIpcBootstrap.ReadFromInheritedStdHandle()` đọc khoá từ handle **kế thừa** qua `CreateProcessAsUser` (`03-ipc-communication.md` mục 5.2, ADR-18) — chỉ tồn tại khi `Service` **thực sự spawn** tiến trình đó. `UI` không có quan hệ cha-con với `Service` (mục 2.3) → không có handle nào để kế thừa → gọi hàm này từ `UI` sẽ luôn ném `InvalidOperationException` ("phải được Service spawn qua ChildProcessLauncher"). Đây chính xác là tình huống `03` mục 5.3/ADR-19 đã lường trước và thiết kế riêng: khoá phiên **ephemeral, thương lượng trong chính phiên kết nối** qua `HelloAck.session_key`.
2. **Khung khoá 2 giai đoạn cho đúng 2 frame đầu**: `IpcChildClient.HandshakeAsync` ký/verify **toàn bộ** kết nối (kể cả `Hello`/`HelloAck`) bằng **1** khoá cố định (`bootstrap.HmacKey`, đã có sẵn từ trước khi connect). Kênh `UI` cần khác: `Hello` đầu tiên + `HelloAck` phản hồi ký bằng khoá hằng số 32-byte-zero biết trước (`03` mục 5.3, ADR-82), **rồi mới chuyển** sang khoá phiên thật nhận được trong chính `HelloAck` đó cho mọi frame sau — 1 kết nối dùng **2 khoá khác nhau ở 2 giai đoạn**, điều `IpcChildClient` không hỗ trợ (nó truyền đúng 1 `bootstrap.HmacKey` cho toàn bộ `RunConnectionAsync`).

### 3.2 `UiIpcClient` — component mới (`ParentalGuard.Ipc.Client` namespace, cùng project `ParentalGuard.Ipc` với `IpcChildClient` để tái dùng `IpcFrameTransport`/`IpcEnvelope` đã có, KHÔNG đặt trong `ParentalGuard.UI`)

```
UiIpcClient
├── ConnectAsync(CancellationToken)         — 1 lần connect (KHÔNG lặp vô hạn như children — user-driven
│                                              foreground action, cần phản hồi nhanh cho UX): thử connect
│                                              timeout 3s, KHÔNG retry nền; caller (Facade) tự quyết định
│                                              hiển thị lỗi + nút "Thử lại" thay vì UiIpcClient tự lặp
├── HandshakeAsync(pipe)                     — gửi Hello ký bằng khoá hằng số 32-byte-zero (03 mục 5.3),
│                                              nhận HelloAck, lưu session_key cho các frame sau
├── SendRequestAsync<TResp>(IpcPayload req)  — ghi request (ký bằng session_key hiện tại) → đọc frame kế
│                                              tiếp khớp correlation_id → trả response. SemaphoreSlim(1)
│                                              bọc quanh toàn bộ hàm này (ADR-119, mục 3.3)
├── DisconnectAsync()                        — đóng pipe chủ động (gọi lúc App thoát)
└── IsConnected (bool)
```

- **Không có Reader loop/Writer loop song song, không `Channel<IpcPayload>`** (khác `IpcChildClient`, ADR-119): kênh `UI` là **request/response thuần, tuần tự** theo đúng thiết kế gốc (`03` mục 2.1: "không có heartbeat định kỳ — request/response theo nhu cầu") — không có message nào `Service` chủ động đẩy xuống `UI` ngoài lúc handshake (mục 4.3 `03`, chỉ áp dụng cho `Vision`/`Overlay`). Vì vậy `SendRequestAsync` đơn giản: ghi 1 frame, đọc 1 frame, không cần vòng lặp nền liên tục — `SemaphoreSlim(1)` đảm bảo 2 lời gọi đồng thời từ 2 Facade khác nhau (ví dụ `DashboardViewModel` đang poll trong khi user bấm nút ở `SettingsPage`) không interleave byte trên cùng 1 `NamedPipeClientStream`.
- **Không retry nền vô hạn**: nếu `SendRequestAsync` ném `IOException` (pipe vỡ giữa chừng), `UiIpcClient` set `IsConnected=false` và ném `UiIpcConnectionException` lên caller — Facade/ViewModel bắt exception này để chuyển UI sang trạng thái lỗi (mục 9), **không** tự动 lặp lại ngầm (khác `Vision`/`Overlay`, nơi mất kết nối vô nghĩa nếu không tự phục hồi — `UI` thì user luôn có thể tự bấm "Thử lại", không cần tự động hoá).
- **Xác thực danh tính/bootstrap khoá phiên đã đủ từ `03` (không thiết kế lại)**: ACL pipe `UI` = `INTERACTIVE`+`SYSTEM` (`03` mục 2.2), `Service` xác thực chữ ký code-signing qua `GetNamedPipeClientProcessId` + đối chiếu đường dẫn cài đặt + Authenticode thumbprint (`03` mục 4.2) **trước khi đọc byte đầu tiên** — `UiIpcClient` không cần tự làm gì thêm ở lớp này, chỉ cần gửi đúng `Hello.process_type=UI` + đúng đường dẫn thực thi (tự động đúng vì `Environment.ProcessPath` trỏ về chính `ParentalGuard.UI.exe` đã cài trong `%ProgramFiles%\ParentalGuard\`).

### 3.3 Polling thay vì push cho trạng thái Dashboard "sống" (ADR-120)

`DashboardViewModel` cần hiển thị trạng thái gần-thời-gian-thực (đang Pause còn lại bao lâu, trạng thái Vision...) nhưng kênh `UI` **không thiết kế cơ chế push** (mục 3.2, đúng nguyên tắc gốc từ Đợt 0). Quyết định: `DashboardViewModel` dùng `DispatcherQueueTimer` (WinUI 3, chạy đúng UI thread) tick **mỗi 5 giây** trong khi `DashboardPage` đang hiển thị (`Start()` ở `OnNavigatedTo`, `Stop()` ở `OnNavigatedFrom` — không poll nền khi user đang ở `S3`/`S4`), gọi `DashboardFacade.GetStatusAsync()` (gộp `DashboardStatusQuery` + `PauseStatusQuery` thành 2 round-trip liên tiếp trên cùng kết nối). 5 giây đủ nhanh cho cảm nhận "sống" của 1 màn hình trạng thái tổng quan (không phải hiển thị đếm-ngược-từng-giây chính xác tuyệt đối như tooltip icon `S8`, vốn đã có cơ chế riêng ở `Overlay`), không tạo tải đáng kể lên `Service` (2 message nhỏ/5s).

## 4. Navigation map

```
App khởi động
  → UiIpcClient.ConnectAsync()
      ├─ FAIL → [Màn hình lỗi kết nối toàn màn hình] (mục 9) + nút "Thử lại"
      └─ OK → AuthStatusQuery
            ├─ password_configured = false → [S1 Onboarding] (toàn màn hình, không có NavigationView)
            │      └─ hoàn tất → [Main Shell, mở S2]
            └─ password_configured = true → [Main Shell, mở S2]

Main Shell (NavigationView, 3 mục): [S2 Dashboard] | [S3 Lịch sử] | [S4 Cài đặt]
  S3: vào tab → luôn hiện [S5 Auth Modal, action_context="view_audit_log"] trước khi render danh sách
      (huỷ → quay lại S2; SUCCESS → hiện danh sách, action_token dùng ngay cho AuditLogQuery đầu tiên)
  S2: nút Pause/Resume → [S5 Auth Modal, action_context="pause_monitoring"]
  S3: nút "Đánh dấu sai" trên 1 dòng ContentBlocked → [S5 Auth Modal, action_context="manage_whitelist"]
  S4: nút xoá 1 whitelist entry → [S5 Auth Modal, action_context="manage_whitelist"]
  S4: form đổi mật khẩu → tự xác thực qua old_password, KHÔNG qua S5 (08 mục 7.4)

[S5 Auth Modal] (ContentDialog, tái dùng 1 component cho mọi action_context)
  → link "Quên mật khẩu?" (luôn hiển thị, không điều kiện) → đóng dialog, mở [S6 Recovery]
[S4 Cài đặt] cũng có link trực tiếp "Quên mật khẩu / dùng Recovery Key" trong card đổi mật khẩu
  (không bắt buộc phải trigger 1 hành động gated trước mới thấy được lối vào S6)

[S6 Recovery] → SUCCESS → hiện Recovery Key mới 1 lần (cùng pattern S1) → quay lại Main Shell

[S7]/[S8]/[S9] — KHÔNG thuộc ParentalGuard.UI, xem mục 1/2.4
```

## 5. Bảng ánh xạ màn hình ↔ message IPC (tổng hợp, chi tiết schema ở `03-ipc-communication.md` mục 3.4/3.6/3.7)

| Màn hình | Message | Gate (`action_token`)? | Nguồn thiết kế nghiệp vụ |
|---|---|---|---|
| S1 Onboarding | `AuthStatusQuery`, `SetInitialPasswordRequest`, `ConfirmRecoveryKeySavedRequest` | Không (chưa có gì để xác thực) | `08` mục 7.1 |
| S2 Dashboard | `DashboardStatusQuery`, `PauseStatusQuery`, `AuditChartQuery` | Không | `03` mục 3.6/3.7 |
| S2 Pause/Resume | `AuthVerifyRequest{pause_monitoring}` → `PauseMonitoringRequest`/`ResumeMonitoringRequest` | **Có** | `02` mục 3a.1/3a.2 |
| S2 banner `PAUSE-021` | `AcknowledgePauseAnomalyRequest` | Không (thuần thông tin, `02` mục 3a.7) | `02` mục 3a.7 |
| S3 Audit log | `AuthVerifyRequest{view_audit_log}` → `AuditLogQuery` | **Có** (mỗi lần vào tab, `PWD-020`) | mục 6.3 file này |
| S3 Đánh dấu sai | `AuthVerifyRequest{manage_whitelist}` → `MarkFalsePositiveRequest` | **Có** | mục 6.3 file này |
| S4 Xem cấu hình | `ConfigQuery` | Không | mục 6.4 file này |
| S4 Đổi thông điệp overlay | `ConfigUpdateRequest` | Không (cosmetic, mục 6.4) | mục 6.4 file này |
| S4 Xoá whitelist entry | `AuthVerifyRequest{manage_whitelist}` → `RemoveWhitelistEntryRequest` | **Có** | mục 6.4 file này |
| S4 Đổi mật khẩu | `ChangePasswordRequest` (tự gate qua `old_password`) | Tự gate (không qua `AuthVerifyRequest`) | `08` mục 7.4 |
| S5 Auth Modal | `AuthVerifyRequest` | — (chính nó là bước gate) | `08` mục 7.2 |
| S6 Recovery | `RecoveryResetRequest` (tự gate qua `recovery_key`) | Tự gate | `08` mục 7.5 |

### 5.1 Vì sao "xem log" (`PWD-020`) chỉ gate `S3` chi tiết, không gate biểu đồ `S2` (ADR-121)

`PWD-020` liệt kê "xem... log" vào danh sách hành động cần `S5`. `FE-070` cho phép biểu đồ thống kê xuất hiện ở **`S2` và/hoặc `S3`**. File này chọn đặt biểu đồ ở `S2` **không gate**, và danh sách sự kiện chi tiết (timestamp, `process_name`, `event_type` từng dòng) ở `S3` **có gate** — diễn giải "xem log" theo `PWD-020` là xem **nhật ký chi tiết** (nhiều thông tin hành vi cụ thể hơn), không phải 1 biểu đồ tổng hợp số đếm/ngày (ít nhạy cảm hơn nhiều, không có timestamp/tên app cụ thể). Đây là cách hiểu hợp lý trong phạm vi câu chữ `PWD-020`/`FE-070` cho phép (cả 2 đều không mâu thuẫn với lựa chọn này), không phải nới lỏng yêu cầu bảo mật đã duyệt — nếu chủ dự án muốn gate cả biểu đồ `S2`, đây là 1 dòng cấu hình đổi được dễ dàng, không ảnh hưởng kiến trúc.

### 5.2 Vì sao whitelist (`MISC-030`) cần gate riêng `manage_whitelist` (ADR-122)

`MISC-030`/`PWD-020` không liệt kê tường minh "quản lý whitelist" vào danh sách hành động cần `S5`. Quyết định: **vẫn gate**, vì thêm 1 process vào whitelist **làm YẾU giám sát** (loại trừ hẳn 1 ứng dụng khỏi capture — hệ quả tương đương "đổi cấu hình" mà `BE-013` mô tả chung là cần xác thực) — khác các thay đổi cosmetic khác ở `S4` (đổi câu chữ overlay, không ảnh hưởng phạm vi giám sát). Thêm hằng số `action_context="manage_whitelist"` mới (amendment `08` mục 7.2) thay vì tái dùng `"change_sensitivity_threshold"` (đã có nhưng dành riêng cho ngưỡng risk score, hiện không dùng vì `BE-091` cấm phơi ra UI) — giữ đúng 1 hằng số/1 loại hành động nghiệp vụ.

## 6. Thiết kế từng màn hình

### 6.1 `S1` — Onboarding

Luồng đúng thứ tự `FE-030`: Giới thiệu (`FE-031`) → Đặt mật khẩu (`PWD-001`–`003`) → Thiết lập khôi phục (`PWD-030`/`030a`) → Hoàn tất.

1. **Giới thiệu** (`FE-031`): đoạn text tĩnh (từ resource) giải thích "dữ liệu không rời máy, ảnh không được lưu" + checkbox "Tôi đã đọc và hiểu" bắt buộc tick trước khi Tiếp tục — thuần UI, không có IPC.
2. **Đặt mật khẩu**: 2 `PasswordBox` (mật khẩu/xác nhận, `PWD-003`), giới hạn 50 ký tự chặn cứng lúc nhập (`PWD-002a`), thanh đo độ mạnh (weak/medium/strong — **thuật toán cụ thể để `feature-dev` tự chọn, thuần UX polish theo `PWD-002` "khuyến nghị không bắt buộc cứng nhắc", không phải cơ chế bảo mật cần thiết kế ở đây**). Bấm Tiếp tục → memory hygiene phía `UI` đúng `08` mục 5.3 (đọc `PasswordBox.Password` → `byte[]` pinned ngay lập tức, gán lại `PasswordBox.Password=""`) → `SetInitialPasswordRequest{password}`.
   - `SetupResult.ALREADY_CONFIGURED` (race hiếm — 2 phiên Onboarding chạy song song, hoặc `Service` restart giữa chừng và phụ huynh chạy lại `UI`): hiện thông báo "Đã có mật khẩu được thiết lập" + điều hướng thẳng Main Shell (không lặp lại bước 1-2).
3. **Hiển thị Recovery Key** (`PWD-030`): `TextBlock` monospace hiển thị `recovery_key_plaintext` (UTF-8 decode 1 lần từ `bytes` nhận được) + nút "Sao chép" (Clipboard chuẩn Windows — **residual risk clipboard history ghi nhận minh bạch, không thiết kế cơ chế tự xoá clipboard timer riêng, không có căn cứ yêu cầu này trong `Specification/`**) + checkbox bắt buộc "Tôi đã lưu lại Recovery Key" (`PWD-030a`, `FE-030a`) khoá nút Tiếp tục cho tới khi tick. Zero buffer plaintext phía `UI` (biến cục bộ giữ chuỗi decode) trong `finally` ngay khi rời màn hình này (residual risk: `string` .NET bất biến, không zero tin cậy 100% được — ghi nhận minh bạch cùng tinh thần `08` mục 5.2, không giả vờ giải quyết triệt để ở tầng UI managed).
4. Bấm Tiếp tục (đã tick) → `ConfirmRecoveryKeySavedRequest{setup_token, confirmed=true}`.
   - `TOKEN_EXPIRED`/`TOKEN_NOT_FOUND` (`PWD-030a`: quá 30 phút, hoặc `Service` restart giữa chừng) → thông báo rõ ràng "Thiết lập chưa hoàn tất, vui lòng thực hiện lại từ bước đặt mật khẩu" → quay lại bước 2 (không giữ lại password cũ trong RAM `UI`).
   - `PERSISTED` → điều hướng Main Shell (`S2`).

### 6.2 `S2` — Dashboard

Bố cục (NavigationView content area):

1. **Thẻ trạng thái tổng quan**: icon + text theo `IconState` tương đương (Active=xanh "Đang hoạt động" / Paused=vàng "Tạm dừng — còn HH:MM" / Error=đỏ "Gián đoạn") — **màu sắc định nghĩa độc lập trong resource của `ParentalGuard.UI`** (không import trực tiếp từ `ParentalGuard.Overlay`, 2 project độc lập, chấp nhận trùng lặp giá trị hex nhỏ, cùng tinh thần chấp nhận trùng lặp default overlay message ở `07` mục 4.4). Dữ liệu: `DashboardStatusQuery` (`vision_connected`/`overlay_connected`/`watchdog_alive` → Error nếu bất kỳ cái nào `false`) + `PauseStatusQuery` (`is_paused`/`pause_expires_at_unix_ms`).
2. **Nút Pause/Resume**: nếu đang Active → nút "Tạm dừng giám sát" mở picker 5 lựa chọn thời lượng (`PAUSE-002`) → `S5` (`pause_monitoring`) → `PauseMonitoringRequest{action_token, duration}`. Nếu đang Paused → nút "Tiếp tục ngay" (`PAUSE-004`) → `S5` (cùng `action_context`) → `ResumeMonitoringRequest{action_token}`.
3. **Banner `PAUSE-021`** (`InfoBar` WinUI 3, mức Warning, dismissible): hiện nếu `PauseStatusQuery`/`DashboardStatusQuery.pause_anomaly_pending_ack=true` — nội dung tĩnh từ resource ("Tạm dừng được kích hoạt bất thường nhiều lần hôm nay. Nếu không phải bạn thực hiện, hãy đổi mật khẩu ngay."), nút "Đã xem" → `AcknowledgePauseAnomalyRequest` (không gate) → ẩn banner, không tự query lại field này cho tới lần poll tiếp theo.
4. **Health check** (`MISC-050`): danh sách gọn — Watchdog (✓/✗ theo `watchdog_alive`), Vision Engine (✓ nếu `vision_connected` và `vision_diagnostic_state` không chứa `"cpu-fallback"`; ⚠ cảnh báo `FE-041` nếu chứa `"ep=cpu-fallback"` — **so khớp chuỗi con đơn giản, không parse cấu trúc phức tạp, vì `vision_diagnostic_state` là free-text chẩn đoán theo đúng thiết kế `05` ADR-45, không phải enum có hợp đồng chặt**; các giá trị khác hiển thị nguyên văn dưới mục "Chi tiết kỹ thuật" thu gọn, không tự diễn giải), Overlay (✓/✗), Dung lượng log còn trống (cảnh báo nếu `audit_log_free_disk_bytes < 500 MB` — **ngưỡng UX tự quyết định, ADR-123, không phải số liệu bảo mật, tương tự tinh thần các hằng số UX khác đã tự quyết trong dự án như N=50 audit verify `04` ADR-28**), cờ `using_fallback_config` (nếu `true` → cảnh báo rõ "Đang dùng cấu hình mặc định do sự cố, vui lòng kiểm tra" — liên hệ `BE-061b`).
5. **Biểu đồ** (`FE-070`–`072`): `AuditChartQuery{range_days=7|30}` (toggle 7/30 ngày, `FE-071`), render bar chart (thư viện chart cụ thể để `feature-dev` chọn — ví dụ `LiveChartsCore.SkiaSharpView.WinUI` hoặc tự vẽ `Canvas`, thuần chi tiết implement, không chặn thiết kế kiến trúc). Trạng thái rỗng (`FE-040`): nếu toàn bộ `blocked_count=0` trong khoảng xem → hiện text tích cực "Chưa phát hiện nội dung nào cần chặn" thay vì biểu đồ trống trơn.

### 6.3 `S3` — Lịch sử / Audit log

- Vào tab → `NavigationService` tự hiện `S5` (`action_context="view_audit_log"`) **trước khi** render bất kỳ nội dung nào — huỷ dialog → điều hướng lại `S2`.
- SUCCESS → dùng ngay `action_token` vừa nhận cho `AuditLogQuery{action_token, page=0, page_size=50}`. Cuộn tới cuối / nút "Tải thêm" → gọi tiếp `page=1,2,...` — **lưu ý**: `action_token` dùng 1 lần (`08` mục 7.2), chỉ hợp lệ cho **request đầu tiên**; các trang tiếp theo trong cùng phiên xem **không cần** `action_token` mới (thiết kế `AuditLogQuery.action_token` chỉ validate ở **request đầu tiên mở phiên xem** — `feature-dev` cụ thể hoá: `Service` có thể chấp nhận `action_token` rỗng cho các trang kế tiếp nếu đã có 1 request hợp lệ trong cùng kết nối gần đây, nhưng đây là chi tiết nghiệp vụ nhỏ nằm trong phạm vi đã đủ rõ ở `03` mục 3.7 — không phát sinh gap kiến trúc mới, chỉ cần `Service`-side nhớ "đã qua gate" theo đúng session pipe hiện tại, tương tự cách 1 phiên `UI` giữ trạng thái đã login trong các app khác dù bản thân `action_token` không phải session).
- Mỗi dòng: thời gian (local time từ `ts_unix_ms`), mô tả `event_type` (bảng tra cứu string→text hiển thị cục bộ trong resource — vd `"ContentBlocked"` → "Nội dung bị chặn", `"PauseActivated"` → "Tạm dừng giám sát"... liệt kê đầy đủ khớp bảng `event_type` ở `04-data-architecture.md` mục 5.1 lúc implement, thuần mapping hiển thị không phải nghiệp vụ). Dòng `ContentBlocked` hiện thêm `process_name`/`risk_score` + nút "Đánh dấu sai" (`MISC-030`) → `S5` (`manage_whitelist`) → `MarkFalsePositiveRequest{action_token, process_name}`.
- Trạng thái rỗng (`FE-040`): tổng số 0 record → "Chưa phát hiện nội dung nào cần chặn".
- **Không hiển thị ảnh** dưới bất kỳ hình thức nào (đúng `BE-060`, và vốn dĩ không có field ảnh nào trong toàn bộ schema — bất biến kiến trúc, không phải giới hạn UI).

### 6.4 `S4` — Cài đặt nâng cao

- Vào tab → **không gate** — `ConfigQuery{}` load ngay (`overlay_message`, `user_whitelisted_process_names`).
- **Đổi mật khẩu**: 3 field (cũ/mới/xác nhận) → `ChangePasswordRequest{old_password, new_password, regenerate_recovery_key}` — checkbox "Sinh Recovery Key mới?" (`PWD-041`, mặc định tick sẵn theo khuyến nghị spec). `SUCCESS` + `regenerate_recovery_key=true` → hiện `new_recovery_key_plaintext` 1 lần (cùng UI component với bước 3 Onboarding, tái dùng `RecoveryKeyDisplayControl`). Link nhỏ "Quên mật khẩu cũ? Dùng Recovery Key" → điều hướng thẳng `S6` (không qua `S5`, vì đây chính là tình huống không có mật khẩu để xác thực).
- **Quản lý whitelist** (`MISC-030`): danh sách `user_whitelisted_process_names` (từ `ConfigResponse`), mỗi dòng có nút Xoá → `S5` (`manage_whitelist`) → `RemoveWhitelistEntryRequest{action_token, process_name}`. Không có nút "Thêm" trực tiếp ở đây (Phase 1 chỉ thêm qua luồng "Đánh dấu sai" ở `S3`, đúng nguyên văn `MISC-030`: "Khi phụ huynh xem lại audit log... đánh dấu 1 sự kiện chặn là sai" — không mô tả thêm bằng tay ngoài luồng đó; nếu cần "thêm thủ công không qua audit log", đó là mở rộng phạm vi `MISC-030`, để dành xác nhận thêm nếu chủ dự án muốn — không tự thêm ở đây).
- **Thông điệp overlay** (`FE-012`/`FE-012a`): `TextBox` (255 ký tự tối đa, bộ đếm ký tự còn lại hiển thị trực tiếp) — chặn nhập/dán ký tự cấm **ngay tại `TextChanging` event** (danh sách cho phép/cấm đúng `FE-012a`), nút "Khôi phục mặc định" → set về `""` cục bộ + gửi luôn (không cần xác nhận thêm). Bấm Lưu → `ConfigUpdateRequest{overlay_message}` (không gate, mục 5.2 lý do). `INVALID_CHARACTERS`/`TOO_LONG` (Service từ chối dù UI đã chặn — defense in depth, không tin client) → hiện lỗi inline, giữ nguyên giá trị cũ đã lưu.
- **Ngôn ngữ**: **không có UI element nào ở Phase 1** — `FE-061` chỉ có tiếng Việt, `FE-063` tự nói rõ "khi có nhiều lựa chọn ở phase sau" mới cần mục này trong `S4`. Không tạo control rỗng cho 1 lựa chọn duy nhất.
- **Ngưỡng nhạy cảm**: **không có, đúng `BE-091`** — không có control nào cho mục này trong `S4`, xác nhận tường minh loại trừ.
- **Chế độ hiệu năng** (`PERF-050`): **KHÔNG implement ở Đợt 6** — xem mục 11 (câu hỏi mở, blocking riêng phần này).
- **Gỡ cài đặt**: **không có nút nào trong Dashboard** — luồng gỡ cài đặt đi qua `ParentalGuard.Uninstaller.exe` độc lập (Windows Add/Remove Programs, `09-anti-tamper-architecture.md` ADR-91), không phải hành động khởi xướng từ `S4`.

### 6.5 `S5` — Auth Modal (component tái dùng)

`AuthPromptDialog : ContentDialog`, tham số hoá theo `action_context` (string) + `purposeText` (resource key tra theo `action_context` — vd `"pause_monitoring"` → "Xác nhận mật khẩu để tạm dừng giám sát"). Trả về `Task<byte[]? actionToken>` (null nếu huỷ) cho caller.

```
Người dùng nhập mật khẩu → Submit → AuthVerifyRequest{password, action_context}
  SUCCESS → trả action_token cho caller, đóng dialog
  WRONG_PASSWORD → hiện lỗi inline "Sai mật khẩu" (+ consecutive_failures nếu muốn hiển thị mức độ,
                    KHÔNG bắt buộc — thuần UX), giữ dialog mở, cho nhập lại
  LOCKED_OUT → khoá nút Submit, hiện đếm ngược tới lockout_until_unix_ms (đọc trực tiếp field response,
               KHÔNG UI tự tính lại logic rate-limit — Service là nguồn sự thật duy nhất, 08 mục 7.7)
Link "Quên mật khẩu?" (luôn hiển thị) → đóng dialog, điều hướng S6
```

Memory hygiene phía `UI`: giống hệt bước 2 Onboarding (`08` mục 5.3) — `byte[]` pinned, zero trong `finally` ngay sau khi request gửi xong.

**Xử lý `action_token` hết hạn giữa chừng** (race: user mất >15 giây giữa lúc `S5` trả `SUCCESS` và lúc hành động thật gửi đi, ví dụ đang chọn thời lượng Pause) → response nghiệp vụ (`PauseMonitoringResponse.result=INVALID_TOKEN` v.v.) → `Facade` ném exception riêng → `ViewModel` hiện "Phiên xác thực đã hết hạn, vui lòng thử lại" → tự mở lại `S5` **từ đầu** (không giữ token cũ, không silent-retry).

### 6.6 `S6` — Recovery

`RecoveryPage`: nhập Recovery Key (input cho phép gõ có `-`, tự chuẩn hoá client-side theo `08` mục 6.2 trước khi gửi — **Service vẫn chuẩn hoá lại lần nữa, không tin input UI**, đúng nguyên tắc đã có) + mật khẩu mới + xác nhận → `RecoveryResetRequest{recovery_key, new_password}`.

- `SUCCESS` → hiện `new_recovery_key_plaintext` 1 lần (`PWD-032`, tái dùng `RecoveryKeyDisplayControl`) → quay lại Main Shell.
- `WRONG_RECOVERY_KEY`/`LOCKED_OUT` → cùng mẫu hình hiển thị lỗi như `S5`.

### 6.7 `S7`/`S8`/`S9`

Không thuộc phạm vi file này — xem `07-overlay-architecture.md`. Liệt kê lại ở đây chỉ để đối chiếu đầy đủ danh sách màn hình `Specification/03-frontend-ui-spec.md` mục 2.

## 7. Đa ngôn ngữ (`FE-060`–`063`, ADR-124)

**Quyết định: tái dùng đúng pattern `.resx` + `System.Resources.ResourceManager` đã có ở `ParentalGuard.Overlay`** (`src/ParentalGuard.Overlay/Resources/OverlayStrings.cs`) — **không** dùng cơ chế `.resw`/`ResourceLoader` gốc của Windows App SDK. Lý do: (a) `.resw`/`ResourceLoader` thiết kế cho app **packaged** (đánh index qua PRI resource pipeline gắn với package identity) — `ParentalGuard.UI` là unpackaged (mục 2.1), dùng `.resw` sẽ cần thêm cấu hình PRI phức tạp không cần thiết cho nhu cầu hiện tại; (b) nhất quán 1 pattern resource xuyên suốt 6 executable của dự án, giảm chi phí nhận thức; (c) `FE-060`/`061` tường minh để ngỏ lựa chọn kỹ thuật này cho System Design ("`.resw` chuẩn của Windows App SDK hoặc `.json`/`.resx` tuỳ lựa chọn kỹ thuật").

- `LocalizationService` (mục 2.1) wrap `ResourceManager("ParentalGuard.UI.Resources.UiStrings", typeof(LocalizationService).Assembly)`, expose qua `x:Bind`/`{x:Bind LocalizationService.Get('Key')}` hoặc `x:Uid` markup extension tuỳ chi tiết implement.
- **Ngoại lệ `overlay_message` default text** (`FE-062`): giá trị mặc định gợi ý hiển thị trong ô nhập ở `S4` (khi `ConfigResponse.overlay_message=""`) đọc từ **`UiStrings.resx`** riêng của `ParentalGuard.UI` (key `DefaultOverlayMessage`, giá trị **phải khớp chính xác** giá trị tương ứng ở `OverlayStrings.resx` của `ParentalGuard.Overlay` — chấp nhận trùng lặp 1 chuỗi giữa 2 project, đã ghi nhận ở `07-overlay-architecture.md` mục 4.4/ADR-110, không tạo project resource dùng chung chỉ vì 1 chuỗi).
- `FE-063`: **không implement chọn ngôn ngữ ở Phase 1** (chỉ 1 ngôn ngữ tồn tại — không có gì để chọn, mục 6.4). Kiến trúc `.resx`/`ResourceManager` theo `CurrentUICulture` đã sẵn sàng mở rộng bằng cách thêm `UiStrings.en.resx` ở Phase 2 mà không cần sửa code logic — đúng yêu cầu `FE-061`.

## 8. Accessibility (`FE-050`)

WinUI 3 cung cấp accessibility cơ bản (contrast, keyboard nav, screen reader) **sẵn có** cho mọi control chuẩn (`Button`, `TextBox`, `NavigationView`, `ContentDialog`...) — yêu cầu thiết kế: **chỉ dùng control chuẩn WinUI 3**, không tự vẽ control tuỳ biến bằng `Canvas`/custom-draw (khác `Overlay`, nơi bắt buộc phải tự vẽ layered-window cho icon trạng thái vì lý do click-through/vùng loại trừ — `Dashboard` là cửa sổ app thông thường, không có ràng buộc đó). `AutomationProperties.Name` gán tường minh cho mọi control không có label trực quan rõ ràng (ví dụ icon-only button). Biểu đồ (`S2` mục 6.2.5) cần `AutomationProperties` mô tả dạng text tóm tắt (ví dụ "7 ngày qua: 3 lần chặn") cho screen reader, vì chart control tự vẽ thường không tự có accessibility tree đầy đủ — **chi tiết cụ thể để `feature-dev` implement theo chuẩn WinUI 3**, không chặn thiết kế kiến trúc.

`S7` (Overlay) **không thuộc phạm vi accessibility bar này** — đúng `FE-050` tự nói rõ ("overlay `S7` có thể tối giản accessibility hơn"), và vốn dĩ không thuộc `ParentalGuard.UI`.

## 9. Trạng thái rỗng & lỗi (`FE-040`/`041`)

| Tình huống | Xử lý |
|---|---|
| Không kết nối được `Service` lúc khởi động `UI` (`ConnectAsync` fail/timeout) | Màn hình toàn cửa sổ: icon lỗi + "Không thể kết nối tới ParentalGuard Service — Service có thể chưa chạy hoặc đang khởi động" + nút "Thử lại" (gọi lại `ConnectAsync`) — **không** crash app, **không** cố đoán nguyên nhân cụ thể (không có quyền query SCM trạng thái Service từ `UI` — chỉ user thường, không thiết kế thêm quyền cho việc này) |
| Mất kết nối giữa chừng (poll `S2` hoặc bất kỳ `SendRequestAsync` nào ném `UiIpcConnectionException`) | `InfoBar` lỗi ở đầu trang hiện tại: "Mất kết nối tới Service, đang thử lại..." — giữ nguyên dữ liệu cũ đã hiển thị (làm mờ nhẹ, không xoá trắng — tránh hiểu nhầm "đã hết trạng thái", nhất quán tinh thần fail-secure hiển thị đã áp dụng cho `Overlay` giữ nguyên rect lúc mất kết nối, `03` mục 6). `DashboardViewModel` tự thử `ConnectAsync` lại ở lần tick tiếp theo (5s, mục 3.3) |
| `S3` chưa có sự kiện nào (`FE-040`) | "Chưa phát hiện nội dung nào cần chặn" (tích cực, không trống trải tiêu cực) |
| `S2` biểu đồ toàn 0 (`FE-040`) | Cùng thông điệp tích cực, thay cho biểu đồ trống |
| Vision Engine CPU-fallback (`FE-041`) | Cảnh báo trong Health check (mục 6.2.4), không phải lỗi chặn — app vẫn hoạt động, chỉ chậm hơn |
| `using_fallback_config=true` (`BE-061b`) | Cảnh báo rõ trong Health check — liên kết đúng ý nghĩa đã thiết kế ở `04-data-architecture.md` mục 6.2 |
| `action_token` hết hạn giữa `S5` và hành động thật | Xem mục 6.5 — mở lại `S5` từ đầu, không silent-retry |

## 10. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-114 | WinUI 3 unpackaged (`WindowsPackageType=None`) | Nhất quán 5 executable còn lại (Win32 `.exe` thuần); MSIX cho cả app chưa quyết định (thuộc `11`, chưa viết) |
| ADR-115 | `CommunityToolkit.Mvvm` cho MVVM | Chuẩn cộng đồng .NET, MIT, nhẹ (source generator), nhất quán mức thận trọng dependency đã áp dụng ở `08` ADR-71 |
| ADR-116 | ViewModel không gọi trực tiếp `UiIpcClient`/`IpcPayload` — qua Facade theo domain | Tách concern, dễ test, `IpcPayload` không thân thiện binding XAML |
| ADR-117 | `UI` khởi động độc lập qua Start Menu/Desktop shortcut, KHÔNG click-to-open từ icon trạng thái | Đã chốt sẵn ở `02` mục 2.5; xác nhận lại `StatusIconForm` không có handler click nào, không phát minh thêm hành vi `Overlay` ngoài phạm vi `Specification/` |
| ADR-117a | Single-instance qua named `Mutex` + activate cửa sổ cũ, không chỉ dựa vào giới hạn 1 kết nối của pipe | UX polish — tránh lỗi kết nối khó hiểu khi mở `UI.exe` lần 2 |
| ADR-118 | Không tái dùng `IpcChildClient`/`ChildIpcBootstrap` cho `UI` | Bootstrap khoá qua handle kế thừa chỉ khả thi cho tiến trình con `CreateProcessAsUser`; `UI` cần khung khoá 2 giai đoạn (zero-key → session-key) mà `IpcChildClient` không hỗ trợ |
| ADR-119 | `UiIpcClient` không có Reader/Writer loop song song — request/response tuần tự qua `SemaphoreSlim(1)` | Kênh `UI` vốn thiết kế "không heartbeat định kỳ — request/response theo nhu cầu" (`03` mục 2.1), không có push nào cần lắng nghe nền |
| ADR-120 | `DashboardViewModel` poll 5 giây (`DispatcherQueueTimer`, chỉ khi `S2` đang hiển thị) thay vì cơ chế push | Kênh `UI` không thiết kế push (đúng nguyên tắc gốc Đợt 0); polling đủ cho cảm nhận "sống" của Dashboard, tải không đáng kể |
| ADR-121 | Biểu đồ `S2` không gate `S5`, danh sách chi tiết `S3` có gate | Diễn giải "xem log" (`PWD-020`) là nhật ký chi tiết, không phải biểu đồ tổng hợp số đếm — cả `PWD-020`/`FE-070` đều không mâu thuẫn với cách chia này |
| ADR-122 | Whitelist management (`MISC-030`) cần `action_token` riêng (`manage_whitelist`, hằng số mới) dù `PWD-020` không liệt kê tường minh | Thêm whitelist làm YẾU giám sát — tương đương "đổi cấu hình" (`BE-013`), khác thay đổi cosmetic khác ở `S4` |
| ADR-123 | Ngưỡng cảnh báo dung lượng đĩa còn trống cho audit log: 500 MB | Số liệu UX tự quyết định (không phải ngưỡng bảo mật), cùng tinh thần các hằng số UX khác đã tự quyết trong dự án (`04` ADR-28: N=50) |
| ADR-124 | Localization dùng `.resx`+`ResourceManager` (tái dùng pattern `Overlay`), không dùng `.resw`/`ResourceLoader` | `.resw` cần package identity (PRI pipeline) — `UI` unpackaged; nhất quán 1 pattern xuyên suốt dự án; `FE-060`/`061` để ngỏ lựa chọn này cho System Design |

## 11. Câu hỏi mở

- [ ] **`PERF-050` (Chế độ hiệu năng, `S4`) — CẦN `spec-maintainer`/chủ dự án chuyển trạng thái `PROPOSED` → `APPROVED`/`REJECTED` trước khi implement phần này của `S4`** (không blocking phần còn lại của Đợt 6 — mục 6.4 ở trên đã loại trừ tường minh mục này khỏi `S4` cho tới khi có quyết định). Chi tiết: `Specification/08-performance-cpu-spec.md` mục 6, `PERF-050` còn mang tag `(PROPOSED)` ngay trong văn bản requirement, dù bản thân file đã đóng dấu "Trạng thái: Approved" ở cấp toàn file và "không còn câu hỏi mở" ở mục 8 — cùng dạng gap 2-tầng-trạng-thái đã gặp ở `PAUSE-021` (nay đã đóng, xem `02` mục 3a.7) và `ANTI-060` trước khi chốt. Chính văn bản `PERF-050` tự nêu rủi ro sản phẩm chưa được cân nhắc kỹ: *"có nên cho phép chọn 'Tiết kiệm pin' không, vì nó làm giảm hiệu quả bảo vệ"* — đây là quyết định đánh đổi bảo mật-khả dụng, không phải chi tiết HOW thuần kỹ thuật, `architecture-writer` không tự quyết định. Đề nghị: (a) `spec-maintainer` chính thức chuyển `PERF-050` sang `APPROVED` (giữ nguyên/điều chỉnh 3 mức đề xuất) hoặc `REJECTED`; (b) nếu `APPROVED`, xác nhận rõ có giữ nguyên tuỳ chọn "Tiết kiệm pin" hay bỏ (chỉ còn "Cân bằng"/"Bảo vệ tối đa"). Cơ chế kỹ thuật nếu được duyệt: đơn giản — 1 field `performance_mode` mới trong `monitoring_state` JSON (`04-data-architecture.md`), `ConfigQuery`/`ConfigUpdateRequest` thêm field tương ứng (đã chừa sẵn dư field number ở khối 140-159, mục 5), `Service` áp `capture_interval_baseline_ms` khác theo mode — không cần thiết kế kiến trúc mới, chỉ chờ đúng 1 quyết định sản phẩm.
- [ ] **`MISC-030` — phạm vi "domain" không khả thi kỹ thuật với kiến trúc hiện tại, đề nghị `spec-maintainer` làm rõ câu chữ (non-blocking)**: văn bản `Specification/10-additional-mechanisms-spec.md` mục 3 và `Specification/03-frontend-ui-spec.md` (`S4`) đều nói "whitelist... domain/app", nhưng toàn bộ pipeline phát hiện (`Vision`) hoạt động **thuần theo pixel/window** (`BE-021`, không OCR/không trích xuất URL/domain nào ở bất kỳ đâu trong `Specification/`/`Architecture/`) — hoàn toàn không có cách nào hệ thống biết "domain" nào đang hiển thị trong 1 cửa sổ browser, và điều này **nhất quán với chính triết lý gốc của dự án** (`Specification/01-tong-quan-va-pham-vi.md`: *"độc lập với domain/URL"*, từ bỏ hẳn cách tiếp cận domain-filtering truyền thống). File này (mục 6.3/6.4) chỉ implement phần **"app" (process name)** — khả thi đầy đủ, đã thiết kế trọn vẹn (mục 6.3/6.4, amendment `03`/`04`/`05`) — và **không** phát minh cơ chế domain-detection nào (sẽ là quyết định sản phẩm + kỹ thuật lớn ngoài phạm vi Đợt 6, cần OCR/DOM inspection hoàn toàn mới). Đề nghị `spec-maintainer` sửa câu chữ "domain/app" → "app" ở 2 file trên cho khớp thực tế kỹ thuật (sửa câu chữ làm rõ, không đổi ý nghĩa cốt lõi của `MISC-030`) — **không blocking**, vì phần "app" đã đủ để triển khai đúng tinh thần chính của `MISC-030` (giảm friction false-positive).
- [ ] Thuật toán cụ thể thanh đo độ mạnh mật khẩu (`PWD-002`, mục 6.1 bước 2) — thuần UX polish, để `feature-dev` tự chọn, không chặn thiết kế.
- [ ] Thư viện chart cụ thể cho `S2`/`FE-070` (mục 6.2.5) — chi tiết implement, không chặn thiết kế kiến trúc.
- [ ] Icon/màu sắc chính xác cho 3 trạng thái Dashboard (mục 6.2.1) — chi tiết asset, cùng tinh thần câu hỏi mở tương tự đã có ở `07-overlay-architecture.md` mục 6 cho icon `Overlay`, để `feature-dev`/thiết kế UI quyết định theo Design System (`03-frontend-ui-spec.md` mục 4).

## 12. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.1.0 | 2026-09-20 | Khởi tạo — Đợt 6 (`ROADMAP.md`, Dashboard UI). Kiến trúc tổng thể WinUI 3 unpackaged/MVVM (`CommunityToolkit.Mvvm`), cơ chế khởi động độc lập qua shortcut (xác nhận lại `02` mục 2.5, không click-to-open từ icon — đã đối chiếu code `StatusIconForm`), xác nhận độc lập hoàn toàn với `Overlay`/`Uninstaller` (mục 2.4). Thiết kế mới `UiIpcClient` (khác biệt kiến trúc với `IpcChildClient` — không bootstrap qua handle kế thừa, khung khoá 2 giai đoạn zero-key→session-key theo `03` ADR-82/mục 5.3, request/response tuần tự không cần Reader/Writer loop, polling 5s thay vì push cho trạng thái Dashboard). Navigation map đầy đủ `S1`-`S9` (`S7`-`S9` xác nhận ngoài phạm vi). Thiết kế chi tiết `S1` Onboarding, `S2` Dashboard (health check `MISC-050`, biểu đồ `FE-070`–`072`, banner `PAUSE-021`), `S3` Audit log (phân trang, gate `view_audit_log`, đánh dấu false-positive `MISC-030`), `S4` Settings (đổi mật khẩu, quản lý whitelist, thông điệp overlay `FE-012`/`012a` — loại trừ tường minh ngôn ngữ/ngưỡng nhạy cảm/hiệu năng/gỡ cài đặt), `S5` Auth Modal tái dùng, `S6` Recovery. Localization tái dùng pattern `.resx` từ `Overlay` (không dùng `.resw`). Accessibility/Empty-error states đầy đủ. 11 ADR (114-124). Amendment cùng lượt: `02-process-architecture.md` v0.3.0 (mục 3a.7, đóng `PAUSE-021`), `03-ipc-communication.md` v0.8.0 (10 message mới field 98-99+140-159, `VisionInferenceResult.process_name`, `OverlayMessageUpdate`), `04-data-architecture.md` v0.3.0 (3 field JSON mới, schema `ContentBlocked.detail`), `05-image-pipeline-architecture.md` v0.2.1 (field `ProcessName`), `07-overlay-architecture.md` v0.2.3 (mục 4.4, sửa bug hardcode Đợt 1), `08-password-authentication-architecture.md` v0.3.1 (`action_context="manage_whitelist"`). **2 câu hỏi mở CHÍNH cần `spec-maintainer`/chủ dự án xử lý** (`PERF-050` còn `PROPOSED` — blocking riêng phần "Chế độ hiệu năng" của `S4`; `MISC-030` phạm vi "domain" không khả thi kỹ thuật — non-blocking, chỉ cần sửa câu chữ spec). Toàn bộ phần còn lại của Đợt 6 (Onboarding/Dashboard/Audit log/Settings trừ hiệu năng/Auth/Recovery) đã thiết kế đủ chi tiết để `feature-dev` implement thẳng. Theo chỉ đạo — không dừng chờ review từng file, dừng lại báo cáo sau khi xong đủ file mới + toàn bộ amendment |

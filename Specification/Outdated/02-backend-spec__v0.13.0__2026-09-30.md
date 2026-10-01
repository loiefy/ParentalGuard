# 02 — Backend Spec

> Version: v0.13.0 | Trạng thái: Approved | Cập nhật: 2026-09-19

"Backend" ở đây nghĩa là toàn bộ phần chạy nền không phải UI: Watchdog Service, Vision Engine (capture + AI), Overlay Controller, và lớp lưu trữ cấu hình local.

## 1. Kiến trúc tổng thể (4 tiến trình tách biệt)

| Module | Loại process | Quyền chạy | Network access |
|---|---|---|---|
| `ParentalGuard.Service` | Windows Service | LocalSystem | **Không** — tuyệt đối, không có ngoại lệ auto-update (`MISC-020` REJECTED, xem `10-additional-mechanisms-spec.md`) |
| `ParentalGuard.Vision` | Child process do Service spawn **vào session tương tác của user** qua `CreateProcessAsUser` (xem `BE-023a`) — không phải Session 0 | User session, quyền hạn chế (AppContainer/restricted token nếu khả thi) | **Cấm tuyệt đối** — enforce ở tầng OS (Windows Filtering Platform rule chặn outbound cho chính process này) |
| `ParentalGuard.Overlay` | Process riêng, chạy trong session người dùng đang đăng nhập | User session, quyền thấp | Không |
| `ParentalGuard.UI` | App WinUI 3, chạy khi phụ huynh mở dashboard | User session | **Không** — tuyệt đối, không có tính năng check update (`MISC-020` REJECTED) |

Lý do tách `Vision` thành process riêng: đây là module nhạy cảm nhất (xử lý ảnh chụp màn hình) — nếu nó bị compromise, việc tách process + chặn network ở tầng OS đảm bảo dữ liệu vẫn không thể rời máy dù code có bug hoặc bị inject.

## 2. Chi tiết từng module

### 2.1 `ParentalGuard.Service` (BE-010)

- Chạy dưới dạng Windows Service, khởi động cùng hệ thống (trước khi user đăng nhập), độc lập với session người dùng.
- Trách nhiệm:
  - `BE-011`: Khởi tạo và giám sát vòng đời của `Vision` và `Overlay` process (spawn, restart nếu crash, kill nếu cần).
  - `BE-012`: Lưu trữ và enforce cấu hình (bật/tắt giám sát, ngưỡng nhạy cảm, danh sách app được whitelist).
  - `BE-013`: Xử lý xác thực mật khẩu cho các hành động nhạy cảm (gỡ cài, tạm dừng, đổi cấu hình) — xem `06-password-management-spec.md`.
  - `BE-014`: Ghi audit log (metadata only) vào local storage đã mã hoá.
  - `BE-015`: Đóng vai trò watchdog cho chính nó thông qua cơ chế được mô tả ở `05-anti-uninstall-tamper-spec.md`.

### 2.2 `ParentalGuard.Vision` (BE-020)

- **Nguyên tắc single-purpose (liên kết `SEC-017`, `04-security-spec.md`)**: `Vision` chỉ làm đúng 1 việc — capture + phân tích ảnh + trả kết quả về `Service`. Không network, không đọc/ghi registry, không đọc/ghi file (trừ đọc model `.onnx` read-only), không tự đọc `config.db`, không spawn tiến trình con, không giao tiếp trực tiếp với `Overlay`/`UI`. Toàn bộ cấu hình `Vision` cần (bật/tắt, tần suất, ngưỡng) đến từ lệnh IPC do `Service` gửi xuống, không tự lấy từ nguồn nào khác.
- **`BE-023a` (mới, quan trọng — quyết định kiến trúc bắt buộc do Session 0 Isolation)**: `Vision` **không thể** được `Service` spawn theo cách `CreateProcess` thông thường, vì `Service` chạy trong **Session 0** (cô lập, không có quyền truy cập desktop tương tác — cơ chế bảo mật chuẩn của Windows từ Vista trở đi), trong khi Desktop Duplication API (DXGI) mà `Vision` dùng để capture **chỉ hoạt động trong session tương tác** (Session 1+, nơi người dùng thực sự đăng nhập). Kiến trúc bắt buộc:
  1. `Service` dùng `WTSQueryUserToken(sessionId)` lấy token của user đang đăng nhập ở session tương tác đang active (`WTSGetActiveConsoleSessionId`).
  2. `DuplicateTokenEx()` nhân bản token.
  3. `CreateProcessAsUser()` khởi chạy `Vision.exe` **vào đúng session tương tác đó** — không dùng `CreateProcess` thông thường.
  4. `Service` đăng ký `WTSRegisterSessionNotification` để nhận sự kiện đổi session (khoá màn hình, Fast User Switching, ngắt/kết nối Remote Desktop) — khi active console session đổi, `Service` phải dừng `Vision` cũ và khởi chạy lại `Vision` mới vào session đang active, tránh giám sát nhầm session không hiển thị hoặc bỏ sót session đang thực sự hiển thị.
- **`BE-023b` (mới)**: Thao tác `WTSQueryUserToken`/`CreateProcessAsUser` chỉ được thực hiện bởi `Service` — đây là thao tác nhạy cảm (process quyền SYSTEM tạo process khác dưới danh nghĩa user đăng nhập), không module nào khác được phép gọi các API này. `Vision` sau khi được tạo trong session người dùng **vẫn phải tuân thủ đầy đủ** nguyên tắc single-purpose ở trên — chạy trong session tương tác không đồng nghĩa được nới lỏng ràng buộc không network/không registry/không file write (liên kết `SEC-017`).
- Nhận lệnh bật/tắt/điều chỉnh tần suất từ Service qua IPC nội bộ.
- Vòng lặp chính: capture → tiền xử lý → inference → gửi kết quả (chỉ bounding box + risk score, KHÔNG gửi ảnh) về Service/Overlay.
- `BE-021`: Sau khi inference xong, buffer ảnh trong RAM phải được xoá (zero-out) ngay lập tức trong cùng frame xử lý — chi tiết ở `09-image-processing-spec.md`.
- `BE-022`: Không có logging chứa dữ liệu ảnh ở module này dưới bất kỳ log level nào, kể cả debug build (phải có lint/test tự động kiểm tra điều này — xem `11-testing-qa-process.md`).
- `BE-023`: Nếu process bị crash, Service phát hiện qua heartbeat (xem 2.4) và tự khởi động lại trong vòng ≤ 3 giây, đồng thời ghi audit log sự kiện gián đoạn.

### 2.3 `ParentalGuard.Overlay` (BE-030)

- **ĐÃ CHỐT (v0.3.0)**: Module này hỗ trợ **nhiều instance đồng thời** — mỗi cửa sổ vi phạm được phát hiện (dù cùng 1 ứng dụng nhiều cửa sổ/tab, hay nhiều ứng dụng khác nhau cùng lúc, hay trải trên nhiều màn hình khác nhau) sẽ có **1 overlay blur riêng biệt** phủ đúng lên cửa sổ đó, độc lập với các overlay khác. Không có giới hạn cứng số lượng overlay đồng thời ở mức thiết kế (giới hạn thực tế chỉ đến từ hiệu năng máy — xem `PERF-060` ở `08-performance-cpu-spec.md`).
- Nhận toạ độ/kích thước cửa sổ cần che + risk score từ Vision (qua Service làm trung gian, không giao tiếp trực tiếp Vision↔Overlay để giữ nguyên tắc least-privilege). Với nhiều cửa sổ vi phạm cùng lúc, Service gửi 1 danh sách (list) các cửa sổ cần che, Overlay module tự quản lý vòng đời từng instance overlay tương ứng (tạo mới khi có cửa sổ vi phạm mới, huỷ khi cửa sổ đó không còn vi phạm hoặc đã bị đóng).
- `BE-031`: Vẽ overlay đúng vị trí cửa sổ vi phạm (dùng `GetWindowRect` + `WinEventHook` để theo dõi resize/move real-time) — áp dụng độc lập cho từng instance.
- `BE-032` **(cập nhật v0.11.3 — PATCH làm rõ câu chữ, không đổi hành vi người dùng thấy)**: Hiển thị nút "Tắt nội dung" trên mỗi overlay → khi bấm, **`Overlay` tự gọi API OS (`PostMessage(WM_CLOSE)`) lên đúng window handle đó ngay tại chỗ** để đóng cửa sổ/ứng dụng vi phạm tương ứng (không ảnh hưởng các cửa sổ vi phạm khác đang có overlay riêng), đồng thời gửi `ForceCloseRequest` lên `Service` để `Service` cập nhật lại danh sách overlay đang active + ghi audit log. Lý do kỹ thuật (phát hiện lúc implement Đợt 1, xem `Architecture/02-process-architecture.md` v0.1.1): `Service` chạy ở **Session 0** (Session 0 Isolation, `BE-023a`) nên **không có quyền gọi bất kỳ API `user32` nào** (kể cả đóng cửa sổ) lên 1 HWND thuộc window station của session tương tác — đây cũng chính là lý do gốc buộc `Vision`/`Overlay` phải chạy trong session tương tác thay vì Session 0. Vì vậy chỉ `Overlay` (đã ở session tương tác) mới có quyền vật lý thực thi hành động đóng cửa sổ; `Service` vẫn giữ đúng vai trò "nguồn sự thật duy nhất" cho state (quyết định window nào cần che, ghi nhận window nào đã đóng, audit log) — chỉ khác ai là bên gọi API OS thật sự. Câu ban đầu "Service thực hiện force-close" (bản v0.11.2 trở về trước) không khớp thực tế vật lý này và được làm rõ lại ở đây; hành vi cuối cùng người dùng thấy (bấm nút → cửa sổ vi phạm đóng, có audit log) không đổi.
- `BE-033`: Icon trạng thái giám sát (góc màn hình) cũng thuộc trách nhiệm module này — luôn hiển thị, không thể tắt bởi user thường. Với nhiều màn hình, icon hiển thị trên **mỗi màn hình** (xem `BE-080`).

### 2.4 Cơ chế Heartbeat & giám sát lẫn nhau (BE-040)

- Service ↔ Vision: heartbeat mỗi 1 giây qua Named Pipe. Mất 3 heartbeat liên tiếp → coi là crash → restart.
- Service ↔ Overlay: tương tự, heartbeat mỗi 2 giây (overlay ít quan trọng về mặt real-time hơn Vision).
- Một watchdog thứ cấp độc lập (mô tả chi tiết ở `05-anti-uninstall-tamper-spec.md`) giám sát chính Service — tránh single point of failure nếu Service bị kill hoàn toàn.

## 3. Cơ chế IPC (Inter-Process Communication)

| Kênh | Công nghệ | Lý do chọn |
|---|---|---|
| Service ↔ Vision | Named Pipes (`System.IO.Pipes`), payload dạng Protobuf nhị phân nhỏ gọn | Nhanh, local-only theo thiết kế, dễ enforce ACL chỉ cho phép 2 process cụ thể kết nối |
| Service ↔ Overlay | Named Pipes tương tự | Đồng nhất công nghệ, dễ maintain |
| Service ↔ UI (dashboard) | Named Pipes hoặc gRPC over Named Pipe binding | UI cần request/response phức tạp hơn (xem cấu hình, lịch sử log) |

- `BE-050`: Named Pipe phải được cấu hình ACL chỉ cho phép SID cụ thể của các process ParentalGuard kết nối — chặn tiến trình lạ giả mạo gửi lệnh vào pipe.
- `BE-051`: Toàn bộ message qua IPC nội bộ nên được ký (HMAC với key sinh ngẫu nhiên lúc cài đặt, lưu trong DPAPI) để chống injection nếu ACL bị bypass bởi lỗ hổng khác.

## 4. Lưu trữ dữ liệu local

| Loại dữ liệu | Nơi lưu | Mã hoá |
|---|---|---|
| Cấu hình app (ngưỡng, whitelist, lịch sử bật/tắt) | `%ProgramData%\ParentalGuard\config.db` (SQLite) | Mã hoá bằng Windows DPAPI (machine-scope, chỉ SYSTEM đọc được) |
| Password hash | Xem `06-password-management-spec.md` | Argon2id, lưu tách biệt khỏi config.db |
| Audit log (metadata only) | `%ProgramData%\ParentalGuard\audit.log` (append-only, hash-chained) | **Không mã hoá nội dung** (không chứa dữ liệu nhạy cảm) — chỉ dùng **hash-chain** để đảm bảo toàn vẹn (integrity), phát hiện nếu bị xoá/sửa entry giữa chừng (xem `SEC-041` ở `04-security-spec.md`, và `10-additional-mechanisms-spec.md`) |
| Model AI (.onnx) | `%ProgramFiles%\ParentalGuard\models\` | Không cần mã hoá (không phải dữ liệu nhạy cảm), nhưng cần verify checksum khi load để chống tamper |

- `BE-060`: **Tuyệt đối không** có bảng/file nào trong hệ thống lưu trữ này chứa dữ liệu hình ảnh hoặc frame đã capture.
- `BE-061` (ĐÃ CHỐT v0.6.0): Nếu `Service` không đọc được `config.db` (file bị hỏng/corrupt, bị sửa đổi trái phép làm sai cấu trúc, hoặc DPAPI giải mã thất bại), `Service` **không được dừng hoạt động** — phải tự động dùng **bộ cấu hình mặc định hard-code sẵn trong code** (không đọc từ file ngoài) để tiếp tục chạy. Bộ mặc định này bắt buộc ở trạng thái an toàn nhất theo nguyên tắc fail-secure: **giám sát luôn BẬT** (không bao giờ mặc định là tắt giám sát), dùng ngưỡng risk score mặc định đã benchmark ở `BE-090`, whitelist rỗng. Đồng thời `Service` ghi audit log sự kiện này (timestamp, lý do không đọc được config) để phụ huynh thấy trong Dashboard — xem thêm `ANTI-070` ở `05-anti-uninstall-tamper-spec.md` (cùng 1 quyết định, được liên kết 2 chiều để tránh trùng lặp nội dung).
- `BE-061a` (ĐÃ CHỐT v0.10.0): Sau khi fallback, `Service` **tự động ghi đè `config.db`** bằng bộ cấu hình mặc định (mã hoá lại bằng DPAPI theo đúng quy trình chuẩn ở `BE-060`/`SEC-040`) — để các lần khởi động sau đọc được config hợp lệ ngay lập tức, không phải lặp lại fallback mỗi lần khởi động.
- `BE-061b` (ĐÃ CHỐT v0.10.0): Đồng thời, `Service` gửi lệnh qua IPC nội bộ (Named Pipe Service↔Overlay đã có ở mục 3) tới `ParentalGuard.Overlay` (process luôn chạy sẵn trong session người dùng, xem `BE-030`/`BE-033`) để hiển thị **1 Windows Toast Notification** (native, local, không qua network — đúng nguyên tắc `SEC-001`) cảnh báo phụ huynh: *"Cấu hình giám sát không còn toàn vẹn, vui lòng kiểm tra lại."* Đây là thông báo **chủ động** — khác với sự kiện kill-restart lặp lại (`ANTI-061` ở `05-anti-uninstall-tamper-spec.md`) chỉ ghi log thụ động — vì mất toàn vẹn config là sự kiện xác định rõ ràng (không phải heuristic có thể false positive), phụ huynh cần biết ngay.

## 5. Data flow tổng quát (happy path)

```
[Desktop Duplication API]
        │ raw frame (RAM only)
        ▼
[Vision: tiền xử lý — resize/downscale]
        │
        ▼
[Vision: ONNX Runtime inference]
        │ risk score + bounding box (không phải ảnh)
        ▼
[Service: nhận kết quả, so ngưỡng cấu hình]
        │ nếu vượt ngưỡng
        ▼
[Service: lấy toạ độ + vùng title bar/nút đóng của cửa sổ ứng dụng vi phạm]
        │ (bất kể loại ứng dụng — browser, video player, app khác)
        ▼
[Overlay: vẽ blur đúng vị trí, CHỪA vùng nút đóng cửa sổ + hiện nút "Tắt nội dung" (xem FE-016)]
        │ user bấm nút
        ▼
[Overlay: tự PostMessage(WM_CLOSE) lên window handle vi phạm + gửi ForceCloseRequest lên Service (BE-032)]
        │
        ▼
[Service: cập nhật danh sách overlay active, ghi audit log — timestamp, action, risk score. KHÔNG ghi ảnh]
```

## 6. Phạm vi giám sát: không giới hạn theo whitelist ứng dụng (BE-070, BE-071)

> **Cập nhật v0.2.0**: Ban đầu spec giới hạn giám sát ở danh sách browser cụ thể. Sau khi review, phạm vi được mở rộng: app phải giám sát **bất kỳ ứng dụng nào** hiển thị nội dung khiêu dâm, không chỉ browser — bao gồm video player (VLC, Windows Media Player, PotPlayer, MPC-HC...), ứng dụng xem ảnh, và các ứng dụng khác. Cách tiếp cận kỹ thuật thay đổi tương ứng như sau.

- `BE-071`: **Chiến lược "giám sát theo cửa sổ đang active" (foreground-window-based), không phải whitelist theo tên ứng dụng.** Thay vì kiểm tra process name có nằm trong danh sách browser đã biết hay không (cách tiếp cận cũ, đã lỗi thời), Vision Engine capture và phân tích nội dung của **bất kỳ cửa sổ nào đang ở foreground** (`GetForegroundWindow`), bất kể đó là ứng dụng gì. Đây là thay đổi kiến trúc quan trọng: bỏ điều kiện lọc theo whitelist ở bước `PERF-020` (xem cập nhật tương ứng ở `08-performance-cpu-spec.md`).
- `BE-072`: Lý do chọn cách này thay vì tiếp tục mở rộng whitelist: danh sách ứng dụng có thể hiển thị nội dung khiêu dâm là vô hạn (video player, ứng dụng nhắn tin hiện ảnh, trình xem PDF, game overlay, ứng dụng portable không có installer...) — duy trì whitelist sẽ luôn đi sau, giống hệt vấn đề "domain thay đổi liên tục" đã phân tích ở giai đoạn đầu dự án. Giám sát theo cửa sổ active giải quyết triệt để vấn đề này.
- `BE-073`: Danh sách ứng dụng **loại trừ khỏi giám sát** (exclude-list) thay vì danh sách được phép (allow-list): các ứng dụng hệ thống không thể hiển thị nội dung web/media (ví dụ Task Manager, File Explorer khi không preview ảnh/video, Settings app...) có thể được loại trừ để tiết kiệm tài nguyên, nhưng đây là tối ưu hoá, không phải yêu cầu bảo mật cốt lõi — nếu không chắc chắn, mặc định vẫn giám sát để tránh bỏ sót.
- `BE-073a` (danh sách khởi điểm — cần verify từng process cụ thể ở System Design trước khi code): Match theo **process name**, chỉ đưa vào exclude-list các ứng dụng hệ thống Windows **không có khả năng hiển thị ảnh/video tuỳ ý** dưới bất kỳ hình thức nào:

| Ứng dụng | Process name | Ghi chú |
|---|---|---|
| Task Manager | `Taskmgr.exe` | — |
| Registry Editor | `regedit.exe` | — |
| Command Prompt | `cmd.exe` (host: `conhost.exe`) | Chỉ text thuần |
| PowerShell | `powershell.exe`, `pwsh.exe` | Chỉ text thuần |
| Windows Terminal | `WindowsTerminal.exe` | An toàn ở bản hiện tại (chưa hỗ trợ render ảnh qua Sixel/terminal graphics) — cần re-check mỗi khi Windows Terminal update, vì đây là tính năng có thể được thêm trong tương lai |
| MMC snap-ins (Task Scheduler, Services, Event Viewer, Device Manager, Disk Management, Group Policy Editor, Local Security Policy...) | `mmc.exe` | Toàn bộ snap-in quản trị hệ thống chuẩn của Windows, không snap-in nào hiển thị media |
| Event Viewer (standalone) | `eventvwr.exe` | — |
| Performance Monitor / Resource Monitor | `perfmon.exe`, `resmon.exe` | — |
| System Configuration | `msconfig.exe` | — |
| System Information | `msinfo32.exe` | — |
| Character Map | `charmap.exe` | — |
| Calculator | `CalculatorApp.exe` | Chỉ chế độ Calculator thuần — không tính khi có tính năng mở rộng hiển thị ảnh trong tương lai |
| Windows Security (Defender UI) | `SecHealthUI.exe` | — |
| Notepad (bản classic) | `notepad.exe` | Chỉ text thuần — cần re-check nếu Microsoft thêm tính năng AI/nhúng ảnh vào Notepad bản mới sau này |
| Lock screen / Login screen | `LogonUI.exe` | Không phải phiên tương tác của user, loại trừ vì lý do khác (không phải session đang giám sát), không phải vì "an toàn nội dung" |

  **Tuyệt đối KHÔNG đưa vào exclude-list** (trông giống ứng dụng hệ thống/tiện ích nhưng có khả năng hiển thị ảnh/video, dễ bị lợi dụng làm lỗ hổng nếu loại trừ nhầm):
  - **File Explorer** (`explorer.exe`) — có preview pane và chế độ xem thumbnail/large icons hiển thị trực tiếp ảnh/video. Muốn loại trừ an toàn cần logic runtime kiểm tra chế độ xem hiện tại (Details/List + preview pane đóng), không thể match tĩnh theo process name — để lại làm quyết định kỹ thuật riêng ở System Design, **không** nằm trong danh sách tĩnh này.
  - **Windows Settings** (`SystemSettings.exe`) — 1 số trang cài đặt hiển thị ảnh (chọn ảnh nền desktop/lock screen), rủi ro thấp nhưng vẫn có khả năng hiển thị ảnh nên không loại trừ toàn bộ process.
  - **Search UI** (`SearchHost.exe`) — kết quả tìm kiếm web có thể kèm ảnh preview.
  - File Office (Word/Excel/PowerPoint/WordPad), Mail/Calendar client, bất kỳ browser, trình xem ảnh/PDF, ứng dụng nhắn tin (Zalo, Messenger, Discord, Teams...), Paint, Photos app, bất kỳ media player nào — tất cả đều **luôn được giám sát**, không có ngoại lệ.
- `BE-074`: Với ứng dụng chạy chế độ **fullscreen exclusive** (một số video player/game dùng chế độ này thay vì borderless fullscreen), Desktop Duplication API có thể gặp giới hạn capture tuỳ driver GPU — cần kiểm thử thực tế trên các player phổ biến (VLC, MPC-HC, Windows Media Player) ở cả chế độ windowed và fullscreen, ghi nhận là rủi ro kỹ thuật cần xác minh sớm ở giai đoạn System Design.
- `BE-075`: Danh sách ứng dụng đã xác nhận cần test kỹ ở Phase 1 (không phải whitelist giới hạn, chỉ là danh sách ưu tiên test):

| Loại ứng dụng | Ví dụ | Ghi chú |
|---|---|---|
| Trình duyệt | Chrome, Edge, Firefox, Brave, Opera | Đầy đủ, đã phân tích ở bản v0.1.0 |
| Video player | VLC, Windows Media Player, PotPlayer, MPC-HC | Cần test riêng chế độ fullscreen exclusive (`BE-074`) |
| Ứng dụng xem ảnh | Windows Photos, IrfanView | Nội dung tĩnh, ít thách thức hơn video |
| Ứng dụng nhắn tin có hiển thị ảnh/video (Discord, Zalo, Messenger desktop...) | — | Cần xác nhận capture đúng vùng preview ảnh trong khung chat, không phải toàn bộ cửa sổ app nếu chỉ 1 phần nhỏ là ảnh |
| Trình duyệt/app portable không cài đặt | — | Không còn là giới hạn vì cách tiếp cận không dựa vào process name/installer |
| Remote desktop / video call (thêm v0.8.0) | Microsoft Remote Desktop Connection (`mstsc.exe`), TeamViewer, AnyDesk, Zoom, Google Meet | Không phải whitelist/exclude riêng — vẫn giám sát bình thường theo `BE-071` như mọi app khác (Vision capture pixel màn hình, không quan tâm nội dung local hay remote). Test riêng vì nhóm này có khả năng cao chạy ở chế độ **fullscreen exclusive** khi remote/share màn hình toàn màn hình — cùng loại rủi ro capture đã ghi nhận ở `BE-074` |

## 7. Multi-monitor & xử lý nhiều cửa sổ vi phạm đồng thời (ĐÃ CHỐT v0.3.0)

### 7.1 Hỗ trợ nhiều màn hình (BE-080)

- `BE-080`: App **bắt buộc hỗ trợ multi-monitor ngay Phase 1** (không giả định 1 màn hình).
- `BE-081`: Desktop Duplication API (DXGI) hoạt động theo từng **output/adapter riêng biệt** — mỗi màn hình vật lý cần 1 phiên capture độc lập (`IDXGIOutputDuplication` riêng cho mỗi `IDXGIOutput`). Vision Engine cần enumerate toàn bộ output khi khởi động và theo dõi sự kiện cắm/rút màn hình (`WM_DISPLAYCHANGE`) để cập nhật danh sách output đang capture.
- `BE-082`: Chiến lược capture: chỉ capture màn hình **có chứa cửa sổ đang cần giám sát** (theo nguyên tắc `BE-071`/`PERF-020`), không capture toàn bộ mọi màn hình liên tục nếu không cần thiết — giữ đúng tinh thần tối ưu hiệu năng đã thống nhất, chỉ mở rộng từ "1 output" sang "N output đang có cửa sổ liên quan".
- `BE-083`: Icon trạng thái giám sát (`BE-033`, `S8`) hiển thị **trên mỗi màn hình** (không chỉ màn hình chính) để đảm bảo tính minh bạch dù trẻ đang thao tác ở màn hình phụ nào.

### 7.2 Xử lý nhiều cửa sổ vi phạm cùng lúc (BE-084)

- `BE-084`: **Mỗi cửa sổ vi phạm nhận 1 overlay blur riêng** (đã mô tả chi tiết ở `BE-030`/`FE-016`). Không gộp nhiều cửa sổ vi phạm vào 1 overlay lớn, kể cả khi chúng ở cạnh nhau hoặc thuộc cùng 1 ứng dụng (ví dụ 2 tab/2 cửa sổ Chrome khác nhau đều vi phạm cùng lúc → 2 overlay độc lập).
- `BE-085`: Mỗi overlay có nút "Tắt nội dung" riêng, hoạt động độc lập — người dùng có thể xử lý lần lượt từng cửa sổ, thứ tự không bắt buộc.
- `BE-086`: Vision Engine xử lý tuần tự (không song song) các cửa sổ cần phân tích trong cùng 1 chu kỳ để tránh tăng đột biến CPU (giữ nguyên nguyên tắc đã có ở `IMG-020`), nhưng vẫn đảm bảo mỗi cửa sổ vi phạm được gán overlay riêng ngay khi có kết quả — không chờ xử lý xong toàn bộ danh sách mới hiển thị overlay đầu tiên (xử lý xong cửa sổ nào, hiện overlay cửa sổ đó ngay).
- `BE-087` (ĐÃ CHỐT v0.9.0): **Thứ tự z-index giữa các overlay khi chồng lấn** trên cùng 1 màn hình: overlay của cửa sổ vi phạm nào đang ở **z-index cao hơn** (đang che các cửa sổ vi phạm khác) thì overlay tương ứng của nó cũng phải có z-index cao hơn overlay của các cửa sổ vi phạm bị che — tức z-index của overlay **luôn khớp với z-index của cửa sổ vi phạm gốc bên dưới nó**, không đảo ngược thứ tự. Nhờ vậy overlay không bao giờ vô tình che mất nút bấm/vùng loại trừ (`FE-016`) của overlay khác đang thực sự ở trên.
- `BE-088` (ĐÃ CHỐT v0.9.0): Giới hạn tối đa **10 overlay đồng thời** (tính tổng trên toàn hệ thống, gộp tất cả màn hình — liên kết benchmark `PERF-060` ở `08-performance-cpu-spec.md`). Nếu số cửa sổ vi phạm đồng thời vượt quá 10, hệ thống chuyển sang **chế độ overlay gộp theo màn hình** (degraded mode): mỗi màn hình chỉ hiển thị **1 overlay duy nhất** che toàn bộ màn hình đó, thay vì overlay riêng cho từng cửa sổ vi phạm — tạm ngưng áp dụng nguyên tắc "mỗi cửa sổ 1 overlay" (`BE-084`) trong trạng thái quá tải này để bảo vệ hiệu năng.
- `BE-088a` (ĐÃ CHỐT v0.12.0, 2026-09-19 — làm rõ/bổ sung cho `BE-088`, không đổi ngưỡng kích hoạt 10 overlay đã chốt ở `BE-088`): Phát hiện gap khi viết `Architecture/07-overlay-architecture.md` (Đợt 2) — cơ chế vùng loại trừ nút đóng 3 lớp (`FE-016`, `03-frontend-ui-spec.md`) giả định ánh xạ 1 overlay ↔ 1 cửa sổ, nhưng chế độ gộp phá vỡ ánh xạ này. Chủ dự án chốt trực tiếp qua 2 vòng hỏi đáp (2026-09-19): overlay ở chế độ gộp (`BE-088`) là **full-screen lock** — che phủ **toàn bộ màn hình** đó, không khoanh vùng riêng theo từng cửa sổ vi phạm như chế độ thường (`FE-010`/`BE-084`), và **không chừa vùng loại trừ nút đóng nào** của các cửa sổ gốc bên dưới. Đây là phản ứng quyết liệt hơn có chủ đích khi người dùng cố tình mở vượt ngưỡng >10 cửa sổ vi phạm cùng lúc. Hệ quả trực tiếp: `FE-016` (và các lớp con `FE-016a`-`FE-016e`) **không áp dụng** trong chế độ gộp — xem chi tiết hành vi UI + lý do thiết kế đầy đủ ở `FE-016f` (`03-frontend-ui-spec.md`).
- ~~`BE-089` (ĐÃ CHỐT v0.9.0): Trong chế độ overlay gộp (`BE-088`), nút "Tắt nội dung" (`FE-015`) trên overlay của **bất kỳ màn hình nào** sẽ force-close **toàn bộ danh sách cửa sổ đang vi phạm cùng lúc trên toàn hệ thống** (không chỉ riêng màn hình chứa overlay đó) — vì ở chế độ này 1 overlay không còn ánh xạ 1:1 với 1 cửa sổ cụ thể nên không thể đóng chọn lọc từng cửa sổ, hành động buộc phải mang tính toàn cục.~~ — **DEPRECATED, superseded by `BE-089a`** (làm rõ overlay gộp là full-screen lock với đúng 1 nút duy nhất, không phải nhiều overlay mỗi cái đều có nút riêng như ngụ ý cách viết cũ).
- `BE-089a` (ĐÃ CHỐT v0.12.0, 2026-09-19, supersedes `BE-089`): Trên overlay full-screen lock của chế độ gộp (`BE-088a`), mỗi màn hình chỉ hiển thị **đúng 1 nút "Tắt nội dung" duy nhất** (khác hẳn chế độ thường ở `BE-085`, nơi mỗi cửa sổ có nút riêng). Bấm nút này gửi lệnh đóng đồng thời — theo đúng cơ chế đã làm rõ ở `BE-032` (v0.11.3): `Overlay` tự `PostMessage(WM_CLOSE)` cục bộ lên từng window handle + gửi `ForceCloseRequest` lên `Service` để cập nhật state/audit log — tới **toàn bộ danh sách cửa sổ đang vi phạm bị gộp cùng lúc trên toàn hệ thống**, không chỉ riêng màn hình chứa overlay vừa bấm (giữ nguyên phạm vi "toàn hệ thống" đã chốt ở `BE-089` gốc, chỉ làm rõ lại ngữ cảnh 1-overlay-1-nút của full-screen lock).
- `BE-089b` (ĐÃ CHỐT v0.13.0, 2026-09-19 — bổ sung lối thoát dự phòng thứ 2 cho `GEN-007a`, chốt qua 2 vòng hỏi đáp trực tiếp với chủ dự án): Bối cảnh — sau khi `BE-088a`/`BE-089a` chốt overlay full-screen lock chỉ có **đúng 1 nút "Tắt nội dung" duy nhất**, session điều phối chỉ ra đây tạo ngoại lệ hoàn toàn cho `GEN-007` (nguyên tắc luôn có ≥2 cách thoát độc lập, tránh false-positive khiến người dùng bị kẹt). Hỏi lại chủ dự án, chủ dự án chọn phương án **thêm timeout tự động gỡ khoá** làm lối thoát dự phòng thứ 2 (không phải mật khẩu, không giữ nguyên chỉ-1-nút). Hành vi cụ thể: nếu overlay full-screen lock (`BE-088a`) tồn tại liên tục quá **30 giây** kể từ thời điểm hiển thị mà chưa có bất kỳ `ForceCloseRequest` nào được gửi lên `Service` (tức chưa ai bấm nút "Tắt nội dung" — `BE-089a`), hệ thống **tự động thực hiện đúng luồng force-close** như thao tác thủ công: `Overlay` tự đếm giờ cục bộ (không phụ thuộc round-trip IPC tới `Service` để tránh trễ), khi hết 30 giây tự gọi `PostMessage(WM_CLOSE)` lên toàn bộ window handle của các cửa sổ vi phạm bị gộp + tự gửi `ForceCloseRequest` lên `Service` để cập nhật state/audit log — **không cần mật khẩu, không cần xác nhận thêm**. Mỗi overlay full-screen lock (1 overlay/màn hình, theo `BE-088`) tự đếm giờ độc lập kể từ lúc chính nó hiển thị; overlay nào hết hạn 30 giây trước sẽ kích hoạt force-close **toàn bộ** (cùng phạm vi "toàn hệ thống" như nút bấm thủ công ở `BE-089a`), không chờ đồng bộ với overlay ở màn hình khác. Đây là lối thoát độc lập thứ 2 (ngoài nút bấm thủ công) — giữ đúng tinh thần `GEN-007` (luôn có ≥2 cách thoát độc lập) dù cơ chế khác biệt: tự động theo thời gian thay vì 1 hành động thủ công thứ 2. **Yêu cầu audit log quan trọng**: sự kiện force-close ghi vào audit log (`BE-014`) phải phân biệt rõ nguồn gốc kích hoạt — `"manual"` (người dùng bấm nút "Tắt nội dung") vs `"auto-timeout"` (hệ thống tự động sau 30 giây không thao tác) — đây là dữ liệu quan trọng để chủ dự án theo dõi sau này tần suất kích hoạt chế độ gộp và mức độ người dùng chủ động xử lý. Đồng bộ với `03-frontend-ui-spec.md` (`FE-016g`) và `00-INDEX.md` (`GEN-007a` cập nhật/`GEN-007b`).

## 8. Ngưỡng risk score (ĐÃ CHỐT v0.3.0)

- `BE-090`: Giá trị ngưỡng risk score mặc định **do đội phát triển quyết định trong quá trình phát triển/benchmark thực tế** (dựa trên bộ dataset test đề cập ở `09-image-processing-spec.md`), không phải giá trị cố định ngay từ bước spec này.
- `BE-091`: Ngưỡng này **không phơi ra cho phụ huynh tự chỉnh trực tiếp** ở Phase 1, đúng theo lo ngại rủi ro đã nêu (chỉnh sai làm giảm hiệu quả bảo vệ) — vì vậy mục "Ngưỡng nhạy cảm" trong Cài đặt nâng cao (`S4`) và bước chọn mức độ nhạy cảm trong Onboarding (`FE-030`/`FE-031` cũ) được **loại bỏ khỏi UI phase 1** (xem cập nhật tương ứng ở `03-frontend-ui-spec.md`). Nếu sau này có nhu cầu cho phép tinh chỉnh, cần thiết kế lại có kiểm soát (ví dụ chỉ 2-3 mức đã kiểm định sẵn thay vì thanh trượt tự do) — để ngỏ cho Phase 2, không quyết định trong spec này.

## 9. Câu hỏi mở (cần chốt trước khi sang System Design)

_Hiện không còn câu hỏi mở nào trong file này._

## 10. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.13.0 | 2026-09-19 | **MINOR — thêm `BE-089b`**: bổ sung lối thoát dự phòng thứ 2 (auto-timeout) cho quyết định `BE-088a`/`BE-089a` (full-screen lock chỉ 1 nút duy nhất) — phát sinh khi session điều phối chỉ ra quyết định trước tạo ngoại lệ hoàn toàn cho `GEN-007` (luôn có ≥2 cách thoát độc lập). Hỏi lại chủ dự án qua 2 vòng, chủ dự án chọn: nếu overlay full-screen lock tồn tại quá **30 giây** không ai bấm nút, hệ thống **tự động force-close** toàn bộ cửa sổ vi phạm bị gộp + gỡ khoá overlay theo đúng cơ chế `WM_CLOSE`/`ForceCloseRequest` như bấm nút thủ công, không cần mật khẩu. Audit log bắt buộc phân biệt nguồn `"manual"` vs `"auto-timeout"`. Đồng bộ với `03-frontend-ui-spec.md` → v0.9.0 (`FE-016g`) và `00-INDEX.md` (`GEN-007a`/`GEN-007b`). Archive: `Specification/Outdated/02-backend-spec__v0.12.0__2026-09-19.md` |
| v0.12.0 | 2026-09-19 | **MINOR — `BE-088a` (mới) + `BE-089a` (supersedes `BE-089`)**: phát sinh khi `architecture-writer` viết `Architecture/07-overlay-architecture.md` (Đợt 2), phát hiện gap — `FE-016` (vùng loại trừ nút đóng 3 lớp) giả định ánh xạ 1 overlay ↔ 1 cửa sổ, bị phá vỡ ở chế độ gộp (`BE-088`). Chủ dự án chốt trực tiếp: overlay chế độ gộp chuyển thành **full-screen lock** (che toàn màn hình, không khoanh vùng riêng từng cửa sổ vi phạm), chỉ có **đúng 1 nút "Tắt nội dung" duy nhất** đóng toàn bộ cửa sổ vi phạm bị gộp cùng lúc (`BE-089a`). `FE-016` **không áp dụng** trong chế độ này — chủ đích thiết kế, không phải thiếu sót, vì mục tiêu là phản ứng quyết liệt hơn khi người dùng cố tình mở vượt ngưỡng >10 cửa sổ vi phạm. `BE-089` cũ đánh dấu `DEPRECATED`. Đồng bộ với `03-frontend-ui-spec.md` → v0.8.0 (`FE-016f`) và `00-INDEX.md` (`GEN-007a`). Archive: `Specification/Outdated/02-backend-spec__v0.11.3__2026-09-19.md` |
| v0.11.3 | 2026-09-18 | **PATCH — làm rõ câu chữ `BE-032`, không đổi hành vi sản phẩm**: sửa mô tả "Service thực hiện force-close" thành đúng thực tế vật lý — `Overlay` (đã ở session tương tác) tự gọi `PostMessage(WM_CLOSE)` cục bộ lên window handle, đồng thời gửi `ForceCloseRequest` lên `Service` để `Service` cập nhật state (danh sách overlay active) + ghi audit log. Lý do: `Service` chạy Session 0 (`BE-023a`) không có quyền gọi API `user32` lên HWND thuộc session tương tác. Phát hiện khi `feature-dev`/`architecture-writer` implement Đợt 1, đồng bộ theo `Architecture/02-process-architecture.md` v0.1.1. Đồng thời cập nhật lại data flow ở mục 5 cho khớp. Không tạo requirement mới/không supersedes vì kết quả hành vi cuối cùng người dùng thấy không đổi — chỉ là làm rõ ai gọi API OS. Archive bản cũ ở `Specification/Outdated/02-backend-spec__v0.11.2__2026-09-18.md` |
| v0.11.2 | 2026-09-17 | **PATCH — làm rõ câu chữ, không đổi ý nghĩa yêu cầu**: sửa dòng "Audit log" ở bảng lưu trữ dữ liệu local (mục 4) — trước ghi mã hoá "DPAPI + hash chain" mâu thuẫn với `SEC-041` ở `04-security-spec.md` (audit log KHÔNG mã hoá nội dung, chỉ dùng hash-chain đảm bảo toàn vẹn). Phát hiện khi `architecture-writer` viết `Architecture/04-data-architecture.md`, chủ dự án xác nhận giữ theo `SEC-041`. Đã archive bản cũ vào `Specification/Outdated/02-backend-spec__v0.11.1__2026-09-17.md` |
| v0.11.1 | 2026-09-17 | Chủ dự án approve toàn bộ requirement trong file này — chuyển trạng thái file từ `Draft` sang `Approved` |
| v0.11.0 | 2026-09-17 | Cập nhật bảng module (mục 1): loại bỏ ngoại lệ network cho `Service`/`UI` (auto-update) — `MISC-020` bị REJECTED ở `10-additional-mechanisms-spec.md`, toàn bộ app giờ **tuyệt đối zero network**, không ngoại lệ nào |
| v0.10.0 | 2026-09-17 | Thêm `BE-061a`/`BE-061b` — trả lời câu hỏi mở tương ứng ở `05-anti-uninstall-tamper-spec.md`: sau khi fallback cấu hình mặc định, `Service` tự động ghi đè `config.db`, đồng thời gửi Windows Toast Notification qua Overlay process cảnh báo phụ huynh cấu hình không còn toàn vẹn |
| v0.9.0 | 2026-09-17 | **Chốt 2 câu hỏi mở cuối cùng của file**: `BE-087` — z-index overlay khớp z-index cửa sổ vi phạm gốc bên dưới. `BE-088` — giới hạn tối đa 10 overlay đồng thời (toàn hệ thống), vượt quá thì chuyển sang overlay gộp theo màn hình. `BE-089` — trong chế độ gộp, nút "Tắt nội dung" ở bất kỳ overlay nào force-close toàn bộ cửa sổ vi phạm trên toàn hệ thống. File này không còn câu hỏi mở |
| v0.8.0 | 2026-09-17 | Thêm dòng "Remote desktop/video call" vào bảng test ưu tiên `BE-075` (mstsc, TeamViewer, AnyDesk, Zoom, Google Meet) — nhóm này vẫn giám sát bình thường như mọi app (không phải whitelist/exclude riêng), chỉ cần test thêm vì khả năng cao chạy fullscreen exclusive, cùng loại rủi ro capture đã ghi ở `BE-074` |
| v0.7.0 | 2026-09-17 | Thêm `BE-073a` — danh sách khởi điểm cụ thể các process được đưa vào exclude-list (Task Manager, PowerShell, MMC snap-ins, Notepad classic...), kèm danh sách tường minh các ứng dụng **không được** đưa vào exclude-list dù trông giống tiện ích hệ thống (File Explorer, Settings, Search UI...) vì có khả năng hiển thị ảnh/video. Đánh dấu cần verify từng process cụ thể ở System Design trước khi code |
| v0.6.0 | 2026-09-17 | **Chốt**: thêm `BE-061` — nếu `Service` không đọc được/giải mã được `config.db` (hỏng, bị sửa trái phép), tự động fallback dùng bộ cấu hình mặc định hard-code trong code, fail-secure (giám sát luôn BẬT), không được dừng hoạt động; ghi audit log sự kiện. Liên kết 2 chiều với `ANTI-070` ở `05-anti-uninstall-tamper-spec.md` |
| v0.5.0 | 2026-09-17 | **Quan trọng — sửa lỗi kiến trúc tiềm ẩn**: bổ sung `BE-023a`/`BE-023b` — `Vision` phải được `Service` khởi chạy vào đúng session tương tác của user qua `WTSQueryUserToken` + `CreateProcessAsUser` (không dùng `CreateProcess` thông thường), do Session 0 Isolation của Windows khiến Service (Session 0) không thể spawn process có quyền dùng Desktop Duplication API nếu không xử lý đúng. Cập nhật bảng kiến trúc tổng thể (mục 1) và mô tả module Vision (2.2) để phản ánh đúng |
| v0.4.0 | 2026-09-17 | Thêm nguyên tắc single-purpose tường minh cho `Vision` (2.2), liên kết `SEC-017` ở `04-security-spec.md` — không network/registry/file write/spawn process/giao tiếp trực tiếp module khác |
| v0.3.0 | 2026-09-17 | **Chốt**: bắt buộc hỗ trợ multi-monitor (`BE-080` đến `BE-083`); mỗi cửa sổ vi phạm có overlay riêng, xử lý được nhiều cửa sổ đồng thời (`BE-084` đến `BE-086`); ngưỡng risk score do đội dev quyết định trong quá trình phát triển, không phơi ra UI cho phụ huynh chỉnh ở Phase 1 (`BE-090`, `BE-091`). Cập nhật module Overlay (`BE-030` đến `BE-033`) để hỗ trợ nhiều instance |
| v0.2.0 | 2026-09-17 | Thay whitelist browser (`BE-070` cũ) bằng chiến lược giám sát theo cửa sổ active bất kỳ (`BE-071` đến `BE-075`); cập nhật data flow để phản ánh phạm vi mở rộng |
| v0.1.0 | 2026-09-17 | Khởi tạo |

# 06 — Security Architecture

> Version: v0.1.3 | Trạng thái: Approved | Cập nhật: 2026-09-19

## 1. Mục đích

File này trả lời **HOW** cho phần bảo mật tầng hệ điều hành chưa được chi tiết hoá ở các file trước: Windows token/Integrity Level cho `Vision`/`Overlay` (mở rộng `BE-023a`/`BE-023b`), luật WFP chặn network cho `Vision` (`SEC-010`, `SEC-016`–`018`), ACL cụ thể cho toàn bộ file/thư mục/registry (hệ thống hoá layout đã có ở `04-data-architecture.md` mục 2 và `ANTI-030`/`031`), và cách áp dụng DPAPI cụ thể (`SEC-040`/`040a`). Không phát minh yêu cầu sản phẩm mới — mọi quyết định trích dẫn ngược `Specification/04-security-spec.md`, `05-anti-uninstall-tamper-spec.md`, `02-backend-spec.md`, hoặc là ADR thuần kỹ thuật.

File này **không** thiết kế lại: (a) ACL/xác thực chữ ký/HMAC của Named Pipe — đã chốt ở `03-ipc-communication.md` mục 2/4/5, chỉ bổ sung 1 hệ quả kỹ thuật còn thiếu (mục 2.5); (b) chi tiết Dual Watchdog, giám sát thay đổi registry bất thường, custom uninstaller — thuộc `09-anti-tamper-architecture.md`, ở đây chỉ định nghĩa **chính sách ACL nền tảng** mà `09` sẽ tái sử dụng; (c) crash dump policy (`SEC-020`) — chưa trong phạm vi Đợt 0 theo `ROADMAP.md`, để ở file phù hợp khi đến Đợt 8.

Ghi chú traceability quan trọng: `Specification/04-security-spec.md` mục 10 có 1 câu hỏi mở còn hiệu lực — *"Định nghĩa cụ thể 'restricted token' ở mức tối thiểu Phase 1 (SEC-010/SEC-016) — dùng Windows Job Object + restricted token cổ điển, hay có cơ chế nhẹ hơn khác? Quyết định kỹ thuật cụ thể ở System Design."* — spec đã chủ động **uỷ quyền quyết định này cho tài liệu kiến trúc**, nên mục 2 dưới đây chính là câu trả lời chính thức, không cần quay lại sửa spec (khác với các trường hợp phải quay lại sửa spec vì thiếu quyết định WHAT).

## 2. Windows Token & Integrity Level cho `Vision`/`Overlay`

### 2.1 Trình tự đầy đủ (chi tiết hoá HOW cho `BE-023a` 3 bước đã chốt)

`BE-023a` đã chốt 3 bước bắt buộc (`WTSQueryUserToken` → `DuplicateTokenEx` → `CreateProcessAsUser`). File này chèn thêm 2 bước hardening token **giữa** bước 2 và 3, không thay đổi thứ tự tổng thể đã chốt:

```
1. Service (SYSTEM, Session 0): WTSGetActiveConsoleSessionId() → sessionId
2. Service: WTSQueryUserToken(sessionId) → hUserToken
   (token primary của user đang đăng nhập ở session tương tác đang active)
3. Service: DuplicateTokenEx(hUserToken, TOKEN_ALL_ACCESS, NULL,
      SecurityImpersonation, TokenPrimary) → hDupToken
   (cần TOKEN_ALL_ACCESS để 2 bước sau có quyền chỉnh sửa token;
    TokenPrimary vì CreateProcessAsUser ở bước 6 yêu cầu primary token)
4. Service: CreateRestrictedToken(hDupToken,
      Flags = DISABLE_MAX_PRIVILEGE,
      SidsToDisable = [ BUILTIN\Administrators ]   // nếu có mặt trong token gốc — phòng hờ
                                                     // trường hợp phụ huynh dùng chung 1 account
                                                     // Administrator vừa để giám sát vừa bị giám sát
   ) → hRestrictedToken
5. Service: SetTokenInformation(hRestrictedToken, TokenIntegrityLevel,
      SID "S-1-16-4096" /* Low Mandatory Level */) 
6. Service: CreateProcessAsUser(hRestrictedToken, "<installdir>\ParentalGuard.Vision.exe", ...,
      lpStartupInfo = STARTUPINFOEX kèm handle list anonymous pipe kế thừa
      (bootstrap khoá HMAC — 03-ipc-communication.md mục 5.2))
7. Service: đóng hUserToken/hDupToken/hRestrictedToken ngay sau khi CreateProcessAsUser
   trả về thành công — không giữ lại lâu hơn cần thiết trong bộ nhớ Service.
```

- `Overlay` dùng **đúng pipeline 7 bước này**, không rút gọn — điểm khác biệt duy nhất giữa 2 tiến trình nằm ở phạm vi tài nguyên được ACL cho phép truy cập (mục 4), không nằm ở cách hardening token (giống hệt nhau).
- Bước 4/5 áp dụng cho **cả 2 tiến trình `Vision` và `Overlay`** — đúng bảng quyền ở `01-tong-quan-kien-truc.md` mục 3 ("User session, quyền hạn chế" / "quyền thấp"): cả hai đều thấp hơn user session bình thường, `Overlay` "thấp hơn" `Vision` thể hiện ở việc `Overlay` không được cấp Read trên `models\*.onnx` (mục 4.1) và không có lý do nghiệp vụ nào để mở bất kỳ file nào ngoài chính binary của nó.

### 2.2 Quyết định: Restricted Token cổ điển + Low Integrity Level, KHÔNG dùng AppContainer

| # | Phương án | Đánh giá |
|---|---|---|
| A | AppContainer đầy đủ | `SEC-015` (Approved) đã hạ AppContainer xuống backlog/optional cho `Vision` — lý do gốc (phòng "ảnh bẫy" khai thác decoder) không áp dụng vì kiến trúc capture pixel thô qua DXGI (`SEC-013`/`014`). Thêm 2 rủi ro kỹ thuật củng cố quyết định không chọn A ở Đợt 0: (1) tương thích DXGI Desktop Duplication API với AppContainer chưa được kiểm chứng, có rủi ro tài liệu cộng đồng ghi nhận Desktop Duplication API gặp trở ngại khi chạy trong tiến trình AppContainer do driver GPU/desktop access bị giới hạn thêm 1 lớp; (2) app .NET không đóng gói dạng MSIX/UWP packaged app (đã chốt dùng WiX/MSI truyền thống theo hướng installer ở `ROADMAP.md`) khiến việc chạy `CreateProcess` với `SECURITY_CAPABILITIES` + khai báo capability cho AppContainer là kỹ thuật nâng cao, ít tài liệu/tooling hỗ trợ cho .NET, rủi ro implement sai cao hơn lợi ích mang lại ở quy mô dự án cộng đồng 1 người maintain (nhất quán tinh thần `ADR-09`). |
| B (chọn) | Restricted Token cổ điển (`CreateRestrictedToken` + `DISABLE_MAX_PRIVILEGE` + disable SID Administrators) + hạ Integrity Level xuống **Low** | Kỹ thuật đã được kiểm chứng rộng rãi (Internet Explorer Protected Mode, Chrome sandbox renderer từng dùng chính kỹ thuật này nhiều năm trước khi có AppContainer) — MIC (Mandatory Integrity Control) mặc định chỉ chặn **write-up** (tiến trình Low không ghi được vào object có nhãn Medium+ — tức hầu hết registry/file trong profile user, đạt đúng mục tiêu least-privilege cho 1 tiến trình về nguyên tắc không cần ghi gì ngoài không ghi gì cả theo `SEC-017`), trong khi vẫn cho phép **đọc** bình thường (không set no-read-up) nên không phá vỡ khả năng load DLL hệ thống/đọc model `.onnx`/nhận input chuột trên chính cửa sổ của `Overlay`. |
| C | `Untrusted` Integrity Level (thấp hơn Low) | Không chọn — mức này thường dùng cho tiến trình hiển thị nội dung không tin cậy tuyệt đối (ví dụ content renderer của trình duyệt), có rủi ro phá vỡ chức năng UI cơ bản của `Overlay` (topmost window, nhận click) mà không có lợi ích bảo mật tương xứng thêm so với Low trong ngữ cảnh ứng dụng này. |
| D | Chỉ Restricted Token, giữ nguyên Medium IL | Không chọn làm phương án chính — Medium IL là mức mặc định của hầu hết tiến trình user thường, không tạo thêm rào cản write-up nào; Low IL (phương án B) đạt least-privilege cao hơn với chi phí kỹ thuật ngang nhau, nên ưu tiên B. Giữ D làm **phương án fallback** duy nhất nếu B gặp vấn đề tương thích thực nghiệm ở Đợt 1 (mục 7 câu hỏi mở). |

- Không dùng thêm kỹ thuật "Restricted SIDs" (`CreateRestrictedToken` tham số `SidsToRestrict`, ép kiểm tra ACL kép) dù đây cũng là 1 lựa chọn kinh điển — quyết định loại trừ có chủ đích (ADR-30 mục 6): kỹ thuật này dễ làm gãy các quyền truy cập ngầm định mà .NET runtime/Windows cần để tự vận hành (load DLL hệ thống, truy cập tài nguyên COM nội bộ...), rủi ro cao hơn lợi ích, khó chẩn đoán lỗi cho 1 dự án cộng đồng không có đội QA lớn để test trên nhiều cấu hình Windows — `DISABLE_MAX_PRIVILEGE` + Low IL đã đủ đạt least-privilege thực chất (loại toàn bộ privilege nhạy cảm + chặn ghi lên tài nguyên Medium+) mà không có rủi ro này.
- **Rủi ro còn tồn đọng cần ghi nhận minh bạch**: MIC mặc định **không chặn đọc** (no-read-up không được bật) — 1 `Vision`/`Overlay` bị compromise về lý thuyết vẫn đọc được file khác trong profile user (tài liệu, cookie trình duyệt...) dù không network hoá được ra ngoài. Đây là rủi ro dư chấp nhận được vì 2 lý do: (1) không có đường thoát dữ liệu — `SEC-010`/`016` đã chặn network ở tầng OS (mục 3), Low IL đã chặn ghi (không thể tạo file thả payload ở nơi khác để duy trì/lan truyền); (2) `SEC-014` đã phân tích supply-chain risk là rủi ro chung cho mọi process trong hệ thống, không phải lý do riêng để đầu tư sandbox hoá `Vision` nặng hơn các module khác. Không đầu tư thêm biện pháp no-read-up toàn hệ thống (không khả thi, phải relabel toàn bộ profile user).

### 2.3 Cấu hình cụ thể — `Vision`

- Token: theo pipeline mục 2.1 (Low IL, restricted, disable Administrators SID).
- Tài nguyên được phép truy cập (qua ACL, mục 4): `<installdir>\ParentalGuard.Vision.exe` (Read+Execute, kế thừa quyền chạy chuẩn của mọi executable), `<installdir>\models\*.onnx` (Read — duy nhất file dữ liệu được phép đọc ngoài chính binary, đúng `SEC-017`), pipe `ParentalGuard.Svc.Vision` (Read+Write qua ACL đã có ở `03` + Mandatory Label mục 2.5).
- Không có quyền nào khác ngoài danh sách trên — đây là **whitelist**, không phải "cấm những gì nghĩ ra được" (đúng nguyên tắc default-deny của `DISABLE_MAX_PRIVILEGE`).

### 2.4 Cấu hình cụ thể — `Overlay`

- Token: giống hệt pipeline mục 2.1.
- Tài nguyên được phép truy cập: `<installdir>\ParentalGuard.Overlay.exe` (Read+Execute), pipe `ParentalGuard.Svc.Overlay` (Read+Write + Mandatory Label mục 2.5). **Không** có quyền Read lên `models\*.onnx` (không có lý do nghiệp vụ — `Overlay` chỉ vẽ rect nhận từ `Service`, không đụng ảnh/model, đúng `02-process-architecture.md` mục 5.1). Đây chính là điểm hiện thực hoá "quyền thấp hơn Vision" đã ghi ở `01-tong-quan-kien-truc.md` mục 3.
- Cần quyền Windows tiêu chuẩn để tạo topmost window, vẽ, và hiển thị Toast Notification (không phải "quyền" theo nghĩa ACL — đây là API OS thông thường mọi tiến trình Low IL vẫn gọi được, không bị hardening ở trên chặn).

### 2.5 Hệ quả bắt buộc lên Named Pipe do Integrity Level thấp (bổ sung kỹ thuật cho `03-ipc-communication.md`, không thiết kế lại)

Đây là điểm mà `03-ipc-communication.md` chưa xử lý vì được viết trước khi quyết định Low IL ở mục 2.2 — cần bổ sung để tránh gãy IPC khi code thật:

- Windows Mandatory Integrity Control áp dụng chính sách **no-write-up** mặc định: 1 tiến trình Low IL **không ghi được** vào kernel object (bao gồm Named Pipe) có nhãn toàn vẹn Medium trở lên. `Service` chạy LocalSystem tạo pipe server sẽ khiến pipe instance đó mặc định có nhãn ngầm định Medium (không có SACL nhãn tường minh) — nếu giữ nguyên, `Vision`/`Overlay` (Low IL) sẽ **không gửi được bất kỳ message nào** (kể cả `Hello`/`HeartbeatPing`) tới `Service` qua 2 pipe `ParentalGuard.Svc.Vision`/`ParentalGuard.Svc.Overlay` — vỡ toàn bộ IPC.
- **Biện pháp bắt buộc (ADR-31)**: ngay sau khi `Service` tạo mỗi `NamedPipeServerStream` instance cho pipe `Vision`/`Overlay` (bao gồm cả lúc recreate instance theo `03-ipc-communication.md` mục 2.2 khi active console session đổi — áp dụng lại mỗi lần tạo mới, thuộc tính này không tự kế thừa qua các lần tạo lại), `Service` gọi `SetKernelObjectSecurity(pipeHandle, LABEL_SECURITY_INFORMATION, sd)` với SACL chỉ chứa 1 Mandatory Label ACE hạ nhãn chính pipe đó xuống Low với cờ no-write-up (biểu diễn SDDL: `S:(ML;;NW;;;LW)`) — **chỉ hạ nhãn của bản thân pipe object**, không đụng tới Integrity Level của tiến trình `Service` (vẫn giữ nguyên System/LocalSystem). Đây là kỹ thuật độc lập, bổ sung thêm 1 lớp (Mandatory Label) **song song** với DACL đã chốt ở `03` mục 2.2 (Allow SID cụ thể + Deny Everyone/ANONYMOUS/Guests) — 2 lớp không thay thế nhau, message vẫn phải qua đủ cả DACL (ai được connect) lẫn Mandatory Label (ai được ghi) để thành công.
- **Pipe `UI` không cần bổ sung này**: `ParentalGuard.UI` chạy đúng Integrity Level bình thường của phiên đăng nhập phụ huynh (Medium, hoặc High nếu phụ huynh chủ động "Run as Administrator" — không bắt buộc) — không hạ IL, nên ghi vào pipe nhãn Medium mặc định vẫn thành công bình thường, không cần chỉnh SACL.
- **Anonymous pipe bootstrap khoá HMAC (`03` mục 5.2) không bị ảnh hưởng**: chiều dữ liệu duy nhất trên kênh này là `Service` ghi, `Vision`/`Overlay` chỉ **đọc** — MIC mặc định không chặn read-up (chỉ chặn write-up), nên tiến trình Low IL đọc được bình thường dữ liệu do `Service` (nhãn cao hơn) ghi trước đó mà không cần chỉnh SACL gì thêm.

## 3. WFP — chặn network cho `Vision` (`SEC-010`, `SEC-016`–`018`)

### 3.1 Kiến trúc filter

- Dùng **Windows Filtering Platform** API gốc (`Fwpuclnt.dll` — `FwpmEngineOpen0`, `FwpmProviderAdd0`, `FwpmSubLayerAdd0`, `FwpmFilterAdd0`...), không dùng lớp API bậc cao "Windows Firewall with Advanced Security" (`INetFwPolicy2`/`netsh advfirewall`) — đúng nghĩa đen `SEC-010` ("Windows Filtering Platform"), đồng thời tránh phụ thuộc vào trạng thái bật/tắt của Windows Firewall service (WFP hoạt động độc lập với việc Firewall service có đang bật hay không, vì đây chính là nền tảng mà Firewall cũng xây trên đó — bền vững hơn).
- **Provider + Sublayer riêng của dự án** (2 GUID hằng số nhúng trong `Service`, ví dụ `PG_WFP_PROVIDER_KEY`/`PG_WFP_SUBLAYER_KEY`) — không thêm filter vào sublayer mặc định của hệ thống, để: (a) dễ liệt kê/gỡ đúng đối tượng của `ParentalGuard` lúc uninstall (chi tiết gỡ ở `09-anti-tamper-architecture.md`, không thiết kế lại ở đây), (b) tránh xung đột thứ tự đánh giá (weight) với filter của phần mềm khác.
- **Layer**: chặn ở tầng ALE (Application Layer Enforcement), **cả 2 chiều**:
  - `FWPM_LAYER_ALE_AUTH_CONNECT_V4` / `_V6` — chặn outbound connect (TCP handshake + UDP send, bao gồm cả truy vấn DNS) — đúng yêu cầu tường minh `SEC-010` ("chặn toàn bộ outbound traffic").
  - `FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4` / `_V6` — chặn luôn khả năng `Vision` nhận inbound connection (listen/accept) — mở rộng nhẹ ngoài chữ nghĩa "outbound" của `SEC-010` nhưng biện minh trực tiếp bởi `SEC-017` ("không network dưới bất kỳ hình thức nào") và `SEC-001a` (zero network tuyệt đối, không ngoại lệ) — chi phí thêm gần như bằng 0 (thêm 2 filter cùng cơ chế), không phải phát minh yêu cầu sản phẩm mới, chỉ là hiện thực hoá triệt để hơn 1 nguyên tắc đã `APPROVED`.
- **Điều kiện filter**: `FWPM_CONDITION_ALE_APP_ID` — giá trị lấy qua `FwpmGetAppIdFromFileName0(<đường dẫn đầy đủ đã cài đặt của ParentalGuard.Vision.exe>)` tính **tại runtime** lúc `Service` khởi động (không hard-code path — khớp đường dẫn cài đặt thực tế), match theo cả đường dẫn lẫn hash nội dung file tại thời điểm đó (App ID của WFP tự bao gồm cơ chế này, không cần thêm điều kiện chữ ký riêng).
- **Action**: `FWP_ACTION_BLOCK`.
- **Weight**: đặt tường minh (không dùng auto-weight), giá trị `FWP_UINT8 = 15` (mức cao nhất trong thang trọng số tự động 0-15 của layer ALE) — đảm bảo luật này luôn được đánh giá trước các luật mặc định khác cùng layer (phần lớn ở weight thấp hơn).
- **Persistence**: toàn bộ Provider/Sublayer/Filter tạo với cờ persistent tương ứng (`FWPM_PROVIDER_FLAG_PERSISTENT`, `FWPM_SUBLAYER_FLAG_PERSISTENT`, `FWPM_FILTER_FLAG_PERSISTENT`) — sống sót qua reboot ngay cả trước khi `Service` kịp khởi động lại, thêm 1 lớp phòng thủ nếu có khoảng trống thời gian giữa lúc máy khởi động và `Service` chạy tới bước áp dụng WFP.

### 3.2 Ai áp dụng, khi nào

- **`Service`** (không phải installer) là chủ sở hữu duy nhất của các đối tượng WFP này — áp dụng ở bước `Starting` (`02-process-architecture.md` mục 6), **ngay sau khi đọc xong `config.db`/fallback, trước khi spawn `Vision` lần đầu tiên** — đảm bảo filter luôn tồn tại trước khi `Vision` có cơ hội chạy bất kỳ dòng code nào.
- **Idempotent bắt buộc**: mỗi lần `Starting`, `Service` kiểm tra Provider/Sublayer/Filter đã tồn tại (qua `FwpmProviderGetByKey0`/liệt kê filter theo `providerKey`) trước khi add — không tạo trùng lặp mỗi lần restart `Service` (kể cả restart do Watchdog, `ANTI-010`). Nhờ cờ persistent (mục 3.1), phần lớn các lần `Starting` sau lần đầu sẽ thấy filter đã tồn tại và bỏ qua bước add.
- Không cần áp dụng lại mỗi lần `Vision` bị respawn (đổi session, crash-restart) — filter match theo đường dẫn file (App ID), không theo PID, nên vẫn đúng hiệu lực xuyên suốt các lần spawn khác nhau của cùng 1 binary.
- **Phạm vi cố định**: filter này **chỉ áp dụng cho `ParentalGuard.Vision.exe`** — không mở rộng sang `Overlay`/`UI`/`Service`/`Watchdog`. Đây là đọc đúng nghĩa đen `SEC-010` ("theo đường dẫn + hash chữ ký" chỉ nêu `Vision`) và bảng threat model `04-security-spec.md` mục 3 (chỉ `Vision` được xác định là module nhạy cảm nhất, nắm dữ liệu pixel) — không tự ý mở rộng thêm cho các process khác dù về lý thuyết cũng "không cần network" (nguyên tắc "không network" của các process khác được enforce bằng code discipline + `SEC-001a`, không có yêu cầu tường minh nào đòi hỏi enforce thêm ở tầng OS cho chúng).

### 3.3 Phát hiện + xử lý khi `Vision` cố network

`Vision` không được viết code gọi network (`SEC-017`) — nhánh xử lý dưới đây chỉ kích hoạt nếu có bug/dependency bị compromise (supply-chain, `SEC-014`), đúng tinh thần defense-in-depth (`SEC-004`):

1. **Bật Windows Security Auditing** cho subcategory *"Filtering Platform Connection"* (`AUDIT_FAILURE`) — `Service` gọi `AuditSetSystemPolicy` lúc `Starting` (idempotent, không lỗi nếu đã bật sẵn) để đảm bảo Windows ghi **Event ID 5157** (*"The Windows Filtering Platform has blocked a connection"*) vào Security Event Log mỗi khi 1 filter BLOCK của mục 3.1 thực sự chặn 1 kết nối.
2. `Service` đăng ký `EventLogWatcher` (namespace `System.Diagnostics.Eventing.Reader`, có sẵn trong .NET) theo dõi Security log với XPath filter Event ID = 5157 — nhận callback gần-thời-gian-thực (không cần vòng lặp polling riêng) khi có sự kiện mới, lọc tiếp theo `Application Name` khớp đường dẫn `ParentalGuard.Vision.exe`.
3. Khi phát hiện đúng 1 sự kiện khớp: `Service` ghi vào `audit.log` (`04-data-architecture.md` mục 5.1) 1 record `event_type = "VisionNetworkBlocked"`, `detail = { dest_addr, dest_port, protocol, blocked_at_unix_ms }` (lấy từ nội dung Event 5157) — bổ sung 1 dòng vào bảng `event_type` ở `04-data-architecture.md` mục 5.1 (không sửa file đó ở đây, ghi nhận cần cập nhật đồng bộ — xem mục 7).
4. **Xử lý như crash**: `Service` kill (nếu tiến trình còn sống) + spawn lại `Vision` ngay lập tức, đúng luồng khôi phục đã có (`BE-023`, `02-process-architecture.md` mục 4) — không có cơ chế "cách ly vĩnh viễn" hay tắt hẳn `Vision` (vi phạm nguyên tắc fail-secure luôn giữ giám sát BẬT).
5. Sự kiện này **tính vào cùng bộ đếm rate-limit** của `ANTI-060` (N lần restart/T phút) — tái dùng cơ chế đã có, không phát minh ngưỡng cảnh báo riêng. Nếu vượt ngưỡng, hành vi cảnh báo (banner on-screen, không Toast chủ động) giữ nguyên đúng như `ANTI-061` đã `ĐÃ CHỐT` (không thêm kênh cảnh báo mới riêng cho `VisionNetworkBlocked`).

### 3.4 Phạm vi ngoài file này

Việc gỡ bỏ Provider/Sublayer/Filter WFP lúc uninstall hợp lệ (`ANTI-020`) và việc phát hiện ai đó cố tình xoá/vô hiệu hoá filter này (ví dụ dùng `netsh` hoặc PowerShell `Remove-NetFirewallRule` — về lý thuyết cần quyền Administrator, nằm ngoài phạm vi bảo vệ theo `SEC-005`) thuộc phạm vi `09-anti-tamper-architecture.md`, không thiết kế ở đây.

## 4. ACL cụ thể theo tài nguyên

### 4.1 `%ProgramFiles%\ParentalGuard\` (`ANTI-030`)

| Đối tượng | `SYSTEM` | `Administrators` | `Authenticated Users` (mọi tài khoản đăng nhập, gồm tài khoản trẻ Standard User) | Ghi chú |
|---|---|---|---|---|
| `*.exe` (6 executable — bổ sung v0.1.3, Đợt 4: `Service`/`Watchdog`/`Vision`/`Overlay`/`UI`/`Uninstaller`) | Full Control | **Modify** (không Full Control — đủ để installer/uninstaller elevated ghi đè bản cập nhật, không cấp thêm `WriteDAC`/`WriteOwner` tuỳ tiện) | **Read + Execute** (`Uninstaller.exe` cần Execute để Windows chạy được khi bấm "Uninstall" ở Control Panel/Settings — UAC + mật khẩu ParentalGuard mới là lớp chặn thật, `09-anti-tamper-architecture.md` mục 5.1) | ACL **protected** (không kế thừa từ `%ProgramFiles%` cha — `SetAccessRuleProtection(true, false)` hoặc tương đương `icacls /inheritance:r`) |
| `models\*.onnx` | Full Control | Modify | **Read + Execute** (chỉ `Vision` thực sự dùng quyền Read này — mục 2.3; `Overlay`/`UI` không có lý do đọc, nhưng ACL ở mức thư mục không phân biệt được giữa các user-session process khác nhau nếu chúng chạy cùng 1 SID user — ranh giới thực sự giữa `Vision` đọc được và `Overlay` không đọc được nằm ở **whitelist tài nguyên trong code** `Overlay` không bao giờ gọi `File.Open` tới đường dẫn này, không phải ở ACL cấp hệ điều hành — đây là giới hạn cố hữu của mô hình "cùng chạy dưới 1 user SID + restricted token giống nhau", không phải thiếu sót thiết kế) | + verify checksum runtime (`MISC-090`, chi tiết cơ chế ở `05-image-pipeline-architecture.md`) |

- **Lý do Administrators không có Full Control**: theo `SEC-005`, quyền Administrator luôn là ranh giới tin cậy cao nhất (`takeown`/`icacls` có thể vượt qua bất kỳ ACL nào) — nên việc giới hạn ở `Modify` **không phải** rào cản kỹ thuật tuyệt đối chống lại 1 Administrator thật sự cố ý, mà nhằm: (1) đúng nguyên tắc least-privilege ngay cả cho tài khoản tin cậy (không cấp dư "cho chắc"), (2) tăng ma sát/dấu vết — 1 hành động `takeown` bất thường dễ trở thành tín hiệu đáng ngờ hơn là 1 thao tác Delete bình thường nếu ACL đã cấp sẵn Full Control.

### 4.2 `%ProgramData%\ParentalGuard\` (hệ thống hoá lại từ `04-data-architecture.md` mục 2, không đổi)

| Đối tượng | `SYSTEM` | Mọi SID khác (kể cả `Administrators`) |
|---|---|---|
| `config.db`, `config.db-wal`, `config.db-shm` | Full Control | **Không có entry nào** (No Access — không Allow bất kỳ ai khác) |
| `auth.dat` | Full Control | Không có entry nào |
| `audit.log` | Full Control | Không có entry nào |

- Giữ nguyên **tuyệt đối** như đã chốt ở `04` (`BE-060`, `PWD-014`, `SEC-040`) — kể cả phụ huynh là Administrator cũng không đọc/ghi trực tiếp, chỉ qua `Service` (đã xác thực mật khẩu) bằng Named Pipe. ACL protected, không kế thừa từ `%ProgramData%` cha.
- **Không có ngoại lệ Read cho Administrators** (khác với mục 4.1, nơi Administrators vẫn cần Modify để phục vụ cài đặt/gỡ hợp lệ) — vì dữ liệu ở đây là **dữ liệu nhạy cảm** (cấu hình giám sát, hash mật khẩu, log), không phải file thực thi; cài đặt/gỡ hợp lệ không cần đọc trực tiếp các file này (uninstaller chỉ cần quyền **xoá**, không cần đọc nội dung — `Service` chạy dưới ngữ cảnh SYSTEM tự xoá `%ProgramData%` thay cho `Uninstaller.exe`/Administrators, chi tiết đầy đủ ở `09-anti-tamper-architecture.md` mục 5.5 — sửa tham chiếu số file v0.1.3, trước đó ghi "07" là số cũ chưa cập nhật qua 2 lần renumbering trước).

### 4.3 Registry key (`ANTI-031`, baseline cho `07` tái sử dụng)

| Key | `SYSTEM` | `Administrators` | `Authenticated Users` |
|---|---|---|---|
| `HKLM\SYSTEM\CurrentControlSet\Services\ParentalGuardService` | Full Control | Read + Write (đủ để SCM/installer elevated thao tác `Start`/`ImagePath` khi cần) | **Read only** |
| `HKLM\SYSTEM\CurrentControlSet\Services\ParentalGuardWatchdog` | Full Control | Read + Write | Read only |

- Windows đã mặc định áp dụng ACL tương đương bảng trên cho toàn bộ subtree `Services\*` (chỉ `SYSTEM`+`Administrators` có quyền ghi mặc định của OS, user thường chỉ Read) — mục này **xác nhận tường minh** baseline mặc định của OS là đủ cho Đợt 0, `Service` không cần tự set lại ACL registry (khác với file ở mục 4.1/4.2, nơi cần set chủ động vì `%ProgramFiles%`/`%ProgramData%` mặc định lỏng hơn cho user thường). Việc chủ động **giám sát** thay đổi bất thường trên key này (phần "watchdog phát hiện" trong tinh thần `ANTI-031`) thuộc `09-anti-tamper-architecture.md`, không thiết kế ở đây.
- Không có Registry Run key nào cho bất kỳ process nào của `ParentalGuard` — `Service`/`Watchdog` đăng ký qua SCM (`Start=Automatic`), `Vision`/`Overlay` do `Service` spawn runtime qua `CreateProcessAsUser` (`02-process-architecture.md` mục 2.3), không có entry Task Scheduler/Run key riêng — nên không có "Run key" nào cần ACL bổ sung ngoài bảng trên.

### 4.4 Nguyên tắc thực thi chung

- **Thời điểm set lần đầu**: installer (chạy elevated) set ACL ban đầu cho toàn bộ layout mục 4.1/4.2 lúc cài đặt — chi tiết installer cụ thể để ở `11-deployment-release-architecture.md` (Đợt 9 theo `ROADMAP.md`, chưa viết). Vì Đợt 0 chưa có installer thật, `Service` **tự áp dụng ACL tương đương** (qua `System.Security.AccessControl.FileSystemAccessRule`/`RegistrySecurity`) mỗi lần khởi động ở bước `Starting`, **idempotent** (kiểm tra ACL hiện tại khớp kỳ vọng trước khi ghi lại, tránh ghi ACL thừa mỗi lần restart) — logic này giữ nguyên cả khi Đợt 9 có installer thật (trở thành cơ chế self-heal nếu ACL bị nới lỏng ngoài ý muốn, hữu ích cho cả `09-anti-tamper-architecture.md`).
- **Không hand-roll SDDL string trong code**: dùng named enum của .NET (`FileSystemRights.FullControl`/`.Modify`/`.ReadAndExecute`, `RegistryRights.FullControl`/`.ReadKey`/`.WriteKey`) qua `FileSystemAccessRule`/`RegistryAccessRule` — tránh sai số bit-mask hex viết tay. SDDL ở bảng trên chỉ dùng để mô tả **ý định rõ ràng** cho người đọc tài liệu, không phải chuỗi bắt buộc copy nguyên văn vào code.

## 5. DPAPI application cụ thể (`SEC-040`, `SEC-040a`)

Áp dụng đồng nhất cho mọi nơi đã dùng "DPAPI machine-scope" ở `04-data-architecture.md`: `monitoring_state.data_encrypted`, `pause_state.data_encrypted`, `ipc_keys.key_encrypted` (mục 3), toàn bộ blob JSON của `auth.dat` (mục 4).

| Khía cạnh | Quyết định | Lý do |
|---|---|---|
| Scope | `DataProtectionScope.LocalMachine` (tương đương cờ native `CRYPTPROTECT_LOCAL_MACHINE`) | `Service` chạy LocalSystem, không gắn với 1 user profile cụ thể — đúng `ADR-06`/`SEC-040` |
| API cụ thể | `System.Security.Cryptography.ProtectedData.Protect`/`.Unprotect` (BCL sẵn có) — **không** P/Invoke trực tiếp `CryptProtectData`/`CryptUnprotectData` | `ProtectedData` là wrapper chính thức của .NET cho đúng 2 API này, đủ dùng cho `DataProtectionScope.LocalMachine`, tránh code P/Invoke thừa không cần thiết |
| Entropy bổ sung | **Có** — 1 hằng số 32 byte nhúng trong `ParentalGuard.Service.exe`, dùng chung cho mọi blob | Không phải bí mật thực sự chống lại kẻ tấn công có quyền đọc binary (dự án mã nguồn mở, hằng số nằm trong assembly) — mục đích thực chất: tránh 1 tool giải mã DPAPI generic chạy dưới SYSTEM (ví dụ tiện ích debug/forensic của bên thứ ba không biết cấu trúc riêng của app) đọc được nội dung mà không cố ý nhắm vào `ParentalGuard` cụ thể — phòng thủ thêm 1 lớp nhỏ, không thay thế cho ACL (mục 4, vẫn là lớp chặn truy cập chính) |
| Protection descriptor | DPAPI **cổ điển** (`CryptProtectData` qua `ProtectedData`) — **không** dùng DPAPI-NG (`NCryptProtectSecret`/protection descriptor kiểu SID/certificate/AD) | Đúng khớp chữ nghĩa `SEC-040` ("Windows DPAPI ở machine-scope" — tên gọi chính xác của DPAPI cổ điển). DPAPI-NG tối ưu cho môi trường domain-joined với hạ tầng protection descriptor — không phù hợp nhóm khách hàng mục tiêu (máy gia đình phổ thông, thường không domain-joined, `SEC-006`) |

- **Hệ quả khi cài lại Windows** (không phải edge case mới, chỉ ghi nhận rõ ở đây): DPAPI machine master key gắn với cài đặt Windows hiện tại — cài lại OS làm mất khả năng giải mã `config.db`/`auth.dat` cũ. Hành vi này **khớp đúng** luồng fail-secure đã thiết kế sẵn ở `04-data-architecture.md` mục 6 (coi như "không giải mã được" → tạo mới toàn bộ với default an toàn) — không cần xử lý gì thêm ở đây.

## 6. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-30 | `Vision`/`Overlay` dùng Restricted Token cổ điển (`CreateRestrictedToken` + `DISABLE_MAX_PRIVILEGE` + disable SID `Administrators`) + hạ Integrity Level xuống **Low**, không dùng AppContainer, không dùng thêm `SidsToRestrict` | Đóng câu hỏi mở `SEC-010` mục 10 (`04-security-spec.md`) — nhất quán `SEC-015` (AppContainer đã hạ backlog); Low IL là kỹ thuật đã kiểm chứng lâu năm (IE Protected Mode/Chrome sandbox cũ), rủi ro tương thích thấp hơn AppContainer với .NET non-packaged app |
| ADR-31 | `Service` chủ động hạ Mandatory Label (SACL, `S:(ML;;NW;;;LW)`) trên named pipe instance `Vision`/`Overlay` ngay lúc tạo | Hệ quả bắt buộc của ADR-30 (Low IL) — nếu không làm, MIC no-write-up sẽ chặn `Vision`/`Overlay` gửi bất kỳ message IPC nào, vỡ toàn bộ `03-ipc-communication.md` |
| ADR-32 | WFP dùng raw API (`Fwpuclnt.dll`), Provider/Sublayer riêng của dự án, filter ở cả `ALE_AUTH_CONNECT` (outbound) và `ALE_AUTH_RECV_ACCEPT` (inbound) v4/v6, persistent, `Service` tự áp dụng idempotent lúc `Starting` | Đúng nghĩa đen `SEC-010` (dùng WFP, không dùng Windows Firewall bậc cao); chặn cả 2 chiều hiện thực hoá triệt để `SEC-017`/`SEC-001a` (không network dưới bất kỳ hình thức nào) |
| ADR-33 | Phát hiện vi phạm WFP qua Windows Security Audit Event 5157 (`EventLogWatcher`, không polling thủ công) | Tận dụng cơ chế audit có sẵn của OS, gần-thời-gian-thực, không cần tự xây kênh log riêng cho sự kiện WFP |
| ADR-34 | Sự kiện `VisionNetworkBlocked` xử lý như crash (kill + respawn ngay), tính vào bộ đếm `ANTI-060`, không thêm kênh cảnh báo chủ động riêng | Tái dùng cơ chế khôi phục + rate-limit đã có, nhất quán tinh thần `ANTI-061` (không tạo thêm ngoại lệ cảnh báo chủ động ngoài trường hợp mất toàn vẹn `config.db`) |
| ADR-35 | DPAPI dùng `ProtectedData` (classic, machine-scope) + entropy bổ sung hằng số nhúng trong `Service`, không dùng DPAPI-NG | Khớp đúng chữ nghĩa `SEC-040`; DPAPI-NG không phù hợp môi trường non-domain-joined của nhóm khách hàng mục tiêu |
| ADR-36 | `Administrators` chỉ có `Modify` (không `Full Control`) trên `%ProgramFiles%\ParentalGuard\`; `%ProgramData%`/registry Service key chỉ `SYSTEM` tuyệt đối kể cả `Administrators` | Least-privilege áp dụng cả cho tài khoản tin cậy, tăng ma sát/dấu vết cho hành vi bất thường — không phải rào cản tuyệt đối chống 1 Administrator thật (đã ghi nhận rõ ở `SEC-005`) |
| ADR-37 | `Service` tự áp dụng ACL (file/registry) idempotent mỗi lần `Starting`, không chỉ dựa vào installer set 1 lần | Đợt 0 chưa có installer thật (Đợt 9 `ROADMAP.md`); đồng thời tạo sẵn cơ chế self-heal ACL hữu ích cho `09-anti-tamper-architecture.md` tái sử dụng |

## 7. Câu hỏi mở

- [ ] **Cần validate thực nghiệm ở Đợt 1** (`05-image-pipeline-architecture.md`, khi thật sự code Desktop Duplication API): `Vision` chạy Low Integrity Level (ADR-30) có tương thích với `IDXGIOutputDuplication`/`AcquireNextFrame` trên các driver GPU phổ biến (Intel/NVIDIA/AMD) hay không. Nếu phát hiện không tương thích: fallback đã định sẵn ở mục 2.2 phương án D (giữ Restricted Token, nâng lại Medium IL) — chỉ đổi 1 tham số `SetTokenInformation` lúc spawn, không phải thiết kế lại kiến trúc, ghi nhận kết quả tại `05-image-pipeline-architecture.md` khi đến Đợt 1.
- [ ] Giá trị chính xác weight WFP filter (hiện tạm `15`, mức tối đa thang tự động) và chu kỳ/độ trễ chấp nhận được của `EventLogWatcher` theo dõi Event 5157 — để tinh chỉnh thực nghiệm lúc code Đợt 0, không chốt số tuyệt đối ở tài liệu kiến trúc.
- [x] ~~Cần bổ sung 1 dòng vào bảng `event_type` ở `04-data-architecture.md` mục 5.1 cho `VisionNetworkBlocked`~~ — **Đã xong** (`04` v0.2.1, cùng lượt viết `08-password-authentication-architecture.md` Đợt 3, tiện thể đóng khoản nợ kỹ thuật này).

## 8. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.1.3 | 2026-09-19 | PATCH — amendment cùng lượt viết `09-anti-tamper-architecture.md` (Đợt 4). Mục 4.1: đổi "5 executable" → "6 executable" (thêm `Uninstaller.exe`, cùng hàng ACL Modify/Read+Execute). Mục 4.2: sửa tham chiếu số file còn sót "chi tiết ở `07`" → "`09-anti-tamper-architecture.md` mục 5.5" (nợ kỹ thuật sót lại qua 2 lần renumbering trước, `07`→`08`→`09`, phát hiện khi viết `09`) + làm rõ ai thực sự xoá `%ProgramData%` (`Service`/SYSTEM, không phải `Uninstaller.exe`/Administrators — đúng thiết kế phân công ở `09` mục 5.5/ADR-96). Không đổi bất kỳ quyết định ACL/token/WFP/DPAPI nào khác |
| v0.1.2 | 2026-09-19 | PATCH — amendment cùng lượt viết `08-password-authentication-architecture.md` (Đợt 3). Đổi 6 tham chiếu số file: 5 chỗ `08-anti-tamper-architecture.md`→`09-anti-tamper-architecture.md` (mục 1, mục 3.1, mục 3.4, mục 4.3, ADR-37, mục 4.4 — 2 chỗ ở mục 4.3/4.4 là nợ kỹ thuật sót lại từ lần renumbering v0.1.1 trước đó, nay phát hiện và sửa luôn) và 1 chỗ `09-deployment-release-architecture.md`→`11-deployment-release-architecture.md` (mục 4.4), theo renumbering ở `00-INDEX.md` khi chèn `08-password-authentication-architecture.md` mới. Đóng câu hỏi mở mục 7 về dòng `event_type` `VisionNetworkBlocked` còn thiếu ở `04` (nay đã bổ sung, `04` v0.2.1). Không đổi nội dung quyết định bảo mật |
| v0.1.1 | 2026-09-19 | PATCH — đổi 4 tham chiếu `07-anti-tamper-architecture.md` thành `08-anti-tamper-architecture.md` (mục 1, mục 3.1, mục 3.4, ADR-37), theo renumbering ở `00-INDEX.md` (chèn `07-overlay-architecture.md` mới cho Đợt 2). Không đổi nội dung quyết định |
| v0.1.0 | 2026-09-17 | Khởi tạo — token/Integrity Level pipeline cho `Vision`/`Overlay` (Low IL + Restricted Token cổ điển, không AppContainer — đóng câu hỏi mở `SEC-010` mục 10 `Specification/04-security-spec.md`), hệ quả Mandatory Label bắt buộc lên Named Pipe (bổ sung kỹ thuật cho `03-ipc-communication.md`), WFP rule chặn network `Vision` 2 chiều (ALE layer, Provider/Sublayer riêng, persistent, áp dụng lúc `Service` Starting, idempotent) + phát hiện qua Security Event 5157 + xử lý kill-restart tái dùng `ANTI-060`, bảng ACL đầy đủ `%ProgramFiles%`/`%ProgramData%`/registry Service key, DPAPI application cụ thể (classic DPAPI machine-scope qua `ProtectedData` + entropy hằng số, không DPAPI-NG), 8 ADR (30-37), 3 câu hỏi mở (validate DXGI/Low IL ở Đợt 1, tinh chỉnh số liệu WFP, bổ sung `event_type` vào `04`) |

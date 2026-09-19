# 04 — Security Spec

> Version: v0.6.1 | Trạng thái: Approved | Cập nhật: 2026-09-17

## 1. Nguyên tắc bảo mật cốt lõi

- `SEC-001`: **Local-first tuyệt đối** — không có network call nào trong pipeline xử lý ảnh, dưới bất kỳ hoàn cảnh nào (kể cả "gửi để cải thiện model", "gửi báo lỗi kèm ảnh"...).
- `SEC-001a` (ĐÃ CHỐT v0.5.0, mở rộng phạm vi `SEC-001`/`GEN-030`): **Toàn bộ ứng dụng** (không chỉ pipeline xử lý ảnh) — mọi module `Service`/`Vision`/`Overlay`/`UI`/`Watchdog` — **tuyệt đối không được truy cập internet** dưới bất kỳ hình thức nào. Không có ngoại lệ "kênh network duy nhất cho update" như đề xuất ban đầu ở `10-additional-mechanisms-spec.md` — đề xuất đó đã bị **REJECTED** (`MISC-020`). Xem `GEN-034` ở `01-tong-quan-va-pham-vi.md`.
- `SEC-002`: **Least privilege** — mỗi process chỉ có quyền tối thiểu cần thiết cho việc của nó (xem phân chia process ở `02-backend-spec.md`). Riêng thao tác `WTSQueryUserToken`/`CreateProcessAsUser` (cần thiết để khởi chạy `Vision` vào đúng session tương tác — xem `BE-023a` ở `02-backend-spec.md`) là ngoại lệ nhạy cảm duy nhất được phép, và **chỉ `Service`** được thực hiện (`BE-023b`) — không module nào khác được cấp quyền này.
- `SEC-003`: **Minh bạch** — không ẩn giấu sự tồn tại của app (khác biệt cốt lõi với stalkerware), luôn có icon trạng thái, tên process rõ ràng trong Task Manager.
- `SEC-004`: **Defense in depth** — không phụ thuộc vào một lớp bảo vệ duy nhất cho bất kỳ tính năng nào (ví dụ chống gỡ dựa vào cả Service protection + registry ACL + watchdog kép).

## 2. Threat Model (STRIDE rút gọn)

| Loại đe doạ | Kịch bản cụ thể | Biện pháp giảm thiểu |
|---|---|---|
| **Spoofing** | Tiến trình giả mạo gửi lệnh qua Named Pipe để giả làm UI/Service | ACL trên pipe + HMAC ký message (xem `BE-050`, `BE-051`) |
| **Tampering** | Trẻ sửa file cấu hình, registry, hoặc audit log để tắt bảo vệ/xoá vết | DPAPI mã hoá config, hash-chain cho audit log, ACL chặn ghi từ user thường (chi tiết `05-anti-uninstall-tamper-spec.md`) |
| **Repudiation** | Không có bằng chứng khi có sự kiện gián đoạn bảo vệ | Audit log tamper-evident, ghi cả sự kiện watchdog restart |
| **Information Disclosure** | Rò rỉ ảnh/frame ra ngoài qua log, crash dump, network | `SEC-001` + `BE-021/BE-022` + cấu hình crash dump không chứa vùng nhớ ảnh (xem mục 5) |
| **Denial of Service** | Trẻ cố tình làm Vision Engine crash liên tục để vô hiệu hoá bảo vệ | Watchdog auto-restart + rate-limit số lần restart trước khi cảnh báo phụ huynh |
| **Elevation of Privilege** | Lợi dụng lỗ hổng trong Service (chạy quyền LocalSystem) để leo thang quyền | Hạn chế bề mặt tấn công: Service không parse dữ liệu phức tạp từ input không tin cậy, code review kỹ phần IPC parsing |

## 2.1 Threat model bổ sung: kịch bản "trẻ có quyền Administrator" (ĐÃ CHỐT v0.2.0)

Đây là câu hỏi nền tảng cần chính thức hoá ranh giới bảo vệ, không chỉ ghi chú qua loa:

- `SEC-005`: **Chính thức xác nhận: kịch bản trẻ sở hữu quyền Administrator trên máy nằm ngoài phạm vi bảo vệ của phần mềm ở Phase 1.** Đây không phải thiếu sót kỹ thuật có thể vá bằng cách code kỹ hơn — mà là giới hạn nền tảng của mô hình phân quyền Windows: quyền Administrator **chính là** ranh giới tin cậy cao nhất của hệ điều hành, không có phần mềm ứng dụng nào (kể cả chạy dưới LocalSystem) có thể tự đặt mình cao hơn chính quyền admin của máy đang chạy nó. Một khi có quyền admin, người dùng luôn có thể: dừng mọi service (kể cả watchdog kép ở `05-anti-uninstall-tamper-spec.md`), `takeown`/`icacls` để chiếm quyền sở hữu file/registry dù ACL đã chặn, tắt Windows Defender, boot Safe Mode, hoặc gỡ ổ cứng sang máy khác để xoá offline.
- `SEC-006`: Do đó, tài liệu người dùng (onboarding, hướng dẫn sử dụng) **bắt buộc nêu rõ khuyến nghị**: phụ huynh nên là tài khoản Administrator duy nhất trên máy, tài khoản trẻ dùng nên là Standard User (liên kết `GEN-022`, `ANTI-041`).
- `SEC-007` (định hướng roadmap, không đầu tư Phase 1): 2 hướng có thể thực sự nâng ranh giới bảo vệ vượt qua giới hạn admin-local, để ngỏ cho Phase 2+:
  1. **Kernel-mode minifilter driver** (ký số WHQL): chặn thao tác xoá/dừng ở tầng kernel — vẫn có thể bị vô hiệu qua Safe Mode/disable driver bởi người đủ hiểu biết, và chi phí phát triển + chứng nhận WHQL rất lớn, không phù hợp quy mô dự án cộng đồng ở giai đoạn đầu.
  2. **Quản lý qua MDM/Intune** (máy được enroll vào tenant do phụ huynh kiểm soát): đây là cách duy nhất thực sự chuyển ranh giới tin cậy từ "admin local" lên "tenant admin" mà trẻ không kiểm soát được dù có quyền admin local — nhưng đổi hẳn mô hình triển khai (không còn "cài là chạy ngay"), phù hợp hơn cho gia đình có kỹ thuật hoặc triển khai qua trường học, không phải mục tiêu Phase 1 dành cho phụ huynh phổ thông.

## 3. Bảo vệ tiến trình `Vision` khỏi network — enforce ở tầng OS

- `SEC-010`: Ngoài việc không viết code gọi network trong `Vision`, phải enforce bổ sung ở tầng hệ điều hành để phòng trường hợp có bug hoặc dependency bị compromise (supply-chain risk):
  - Tạo rule **Windows Filtering Platform (WFP)** chặn toàn bộ outbound traffic từ process `ParentalGuard.Vision.exe` (theo đường dẫn + hash chữ ký), áp dụng lúc Service khởi động.
  - `Vision` chạy như process riêng biệt, quyền hạn chế (không phải LocalSystem đầy đủ) — mức bảo vệ tối thiểu bắt buộc Phase 1.

### 3.1 Đánh giá lại bề mặt tấn công của Vision (ĐÍNH CHÍNH v0.3.0 — thay thế nội dung v0.2.0)

- `SEC-013` **(đã sửa)**: `Vision` **không tự giải mã file ảnh nén** (JPEG/PNG/WebP...) dưới bất kỳ hình thức nào — nó dùng Desktop Duplication API (DXGI) để chụp lại **pixel đã được render sẵn** trên màn hình, sau khi browser/video player đã tự giải mã ảnh xong. Vì vậy, lớp rủi ro "decoder exploit" kinh điển (ví dụ CVE-2023-4863 ở libwebp, hay kỹ thuật FORCEDENTRY khai thác decoder JBIG2) **không áp dụng trực tiếp cho `Vision`** — bề mặt tấn công đó nằm ở phía browser/hệ điều hành (nơi thực sự parse file ảnh nén), ngoài phạm vi của `Vision`. Input mà `Vision` nhận là buffer pixel thô, cấu trúc đơn giản, kích thước cố định — bề mặt tấn công nhỏ hơn nhiều so với đánh giá ban đầu.
- `SEC-014` **(đã sửa)**: Rủi ro còn lại không do sandbox giải quyết được:
  - **Adversarial example** (model bị đánh lừa bằng pixel được tính toán kỹ để né/gây false positive) — đây là vấn đề độ bền của model AI, không phải lỗ hổng bộ nhớ, sandbox không có tác dụng với loại này (đã ghi nhận ở "Known Risks" trong `01-tong-quan-va-pham-vi.md`).
  - **Supply-chain risk** (dependency như ONNX Runtime bị compromise) — rủi ro này áp dụng cho **mọi** process trong hệ thống (Service, Overlay, UI đều dùng dependency bên thứ ba), không phải lý do riêng để ưu tiên sandbox hoá `Vision` hơn các module khác.

### 3.2 Quyết định ưu tiên AppContainer — hạ xuống backlog (ĐÍNH CHÍNH v0.3.0)

- `SEC-015` **(đã sửa, thay thế quyết định "Phase 2" trước đó)**: Vì lý do chính đưa ra ban đầu cho AppContainer đầy đủ (phòng "ảnh bẫy" khai thác decoder) không thực sự áp dụng với kiến trúc capture-pixel-thô hiện tại, **AppContainer đầy đủ được hạ xuống mức backlog/optional**, không còn là hạng mục đã lên kế hoạch cho Phase 2. Chỉ đáng cân nhắc triển khai lại nếu:
  1. Scope tương lai mở rộng để `Vision` (hoặc module kế thừa) tự giải mã file ảnh/video tải về từ nguồn không tin cậy (khi đó lớp rủi ro decoder-exploit mới thực sự xuất hiện trở lại), hoặc
  2. Có ngân sách dư để đầu tư phòng thủ chiều sâu bổ sung mà không đánh đổi tiến độ, thuần tuý vì lợi ích "thêm 1 lớp" chứ không phải để giải quyết 1 rủi ro cụ thể đã xác định.
- `SEC-016` (giữ nguyên, không đổi): Mức tối thiểu Phase 1 (`SEC-010` — WFP chặn network + `Vision` chạy process riêng biệt, quyền hạn chế) **vẫn giữ nguyên là bắt buộc**, không phụ thuộc vào đánh giá lại ở trên — vì đây là biện pháp chi phí thấp, có giá trị phòng thủ chung (chặn exfiltration dữ liệu bất kể nguyên nhân compromise là gì, kể cả các nguyên nhân chưa lường trước), không cần 1 threat model cụ thể để biện minh.

### 3.3 Vision — nguyên tắc "single-purpose", không thao tác tài nguyên hệ thống khác (ĐÃ CHỐT v0.4.0)

- `SEC-017`: `Vision` **chỉ có đúng 1 nhiệm vụ**: capture màn hình + phân tích ảnh (inference) + trả kết quả (risk score, toạ độ) về `Service` qua IPC. Đây là phiên bản tường minh, cụ thể hoá của nguyên tắc Least Privilege (`SEC-002`), liệt kê rõ để không ai "tiện tay" mở rộng chức năng của module này khi code:
  - **Không network**: đã có ở `SEC-010`/`SEC-016` (enforce cả ở tầng code lẫn tầng OS qua WFP).
  - **Không đọc/ghi registry** dưới bất kỳ hình thức nào.
  - **Không đọc/ghi file** ngoài đúng 1 việc: đọc file model `.onnx` ở đường dẫn cố định (read-only, có verify checksum theo `MISC-090`). Không tạo file mới, không ghi log trực tiếp xuống đĩa (log do `Service` ghi, `Vision` chỉ gửi sự kiện qua IPC cho `Service` tự ghi).
  - **Không tự đọc `config.db`**: `Vision` nhận toàn bộ cấu hình cần thiết (bật/tắt, tần suất capture, ngưỡng risk score) qua lệnh IPC từ `Service` — không tự kết nối đọc file cấu hình, giữ đúng mô hình `Service` là nơi duy nhất sở hữu và enforce cấu hình (`BE-012`).
  - **Không khởi chạy tiến trình con khác**, không inject vào tiến trình khác, không thao tác cửa sổ của ứng dụng khác (việc vẽ overlay là trách nhiệm của `Overlay`, không phải `Vision` — giữ đúng phân chia module ở `02-backend-spec.md`).
  - **Không giao tiếp trực tiếp với `Overlay` hay `UI`** — mọi thông tin đi qua `Service` làm trung gian duy nhất (đã có trong kiến trúc IPC ở `02-backend-spec.md` mục 3).
- `SEC-018`: Yêu cầu này cần được **enforce ở nhiều lớp**, không chỉ dựa vào "viết code đúng": (1) code review bắt buộc kiểm tra không có import/using nào liên quan network, registry, file I/O ngoài phạm vi cho phép trong toàn bộ project `Vision`; (2) test tự động (xem `11-testing-qa-process.md`) quét static analysis để phát hiện các API bị cấm (registry API, `System.Net.*`, file write API ngoài đường dẫn model) nếu vô tình được gọi trong code của `Vision`; (3) enforce ở tầng OS như đã có (WFP cho network, và cân nhắc thêm ACL hạn chế filesystem access ở mức tối thiểu theo `SEC-016`, dù chưa cần AppContainer đầy đủ theo `SEC-015`).

## 4. Bảo mật kênh IPC nội bộ

- `SEC-011`: Named Pipe tạo với `PipeSecurity` chỉ cho phép SID của các process ParentalGuard cụ thể (xác định qua chữ ký code-signing, không chỉ tên process — tránh giả mạo bằng cách đặt tên file giống).
- `SEC-012`: Payload IPC dùng Protobuf (tránh parser tự chế dễ có lỗi buffer overflow) + giới hạn kích thước message tối đa hợp lý (chặn payload bất thường lớn).

## 5. Chính sách Crash Dump & Diagnostic

- `SEC-020`: Cấu hình Windows Error Reporting (WER) để **loại trừ** `ParentalGuard.Vision.exe` khỏi việc tạo full memory dump khi crash (full dump có thể chứa vùng nhớ chứa frame ảnh) — chỉ cho phép mini-dump không chứa heap data ảnh, hoặc tắt hẳn dump tự động cho tiến trình này và thay bằng log lỗi dạng text (stack trace, không có memory content).

## 6. Chính sách với Antivirus / SmartScreen

- `SEC-030` (ĐÃ CHỐT v0.6.0 — sửa lại theo mô hình free/open-source): Ký số toàn bộ executable trước khi phát hành bất kỳ bản build nào ra ngoài môi trường dev. **Không dùng EV Code Signing Certificate tự mua** (đề xuất cũ) — vì dự án chuyển hẳn sang **miễn phí/open-source, không còn kế hoạch bán để có ngân sách trả phí chứng chỉ EV hàng năm + HSM/USB token**. Thay vào đó dùng **chứng chỉ OV miễn phí qua chương trình SignPath Foundation dành cho dự án open-source** (`signpath.io/solutions/open-source-community`) — ký qua pipeline CI/CD (GitHub Actions), private key nằm hoàn toàn trên HSM của SignPath, dự án không bao giờ cầm/quản lý private key (đơn giản hoá luôn cả `DEV-012`). Lưu ý điều kiện tham gia: SignPath yêu cầu dự án **đã có ít nhất 1 bản release công khai** trước khi đủ điều kiện — bản release đầu tiên có thể cần phát hành tạm thời chưa ký hoặc ký bằng chứng chỉ cá nhân rẻ tiền, rồi apply SignPath ngay sau đó. Tham chiếu quy trình submit false-positive cho Microsoft nếu vẫn bị SmartScreen flag (đã phân tích ở giai đoạn nghiên cứu trước).
- `SEC-031`: Duy trì tài liệu "Software Behavior Disclosure" (mô tả rõ hành vi hệ thống: capture màn hình, watchdog, chặn uninstall) để đính kèm khi submit false-positive cho Microsoft, và để publish công khai trên trang dự án (tăng uy tín, giảm nghi ngờ từ AV vendor khác ngoài Microsoft).

## 7. Bảo mật dữ liệu tại chỗ (Data at Rest)

- `SEC-040`: Toàn bộ config nhạy cảm (ngưỡng nhạy cảm, whitelist, password hash) mã hoá bằng **Windows DPAPI ở machine-scope** (không phải user-scope, vì Service chạy dưới LocalSystem, không gắn với 1 user session cụ thể).
- `SEC-040a`: Nếu DPAPI giải mã `config.db` thất bại (file hỏng/bị sửa trái phép), hành vi fail-secure — fallback về bộ cấu hình mặc định hard-code trong code, giám sát luôn BẬT, không dừng hoạt động — xem `BE-061` ở `02-backend-spec.md` và `ANTI-070` ở `05-anti-uninstall-tamper-spec.md` (nội dung gốc đặt ở 2 file đó để tránh trùng lặp).
- `SEC-041`: File audit log dùng cơ chế **hash chain** (mỗi entry chứa hash của entry trước) để phát hiện nếu bị xoá/sửa entry giữa chừng — không cần mã hoá nội dung (vì không chứa dữ liệu nhạy cảm) nhưng cần đảm bảo tính toàn vẹn (integrity).

## 8. Cập nhật phần mềm (ĐÃ CHỐT v0.5.0 — không có cơ chế auto-update)

- ~~`SEC-050`: Mọi bản cập nhật (app hoặc model AI) phải được ký số và verify chữ ký trước khi cài đặt~~ — **REJECTED cùng `MISC-020`** ở `10-additional-mechanisms-spec.md`. Không có cơ chế update nào trong app (không network). Cập nhật app/model chỉ qua **cài lại installer thủ công** (đã ký số theo `SEC-030`/`GEN-033` — vẫn áp dụng cho bản thân file installer, chỉ khác là không có logic auto-update bên trong app).

## 9. Nguyên tắc đạo đức/pháp lý cần tuân thủ song song

- `SEC-060`: App **không** được thiết kế để hoạt động ẩn danh hoàn toàn (khác biệt bắt buộc so với stalkerware) — icon trạng thái (`FE-020`) là yêu cầu bảo mật/đạo đức bắt buộc, không phải tính năng tuỳ chọn.
- `SEC-061`: Tài liệu người dùng phải nêu rõ giới hạn của app (không giám sát tin nhắn mã hoá, không đảm bảo chặn 100%) để tránh phụ huynh ỷ lại quá mức vào công cụ kỹ thuật.

## 10. Câu hỏi mở

- [ ] ~~Kết quả benchmark DirectML trong AppContainer~~ — không còn cần thiết ở System Design do `SEC-015` đã hạ AppContainer đầy đủ xuống backlog; chỉ cần benchmark lại nếu điều kiện kích hoạt ở `SEC-015` mục 1-2 xảy ra trong tương lai.
- [ ] (Còn mở) Định nghĩa cụ thể "restricted token" ở mức tối thiểu Phase 1 (`SEC-010`/`SEC-016`) — dùng Windows Job Object + restricted token cổ điển, hay có cơ chế nhẹ hơn khác phù hợp hơn với .NET runtime? Quyết định kỹ thuật cụ thể ở System Design.

## 11. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.6.1 | 2026-09-17 | Chủ dự án approve toàn bộ requirement trong file này — chuyển trạng thái file từ `Draft` sang `Approved` |
| v0.6.0 | 2026-09-17 | Sửa `SEC-030`: bỏ yêu cầu tự mua EV Code Signing Certificate (không còn ngân sách vì dự án chuyển hẳn sang free/open-source, không bán nữa), thay bằng chứng chỉ OV miễn phí qua chương trình SignPath Foundation cho dự án open-source — đồng thời đơn giản hoá `DEV-012` vì SignPath tự giữ private key trên HSM của họ |
| v0.5.0 | 2026-09-17 | Thêm `SEC-001a` — mở rộng nguyên tắc local-first sang **toàn bộ app** (không chỉ pipeline ảnh), tuyệt đối zero internet, không ngoại lệ. Loại bỏ `SEC-050` (update security) do `MISC-020` bị REJECTED — không còn cơ chế auto-update, chỉ cài lại thủ công |
| v0.4.2 | 2026-09-17 | Thêm `SEC-040a` — liên kết chéo hành vi fail-secure khi DPAPI giải mã `config.db` thất bại (fallback cấu hình mặc định hard-code, giám sát luôn BẬT), nội dung gốc đặt ở `BE-061` (`02-backend-spec.md`) và `ANTI-070` (`05-anti-uninstall-tamper-spec.md`) |
| v0.4.1 | 2026-09-17 | Thêm liên kết tới `BE-023a`/`BE-023b` ở `SEC-002` — ghi nhận ngoại lệ duy nhất cho nguyên tắc Least Privilege (thao tác `WTSQueryUserToken`/`CreateProcessAsUser` chỉ `Service` được thực hiện, để khởi chạy `Vision` vào session tương tác) |
| v0.4.0 | 2026-09-17 | **Chốt**: thêm nguyên tắc "single-purpose" tường minh cho `Vision` — chỉ capture+phân tích+trả kết quả, cấm tuyệt đối network/registry/file write ngoài đọc model/spawn process/giao tiếp trực tiếp Overlay-UI (`SEC-017`), kèm yêu cầu enforce nhiều lớp gồm code review + static analysis + OS-level (`SEC-018`). Section 3.3 mới |
| v0.3.0 | 2026-09-17 | **Đính chính quan trọng**: sửa lại `SEC-013`/`SEC-014` — lý do "ảnh bẫy khai thác decoder" không áp dụng cho `Vision` vì kiến trúc capture pixel thô qua DXGI, không giải mã file ảnh nén. Hạ AppContainer đầy đủ từ "Phase 2 đã lên kế hoạch" xuống **backlog/optional** (`SEC-015`), chỉ mức tối thiểu (network block + process tách biệt) vẫn bắt buộc Phase 1 (`SEC-016`, không đổi) |
| v0.2.0 | 2026-09-17 | **Chốt**: kịch bản "trẻ có quyền Administrator" chính thức ngoài phạm vi bảo vệ Phase 1, có ghi rõ lý do nền tảng + 2 hướng roadmap Phase 2+ (kernel driver, MDM) — `SEC-005` đến `SEC-007`, section 2.1 mới. AppContainer cho Vision: lý do cần sandbox hoá giải thích rõ (`SEC-013`, `SEC-014`) + quyết định phân kỳ 2 mức, mức tối thiểu bắt buộc Phase 1, AppContainer đầy đủ đẩy sang Phase 2 do rủi ro tương thích DirectML (`SEC-015`) |
| v0.1.0 | 2026-09-17 | Khởi tạo |

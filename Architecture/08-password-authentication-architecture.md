# 08 — Password & Authentication Architecture

> Version: v0.3.1 | Trạng thái: Approved | Cập nhật: 2026-09-20

## 0. Ghi chú tổ chức tài liệu (vì sao có file riêng, vì sao đánh số `08`)

`ROADMAP.md` mục 3 (bảng domain) ghi Đợt 3 (Password & Authentication) chỉ phụ thuộc `Architecture/04`, `06`. Sau khi đọc kỹ cả 2 file: `04-data-architecture.md` mục 4 (`auth.dat`) đã dựng sẵn đúng schema cần thiết từ Đợt 0 (hash Argon2id PHC string, `argon2_params`, `rate_limit`, `security_questions: null` placeholder) và `06-security-architecture.md` mục 4.2/5 đã chốt ACL + DPAPI áp dụng cho `auth.dat` — **cả 2 file đều đã đủ, không cần amendment nội dung schema/ACL** (xác nhận tường minh ở mục 2 dưới đây, đúng yêu cầu rà soát đã giao).

Tuy vậy, nội dung cần thiết kế mới cho Đợt 3 (tham số Argon2id cụ thể, luồng memory hygiene xuyên suốt 2 tiến trình, toàn bộ message IPC cho setup/xác thực/đổi mật khẩu/khôi phục, cơ chế rate-limit thực thi cụ thể, format Recovery Key) đủ lớn và đủ độc lập (không thuộc về data schema hay OS security boundary thuần tuý) để xứng đáng **1 file riêng** — không nhồi vào `04`/`06` (sẽ làm 2 file đó phình to lẫn lộn chủ đề, đúng tinh thần đã áp dụng khi tách `07-overlay-architecture.md` khỏi `02`). Tạo file mới, đánh số **`08`** (chèn ngay sau `07-overlay-architecture.md`, đúng thứ tự Đợt 3 trong `ROADMAP.md`), dồn số 4 file "chưa viết" còn lại (xác nhận qua `Architecture/00-INDEX.md` cột trạng thái — không có file thật nào trên đĩa cho các số này, chỉ là placeholder mục lục): `08-anti-tamper-architecture.md`→`09`, `09-ui-architecture.md`→`10`, `10-deployment-release-architecture.md`→`11`, `11-dev-automation-architecture.md`→`12`. Cập nhật đồng bộ tham chiếu bằng số ở các file `Approved` bị ảnh hưởng (`02`, `03`, `06`) trong cùng lượt — chi tiết đầy đủ ở changelog `00-INDEX.md` mục 6.

## 1. Mục đích và phạm vi

File này trả lời **HOW** cho toàn bộ `PWD-001`–`PWD-035` (`Specification/06-password-management-spec.md`, Approved, không có câu hỏi mở nào ở thời điểm viết file này): tham số Argon2id cụ thể, luồng bảo vệ plaintext trong RAM xuyên suốt `UI`↔`Service`, message IPC cho đặt mật khẩu lần đầu, modal xác thực hành động nhạy cảm, đổi mật khẩu, và khôi phục qua Recovery Key. Không phát minh yêu cầu sản phẩm mới — mọi quyết định trích dẫn ngược `PWD-0xx`, hoặc là ADR thuần kỹ thuật (mục 8).

File này **không** thiết kế lại: (a) schema lưu trữ `auth.dat`/ACL/DPAPI — đã chốt đủ ở `04-data-architecture.md` mục 4 và `06-security-architecture.md` mục 4.2/5, chỉ tái sử dụng nguyên trạng; (b) UI/UX cụ thể của màn hình Onboarding/S5 modal/S6 Recovery (bố cục, animation, đa ngôn ngữ) — thuộc `10-ui-architecture.md` (Đợt 6, UI polish theo `ROADMAP.md`), file này chỉ định nghĩa **hợp đồng IPC** mà UI phải gọi; (c) câu hỏi bảo mật bổ sung (`PWD-034`/`035`) — **Phase 2**, ngoài phạm vi `ROADMAP.md` Đợt 3, chỉ chừa chỗ mở rộng (mục 7.6).

## 2. Xác nhận `04`/`06` đã đủ cho Đợt 3 (không cần amendment schema)

| Nhu cầu Đợt 3 | Đã có ở đâu | Kết luận |
|---|---|---|
| Lưu hash Argon2id (PHC string) cho mật khẩu + Recovery Key | `04` mục 4, field `password.hash`/`recovery_key.hash` | Đủ — không đổi |
| Lưu tham số Argon2id đi kèm mỗi hash | `04` mục 4, field `argon2_params {memory_kb, iterations, parallelism}` | Đủ — không đổi |
| Rate-limit counter sống sót qua restart `Service` | `04` mục 4, field `rate_limit {consecutive_failures, last_failure_at_unix_ms, delay_until_unix_ms}` (ADR-26) | Đủ — không đổi (mục 5 dưới đây dùng nguyên field này) |
| Đánh dấu Recovery Key đã dùng | `04` mục 4, field `recovery_key.used` | Đủ — không đổi |
| Chỗ trống cho câu hỏi bảo mật Phase 2 | `04` mục 4, field `security_questions: null` | Đủ — giữ `null`, không thiết kế thêm |
| ACL chỉ `SYSTEM` đọc/ghi `auth.dat` | `06` mục 4.2 | Đủ — không đổi |
| Mã hoá DPAPI machine-scope cho toàn bộ blob `auth.dat` | `06` mục 5 | Đủ — không đổi |
| `AuthState` (RAM, không persist) | `02-process-architecture.md` mục 5 (placeholder) | Cụ thể hoá ở mục 4/7 file này — không cần bảng `config.db` mới (đúng `PWD-013`: tách biệt `config.db`) |

Không có gap schema nào cần `spec-maintainer` hay amendment `04`/`06` cho phần lưu trữ cốt lõi. 2 amendment nhỏ, thuần bổ sung (không đổi ý nghĩa field cũ) được thực hiện cùng lượt: (1) `04` mục 5.1 thêm 2 dòng `event_type` (`AuthBruteForceThresholdReached` cho chính domain này, và `VisionNetworkBlocked` — khoản nợ kỹ thuật còn treo từ `06` v0.1.0 mục 7, tiện thể đóng luôn); (2) `03-ipc-communication.md` thêm message Password/Auth vào khối field 80-99 đã dành sẵn cho kênh `UI`. Chi tiết ở changelog các file đó.

## 3. Vai trò & ranh giới trách nhiệm

- **`Service` là nơi DUY NHẤT tính toán/so sánh Argon2id** — đúng tinh thần least-privilege (`SEC-002`) và hệ quả trực tiếp của `PWD-014`/`06` mục 4.2 (chỉ tiến trình chạy dưới `SYSTEM`, tức `Service`, đọc được `auth.dat`). `UI` không bao giờ tự đọc `auth.dat`, không bao giờ tự hash — `UI` chỉ thu thập input từ `PasswordBox` và chuyển tiếp qua IPC.
- `Vision`/`Overlay` **không liên quan gì** đến domain này — không nhận, không xử lý bất kỳ message Password/Auth nào (đúng nguyên tắc mỗi pipe chỉ chấp nhận đúng nhóm message thuộc kênh đó, `03-ipc-communication.md` mục 2.1).
- Toàn bộ message ở file này đi qua pipe `ParentalGuard.Svc.UI` — dùng đúng session-key ephemeral đã chốt ở `03-ipc-communication.md` mục 5.3 (không thiết kế lại xác thực danh tính `UI`/HMAC, chỉ định nghĩa thêm nội dung nghiệp vụ mang trên kênh đã có).

## 4. Argon2id — tham số cụ thể, thư viện, định dạng lưu trữ

### 4.1 Thư viện

**Chọn `Konscious.Security.Cryptography.Argon2` (`Konscious.Security.Cryptography.Argon2id` class, MIT, thuần C# managed)** — không dùng thư viện wrap native (`Isopoh.Cryptography.Argon2`, bind `libargon2` qua P/Invoke). Lý do (ADR-71, mục 8): (1) nhất quán với thận trọng đã thể hiện ở `06` ADR-30 (tránh kỹ thuật/dependency native ít tài liệu hỗ trợ cho .NET, rủi ro implement sai cao cho dự án 1 người maintain); (2) toàn bộ dependency là managed assembly được SignPath ký cùng lô với `ParentalGuard.Service.exe` (`DEV-012`) — không phát sinh thêm native DLL rời cần quản lý chữ ký/supply-chain riêng (liên hệ `SEC-014`).

### 4.2 Tham số chính thức (ĐÃ CHỐT bởi chủ dự án 2026-09-20 — phương án nhẹ hơn đề xuất khởi điểm; vẫn benchmark xác nhận ở Đợt 3)

`PWD-011` đã tường minh giao việc "benchmark cụ thể trên máy cấu hình thấp" cho bước thiết kế/implement — không phải gap cần `spec-maintainer`. Đề xuất khởi điểm ban đầu ở file này là `m=65536 (64 MiB), t=3, p=2`; chủ dự án đã xem xét và **chốt phương án NHẸ HƠN** (ADR-72, cập nhật 2026-09-20), ưu tiên trải nghiệm mượt trên máy yếu/cũ, vẫn giữ trên sàn an toàn tối thiểu OWASP:

| Tham số | Giá trị chính thức | Lý do |
|---|---|---|
| `memory_kb` (m) | `32768` (32 MiB) | ĐÃ CHỐT bởi chủ dự án 2026-09-20 — nhẹ hơn đề xuất khởi điểm (64 MiB) để ưu tiên trải nghiệm mượt trên máy yếu/cũ; vẫn cao hơn ~1.7× sàn tối thiểu OWASP (19 MiB, xem dòng cuối bảng) nên vẫn trên sàn an toàn tối thiểu, đúng đe doạ mô hình `PWD-014` |
| `iterations` (t) | `2` | ĐÃ CHỐT bởi chủ dự án 2026-09-20 cùng quyết định trên — cận dưới dải khuyến nghị OWASP (2-5 tuỳ mức memory); bù lại bằng `parallelism=2` (cấu hình sàn tối thiểu OWASP tương ứng chỉ dùng `p=1`), nên tổng thể vẫn mạnh hơn cấu hình sàn tối thiểu OWASP dù `t` ở cận dưới |
| `parallelism` (p) | `2` | Đủ tận dụng đa lõi phổ biến trên máy Windows mục tiêu mà không phụ thuộc `Environment.ProcessorCount` runtime (tránh 2 máy khác nhau tạo cost khác nhau cho cùng 1 policy — giá trị cố định, nhúng hằng số); giữ nguyên, không đổi trong lần chốt 2026-09-20 |
| Ngân sách UX mục tiêu | 300 ms – 1500 ms / lần verify trên máy tầm trung (dự kiến thấp hơn cận trên nhờ tham số nhẹ hơn — xác nhận số liệu thực tế qua benchmark Đợt 3) | Modal xác thực không phải hot-path (không gọi liên tục), chấp nhận độ trễ vài trăm ms–1 giây |
| Sàn tối thiểu nếu benchmark máy rất yếu vẫn vượt ngân sách | `m ≥ 19456` (19 MiB), `t ≥ 2`, `p ≥ 1` | Đúng mức khuyến nghị OWASP tối thiểu — không hạ thấp hơn dù benchmark máy yếu cho kết quả chậm, chấp nhận độ trễ cao hơn thay vì giảm dưới sàn an toàn; giữ nguyên như đã chốt trước đây, không đổi |

`m=32768, t=2, p=2` là giá trị **chính thức** (không còn là "khởi điểm đề xuất" chờ benchmark quyết định số cuối cùng như bản v0.1.0). `feature-dev` vẫn benchmark trên máy cấu hình thấp thực tế ở Đợt 3 để xác nhận nằm trong ngân sách UX ở trên; nếu benchmark cho thấy máy rất yếu vẫn vượt ngân sách, được phép hạ tới đúng sàn tối thiểu OWASP ở dòng cuối bảng (không hạ thấp hơn); ghi lại số liệu benchmark thực tế vào changelog file này khi có.

### 4.3 Salt & định dạng hash lưu trữ

- Salt: **16 byte (128-bit)**, sinh bằng `RandomNumberGenerator.GetBytes(16)`, ngẫu nhiên riêng cho mỗi lần hash (`PWD-012`) — không tái sử dụng salt giữa mật khẩu và Recovery Key dù cùng 1 lần setup.
- Hash output: 32 byte (256-bit).
- PHC string tự dựng theo chuẩn Argon2 PHC format (Konscious không tự sinh chuỗi này, `Service` build thủ công theo đúng cấu trúc chuẩn cộng đồng, để tương thích công cụ verify độc lập nếu cần sau này):
  ```
  $argon2id$v=19$m=<memory_kb>,t=<iterations>,p=<parallelism>$<base64(salt)>$<base64(hash)>
  ```
- Lưu đúng vào `password.hash`/`recovery_key.hash` (string) **và** đồng bộ `argon2_params` (object) trong cùng 1 lần ghi `auth.dat` — 2 nơi trùng thông tin có chủ đích (PHC string là nguồn sự thật để verify, `argon2_params` chỉ để đọc nhanh không cần parse chuỗi) — đã đúng shape có sẵn ở `04` mục 4, không đổi.
- Verify: parse PHC string lấy lại `m`/`t`/`p`/salt, hash lại credential vừa nhận với đúng tham số đó, so sánh bằng `CryptographicOperations.FixedTimeEquals` (constant-time, tái dùng đúng nguyên tắc đã áp dụng cho HMAC ở `03` mục 5.4).

## 5. Bảo vệ plaintext trong bộ nhớ (Runtime memory hygiene, `PWD-050`/`051`)

Nguyên tắc bao trùm — **áp dụng nhất quán đúng tinh thần `IMG-003`** đã hiện thực ở `05-image-pipeline-architecture.md` mục 6: mỗi bước tự chịu trách nhiệm zero-out dữ liệu nhạy cảm nó vừa dùng xong, trong khối `try/finally` cục bộ, bằng lệnh đồng bộ (không phụ thuộc Garbage Collector).

### 5.1 Vì sao không dùng `SecureString` (ADR-74)

`PWD-050` cho phép cả 2 phương án ("`SecureString` cân nhắc, hoặc tự quản lý `byte[]` và ghi đè 0"). Chọn phương án 2: **`byte[]` tự quản lý, ghi trên vùng nhớ *pinned*** (`GC.AllocateArray<byte>(n, pinned: true)`, .NET 5+, cấp phát trực tiếp trên Pinned Object Heap — GC không di chuyển/copy ngầm trong lúc compact, tránh để lại bản sao rải rác) + `CryptographicOperations.ZeroMemory(span)` để ghi 0 (API chuyên dụng cho mục đích bảo mật, được thiết kế để **không bị trình biên dịch/JIT tối ưu hoá loại bỏ** như có thể xảy ra với `Array.Clear` gọi ngay trước khi biến hết scope — khác biệt quan trọng so với zero-out buffer ảnh ở `IMG-003`, nơi không có rủi ro dead-store-elimination tương tự vì buffer còn được dùng tiếp ở bước sau). Không dùng `SecureString` vì Microsoft khuyến cáo tránh dùng cho code mới từ .NET Core trở đi (API cũ, không còn được đầu tư phát triển).

### 5.2 IPC: field mật khẩu dùng `bytes`, không dùng `string` (ADR-73)

Mọi field mang plaintext credential trong message Protobuf (mục 7) khai báo kiểu `bytes` (UTF-8 encode), **không** dùng `string` — vì `System.String` trong .NET bất biến (immutable), không thể ghi đè 0 tin cậy (mọi phép toán trên string tạo bản sao mới, bản cũ vẫn tồn tại trong heap cho tới khi GC dọn, không đồng bộ/không kiểm soát được thời điểm — vi phạm trực tiếp tinh thần `PWD-050`).

**Quy tắc này áp dụng cho MỌI field credential — KHÔNG CÓ NGOẠI LỆ (sửa v0.3.0)**. Bản v0.1.0/v0.2.0 để sót `recovery_key_plaintext`/`new_recovery_key_plaintext` (mục 7) ở kiểu `string`, kèm 1 dòng comment tự nhận "ngoại lệ duy nhất" trong `ipc.proto` nhưng **không có phân tích rủi ro nào biện minh cho ngoại lệ đó** — đây là lỗi thiết kế, phát hiện qua audit bảo mật Đợt 3 (FAIL 1, `security-privacy-auditor`), không phải đánh đổi có chủ đích. Đã sửa cả 3 field (`SetInitialPasswordResponse.recovery_key_plaintext`, `ChangePasswordResponse.new_recovery_key_plaintext`, `RecoveryResetResponse.new_recovery_key_plaintext`, mục 7) sang `bytes`, đồng bộ `ipc.proto`. Field Recovery Key có 1 khác biệt thật sự nhưng chỉ ở **THỜI ĐIỂM zero**, không phải ở kiểu dữ liệu hay có được miễn trừ zero — xem mục 5.5/ADR-83.

**Giới hạn thực tế cần ghi nhận minh bạch (residual risk, cùng tinh thần cách `06` mục 2.2 ghi nhận rủi ro dư MIC no-read-up)**: bản thân Protobuf (`Google.Protobuf`) tạo tối thiểu 2 bản sao bất biến không thể zero bằng API công khai thông thường — (a) `ByteString` nội bộ (khi build message để gửi, và khi deserialize lúc nhận), (b) buffer `N` byte đã serialize ra wire (`03` mục 2.3 framing). Biện pháp giảm thiểu (không loại bỏ hoàn toàn được do giới hạn của framework serialization, khác hẳn buffer ảnh thô ở `IMG-003` vốn không đi qua tầng serialization nào):

1. `byte[]` pinned gốc (trước khi đưa vào `ByteString.CopyFrom`) — zero ngay trong `finally` sau khi gửi/dùng xong.
2. Dùng `Google.Protobuf.UnsafeByteOperations.UnsafeGetBuffer(byteString)` để lấy trực tiếp buffer nội bộ của `ByteString` (không copy thêm) — gọi `CryptographicOperations.ZeroMemory` lên buffer đó **ngay sau khi** giá trị đã được dùng xong (bên gửi: ngay sau khi serialize xong frame; bên nhận: ngay sau khi verify/hash xong) — giảm tối đa thời gian tồn tại của bản sao này dù không đưa về 0 tức thời tuyệt đối như buffer ảnh.
3. Đây là kênh **chỉ nội bộ máy** (Named Pipe kernel object, ACL + HMAC, không network — `03` mục 2.2/5) — rủi ro dư này chỉ có ý nghĩa nếu kẻ tấn công đã có khả năng đọc bộ nhớ tiến trình `Service`/`UI` (memory dump), tức đã vượt qua ranh giới bảo mật ở mức cao hơn nhiều so với "gõ sai mật khẩu" — cùng mức chấp nhận rủi ro đã áp dụng cho MIC no-read-up ở `06`.

Phương án đã cân nhắc và loại (ADR-73, bổ sung): tách credential ra khỏi thân Protobuf, gửi như 1 đoạn byte thô ngoài khung (tương tự cách `03` ADR-17 giữ chữ ký HMAC ngoài message) để tránh hoàn toàn bản sao `ByteString`. **Không chọn** — thêm 1 định dạng framing đặc thù chỉ cho riêng loại message này làm tăng độ phức tạp parser/tăng rủi ro implement sai cho lợi ích bảo mật biên (kẻ tấn công đã cần đọc được RAM tiến trình), không tương xứng với mức độ rủi ro còn lại, đi ngược tinh thần đơn giản hoá đã áp dụng nhất quán trong dự án.

### 5.3 Luồng phía `UI`

- `PasswordBox.Password` (WinUI 3) trả về `string` bất biến — **giới hạn nền tảng đã biết, không có cách khắc phục hoàn toàn** (ghi nhận rõ, không giả vờ đã giải quyết): ngay khi đọc giá trị, `UI` lập tức `Encoding.UTF8.GetBytes` vào `byte[]` pinned, gán lại `PasswordBox.Password = string.Empty` (best-effort dọn UI, không xoá được bản gốc trong heap managed).
- Build message → gửi qua pipe → `finally`: zero `byte[]` pinned cục bộ (mục 5.1) + zero buffer `ByteString` vừa build (mục 5.2 bước 2).

### 5.4 Luồng phía `Service`

- Nhận message → lấy buffer credential qua `UnsafeByteOperations.UnsafeGetBuffer` (không copy thêm) → dùng trực tiếp cho Argon2id hash/verify → **ngay sau khi verify xong** (dù thành công hay thất bại), `finally`: zero buffer đó bằng `CryptographicOperations.ZeroMemory`.
- Không log giá trị credential dưới bất kỳ log level nào (`PWD-051`) — bao gồm cả log lỗi/exception (message exception không bao giờ nội suy giá trị credential vào chuỗi).
- `auth.dat` không bao giờ chứa plaintext ở bất kỳ bước trung gian nào — chỉ ghi đúng 1 lần cuối cùng: PHC hash string đã tính xong (`PWD-010`).

### 5.5 Trường hợp đặc biệt: Recovery Key plaintext — cùng KIỂU `bytes`, khác THỜI ĐIỂM zero (ADR-83, bổ sung v0.3.0)

Recovery Key plaintext (field `recovery_key_plaintext`/`new_recovery_key_plaintext`, mục 7) khác về **bản chất sử dụng** so với mọi credential khác ở file này (mật khẩu, mật khẩu cũ, Recovery Key *nhập vào* lúc khôi phục): nó là giá trị `Service` **sinh ra** để **đưa cho người dùng xem và chép lại** (`PWD-030`), không phải giá trị `Service` nhận vào để verify rồi bỏ đi ngay. Nguyên tắc "zero ngay sau khi dùng xong" ở mục 5 vẫn áp dụng nguyên vẹn — chỉ khác **"dùng xong" nghĩa là gì** cho 1 giá trị đầu ra thay vì đầu vào: với Recovery Key plaintext, "dùng xong" là thời điểm `Service` đã **chuyển giao xong** giá trị đó cho `UI` qua IPC, không phải thời điểm hash xong (hash chỉ là 1 bước trung gian, `Service` còn phải giữ buffer sống thêm để đưa vào response).

**Quy tắc thời điểm zero chính thức, áp dụng cho cả 3 handler `feature-dev` cần sửa** (`HandleSetInitialPasswordAsync`, `HandleChangePasswordAsync` khi `regenerate_recovery_key=true`, `HandleRecoveryResetAsync`):

1. Sinh Recovery Key plaintext vào **đúng 1 buffer `byte[]` pinned** (mục 5.1) — dùng buffer này cho cả (a) hash Argon2id (mục 4.3/6.3) lẫn (b) build vào field response tương ứng (`ByteString.CopyFrom` hoặc tương đương). Không tạo thêm bản sao trung gian nào ngoài phạm vi đã có ở mục 5.2 (pinned array gốc + `ByteString` nội bộ Protobuf).
2. `Service` **await ghi xong** frame response vào `NamedPipeServerStream` (tương đương điểm mà `03-ipc-communication.md` coi là "đã gửi" — sau khi `WriteAsync`/`FlushAsync` của khung length-prefixed hoàn tất, không phân biệt UI đã đọc hay chưa vì đó là trách nhiệm phía nhận). Tại thời điểm này, giá trị đã rời khỏi ranh giới `Service` — về mặt bảo mật, dữ liệu từ đây "thuộc về" phiên hiển thị phía `UI` (`Service` không còn lý do nghiệp vụ nào để giữ tiếp).
3. Ngay sau bước 2 (trong `finally`, chạy **bất kể** ghi thành công hay lỗi/exception giữa chừng — không để sót buffer sống nếu pipe gãy giữa chừng): zero pinned array gốc (`CryptographicOperations.ZeroMemory`) **và** buffer `ByteString` vừa build (`UnsafeByteOperations.UnsafeGetBuffer` + `ZeroMemory`, đúng kỹ thuật đã có ở mục 5.2 bước 1-2) — không có gì khác biệt về **kỹ thuật** zero so với credential khác, chỉ khác **thời điểm** trigger (sau khi gửi, thay vì sau khi verify).
4. **`AuthState.PendingSetup` KHÔNG còn giữ field `recovery_key_plaintext`** (sửa so với v0.1.0/v0.2.0 mục 7.1) — struct chỉ còn `{ password_hash_phc, recovery_key_hash_phc, setup_token, created_at_unix_ms }`. Lý do: bước xác nhận `ConfirmRecoveryKeySavedRequest` (mục 7.1) chỉ cần ghi `auth.dat` bằng **hash** đã tính sẵn ở bước 1, không bao giờ cần đọc lại plaintext — giữ plaintext sống trong `PendingSetup` tới tận lúc Confirm (tối đa **30 phút**, thời hạn pending) là cửa sổ tồn tại dư thừa không cần thiết, đã bị thu hẹp xuống còn đúng khoảng thời gian build+gửi response (mili-giây) theo quy tắc ở trên.

**Vì sao không chọn "zero ngay sau khi hash xong" (giống credential đầu vào khác)**: bất khả thi — buffer còn phải sống tiếp để build+gửi response chứa chính giá trị đó; zero sớm hơn sẽ gửi rác về `UI` thay vì Recovery Key thật.

**Vì sao không chọn "để UI tự lo, Service không cần quan tâm thời điểm"**: vi phạm nguyên tắc mỗi bước tự chịu trách nhiệm zero dữ liệu nó vừa dùng xong (mục 5 mở đầu) — `Service` là nơi sinh ra giá trị, phải chủ động dọn phần của mình ngay khi hết trách nhiệm, độc lập với việc `UI` có tự dọn tốt phần của nó hay không (thiết kế memory hygiene phía `UI` cho màn hình hiển thị Recovery Key thuộc `10-ui-architecture.md`, Đợt 6 — chưa thiết kế, không phải phạm vi file này, nhưng không phải lý do để `Service` trì hoãn zero phần của mình).

## 6. Recovery Key — format sinh, xác thực (`PWD-030`–`033`)

### 6.1 Format (ADR-75, ĐÃ CHỐT bởi chủ dự án 2026-09-20)

`PWD-030` cho khoảng "24-32 ký tự". Đề xuất khởi điểm ở bản v0.1.0 là 32 ký tự (biên trên); chủ dự án đã chốt **24 ký tự** (biên dưới của khoảng đã chốt trong spec — vẫn là lựa chọn HOW trong phạm vi WHAT đã cho, không phát minh thêm yêu cầu), ưu tiên chuỗi gọn hơn cho phụ huynh chép tay/lưu giấy: sinh **15 byte ngẫu nhiên** (`RandomNumberGenerator.GetBytes(15)` = 120-bit entropy) → encode **Crockford Base32** (bảng chữ 32 ký tự `0123456789ABCDEFGHJKMNPQRSTVWXYZ`, loại trừ `I`/`L`/`O`/`U` để giảm nhầm lẫn thị giác `1`/`I`/`l`, `0`/`O`, và giảm khả năng vô tình tạo từ nhạy cảm) → mỗi byte đầu vào tương ứng đúng 5 bit/ký tự, 15 byte × 8 bit ÷ 5 bit/ký tự = **24 ký tự tròn**, không cần padding. Hiển thị chia nhóm 4 ký tự/nhóm, 6 nhóm, nối bằng `-` (đúng ví dụ minh hoạ "`XXXX-XXXX-XXXX-XXXX`" ở `PWD-030`, mở rộng thêm nhóm cho đủ 24 ký tự thay vì đúng 16 ký tự literal của ví dụ — ví dụ trong spec chỉ minh hoạ *kiểu* hiển thị, không phải số nhóm cố định).

**Xác nhận đủ an toàn theo `PWD-030`**: 120-bit entropy (2^120 tổ hợp) vượt xa mọi ngưỡng thực tế cho tấn công brute-force offline lẫn online — làm mốc so sánh, AES-128 (chuẩn mã hoá được coi là an toàn cho hầu hết ứng dụng tới nhiều thập kỷ tới) cũng chỉ có 128-bit key space; 120-bit vẫn cùng bậc độ lớn, không phải một khoảng giảm entropy đáng kể so với phương án 160-bit (32 ký tự) đã cân nhắc trước đó. Kết hợp thêm 2 lớp phòng thủ độc lập đã có: (1) Recovery Key không bao giờ lộ ra ngoài trừ đúng 1 lần hiển thị lúc Onboarding (`PWD-030`), không phải thứ kẻ tấn công đoán mù từ xa; (2) rate-limit dùng chung bộ đếm với mật khẩu (mục 7.7/ADR-76) khiến brute-force online hoàn toàn bất khả thi bất kể độ dài key. 24 ký tự vẫn nằm đúng trong khoảng WHAT đã chốt ở `PWD-030` (24-32) — không phải lựa chọn ngoài phạm vi.

### 6.2 Chuẩn hoá lúc xác thực

Trước khi hash (lúc sinh) và trước khi so sánh (lúc verify): **không cần chuẩn hoá lúc sinh** (luôn sinh đúng dạng canonical uppercase, không dấu gạch). Lúc verify, input người dùng gõ lại được chuẩn hoá: loại bỏ dấu `-` và khoảng trắng, chuyển toàn bộ về chữ hoa, rồi mới hash để so sánh — nhất quán tinh thần giảm rủi ro tự khoá mình do sai định dạng đã áp dụng ở `PWD-035` cho câu hỏi bảo mật. Không làm giảm entropy (chuẩn hoá case không thu hẹp không gian giá trị vì bảng chữ chỉ sinh ra chữ hoa).

### 6.3 Lưu trữ & vòng đời

Hash Recovery Key (sau chuẩn hoá) bằng đúng tham số Argon2id ở mục 4 (không dùng tham số nhẹ hơn — `PWD-031`: "cùng cơ chế bảo mật như mật khẩu chính"), lưu `recovery_key.hash`/`recovery_key.argon2_params`/`recovery_key.used=false` (`04` mục 4, không đổi). Sau khi dùng thành công 1 lần (mục 7.5): sinh Recovery Key **mới hoàn toàn**, ghi đè toàn bộ object `recovery_key` (bao gồm reset `used=false` cho key mới), vô hiệu hoàn toàn key cũ (`PWD-032`).

## 7. IPC — message schema & luồng nghiệp vụ

Toàn bộ message dưới đây bổ sung vào khối field **80-99** (kênh `UI`) đã dành sẵn ở `03-ipc-communication.md` mục 3.1 — chiếm **80-91** (domain Password/Auth), để **92-99** cho nhu cầu Đợt 6 (Dashboard query khác). Amendment `.proto` thực hiện ở `03-ipc-communication.md` (xem changelog file đó), nội dung/thiết kế nghiệp vụ đầy đủ trình bày ở đây.

```protobuf
// --- Đợt 3, PWD-0xx: Password & Authentication (field 80-91 của khối UI 80-99) ---

message AuthStatusQuery {}                                    // field 90

message AuthStatusResponse {                                  // field 91
  bool password_configured = 1;   // false = chưa từng hoàn tất Onboarding (PWD-004/030a) — UI hiển thị màn Set Password
}

message SetInitialPasswordRequest {                            // field 80
  bytes password = 1;
  reserved 10 to 15; // Phase 2 (PWD-034): hook thiết lập câu hỏi bảo mật cùng lúc, KHÔNG thiết kế nội dung ở đây
}

message SetInitialPasswordResponse {                           // field 81
  SetupResult result                = 1;
  bytes       recovery_key_plaintext = 2; // UTF-8; CHỈ set khi result=SUCCESS — hiển thị đúng 1 lần (PWD-030); zero theo ADR-83/mục 5.5
  bytes       setup_token            = 3; // correlate với ConfirmRecoveryKeySavedRequest, 16 byte ngẫu nhiên
}

enum SetupResult {
  SETUP_RESULT_UNSPECIFIED = 0;
  SUCCESS               = 1;
  PASSWORD_TOO_LONG      = 2; // > 50 ký tự, PWD-002a — kiểm tra lại phía backend dù UI đã chặn
  ALREADY_CONFIGURED     = 3; // auth.dat đã tồn tại đầy đủ — phải dùng luồng đổi mật khẩu (mục 7.4), không setup lại
}

message ConfirmRecoveryKeySavedRequest {                       // field 82
  bytes setup_token = 1;
  bool  confirmed    = 2; // true = đã tick "Tôi đã lưu lại Recovery Key" (PWD-030a) + bấm Tiếp tục
}

message ConfirmRecoveryKeySavedResponse {                      // field 83
  ConfirmResult result = 1;
}

enum ConfirmResult {
  CONFIRM_RESULT_UNSPECIFIED = 0;
  PERSISTED     = 1; // auth.dat đã ghi thành công — Onboarding coi như hoàn tất phần mật khẩu
  TOKEN_EXPIRED = 2; // quá thời gian giữ pending (mục 7.2) — UI phải quay lại bước nhập mật khẩu từ đầu
  TOKEN_NOT_FOUND = 3; // Service vừa restart giữa chừng (mất state RAM) — coi như chưa thành công, quay lại từ đầu
}

message AuthVerifyRequest {                                    // field 84
  bytes  password       = 1;
  string action_context = 2; // hằng số đã định nghĩa ở mục 7.3 — vd "uninstall", "pause_monitoring"
  reserved 10 to 15; // Phase 2 (PWD-034): hook xác thực bằng câu hỏi bảo mật thay mật khẩu, KHÔNG thiết kế ở đây
}

message AuthVerifyResponse {                                   // field 85
  AuthResult result                       = 1;
  bytes      action_token                  = 2; // CHỈ set khi result=SUCCESS, 16 byte ngẫu nhiên, dùng 1 lần
  int64      action_token_expires_at_unix_ms = 3;
  int64      lockout_until_unix_ms          = 4; // CHỈ có ý nghĩa khi result=LOCKED_OUT
  uint32     consecutive_failures           = 5; // thông tin hiển thị, không phải cơ chế bảo mật
}

enum AuthResult {
  AUTH_RESULT_UNSPECIFIED = 0;
  SUCCESS          = 1;
  WRONG_PASSWORD   = 2;
  LOCKED_OUT       = 3;
}

message ChangePasswordRequest {                                // field 86
  bytes old_password             = 1;
  bytes new_password             = 2;
  bool  regenerate_recovery_key  = 3; // PWD-041 — UI hỏi trước, gửi lựa chọn luôn trong request
  reserved 10 to 15; // Phase 2 (PWD-034)
}

message ChangePasswordResponse {                               // field 87
  ChangeResult result                  = 1;
  bytes        new_recovery_key_plaintext = 2; // UTF-8; CHỈ set nếu regenerate_recovery_key=true và result=SUCCESS; zero theo ADR-83/mục 5.5
}

enum ChangeResult {
  CHANGE_RESULT_UNSPECIFIED = 0;
  SUCCESS               = 1;
  WRONG_OLD_PASSWORD     = 2;
  LOCKED_OUT             = 3;
  NEW_PASSWORD_TOO_LONG  = 4;
}

message RecoveryResetRequest {                                 // field 88
  bytes recovery_key = 1; // đã chuẩn hoá phía UI trước khi gửi (mục 6.2) — Service chuẩn hoá lại lần nữa, không tin UI
  bytes new_password = 2;
  reserved 10 to 15; // Phase 2 (PWD-034): fallback qua câu hỏi bảo mật nếu mất luôn Recovery Key
}

message RecoveryResetResponse {                                // field 89
  RecoveryResetResult result                  = 1;
  bytes               new_recovery_key_plaintext = 2; // UTF-8; CHỈ set nếu result=SUCCESS (PWD-032: luôn sinh key mới); zero theo ADR-83/mục 5.5
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

### 7.1 Luồng đặt mật khẩu lần đầu (Onboarding, `PWD-001`–`004`, `030`–`030a`)

```
UI                                              Service
 │── AuthStatusQuery ─────────────────────────▶│
 │◀── AuthStatusResponse{password_configured=false} ──│  (chưa từng setup → hiển thị màn Set Password)
 │                                               │
 │  (UI tự so khớp password/confirm 2 ô nhập –  │
 │   PWD-003, KHÔNG cần round-trip IPC)          │
 │                                               │
 │── SetInitialPasswordRequest{password} ──────▶│  Argon2id hash password (mục 4)
 │                                               │  sinh Recovery Key + hash (mục 6)
 │                                               │  GIỮ TRONG RAM (AuthState.PendingSetup),
 │                                               │  KHÔNG ghi auth.dat (PWD-030a — chưa xác nhận)
 │◀── SetInitialPasswordResponse{               │
 │      recovery_key_plaintext, setup_token} ───│
 │                                               │
 │  (UI hiển thị Recovery Key, checkbox          │
 │   "Tôi đã lưu lại", chờ user tick + bấm Tiếp) │
 │                                               │
 │── ConfirmRecoveryKeySavedRequest{             │
 │      setup_token, confirmed=true} ──────────▶│  Ghi auth.dat NGAY (PHC hash password +
 │                                               │  PHC hash recovery key), xoá PendingSetup khỏi RAM
 │◀── ConfirmRecoveryKeySavedResponse{PERSISTED}│
 │                                               │  (Onboarding coi như hoàn tất phần mật khẩu — PWD-004)
```

- **`AuthState.PendingSetup`**: struct RAM-only (`{ password_hash_phc, recovery_key_hash_phc, setup_token, created_at_unix_ms }` — **sửa v0.3.0**: bỏ field `recovery_key_plaintext`, xem lý do ở mục 5.5 điểm 4), tối đa **1 pending slot** tại 1 thời điểm (đúng giới hạn 1 kết nối `UI` đồng thời, `03` mục 6). Hết hạn sau **30 phút** không xác nhận (ADR, giá trị UX không ảnh hưởng bảo mật — cho phụ huynh đủ thời gian tìm giấy bút) — hết hạn thì `ConfirmRecoveryKeySavedRequest` trả `TOKEN_EXPIRED`, UI quay lại bước nhập mật khẩu từ đầu.
- Nếu `Service` crash/restart giữa 2 bước (Watchdog respawn theo `BE-023`): `PendingSetup` mất theo RAM → `ConfirmRecoveryKeySavedRequest` trả `TOKEN_NOT_FOUND` → đúng hệ quả `PWD-030a` ("coi như thiết lập chưa thành công", không có `auth.dat` nào được ghi dở dang — không vi phạm `PWD-010` vì chưa từng ghi xuống đĩa).
- Recovery Key plaintext (buffer cục bộ, **không** phải field của `PendingSetup`) được zero ngay trong `finally` sau khi `SetInitialPasswordResponse` đã ghi xong vào pipe — **không** đợi tới lúc `ConfirmRecoveryKeySavedRequest` xử lý xong như bản v0.1.0/v0.2.0 (sửa theo ADR-83/mục 5.5, thu hẹp cửa sổ tồn tại từ tối đa 30 phút xuống còn mili-giây — bước Confirm chỉ cần `recovery_key_hash_phc` đã tính sẵn, chưa từng cần đọc lại plaintext).

### 7.2 Luồng xác thực hành động nhạy cảm — "Auth Modal" (`PWD-020`–`023`)

`PWD-020` liệt kê 5 hành động: gỡ cài đặt, tạm dừng giám sát, đổi ngưỡng nhạy cảm, xem/xoá log, đổi mật khẩu. **4 hành động đầu chưa có message request riêng** (thuộc phạm vi Đợt 4/5/6, chưa thiết kế) — file này chỉ đảm bảo sẵn **cổng xác thực lõi** để các Đợt đó tái sử dụng, đúng lý do `ROADMAP.md` đặt Đợt 3 trước Đợt 4/5 ("Anti-tamper và Pause đều cần xác thực mật khẩu làm gate — phải có trước"). Riêng "đổi mật khẩu" thiết kế trọn vẹn ở mục 7.4 (không qua cổng chung này — mục 7.4 giải thích lý do).

**Hợp đồng cổng chung (ADR-78)**:

```
UI                                              Service
 │── AuthVerifyRequest{                          │
 │      password, action_context="pause_monitoring"} ─▶│
 │                                               │  1. Kiểm tra lockout hiện hành (mục 7.7) — nếu đang
 │                                               │     khoá: trả LOCKED_OUT NGAY, KHÔNG hash (tiết kiệm
 │                                               │     CPU, không tính thêm vào consecutive_failures)
 │                                               │  2. Nếu không khoá: Argon2id verify (mục 4.3)
 │                                               │  3. Đúng: reset consecutive_failures=0, ghi audit.log
 │                                               │     AuthAttempt{result=success}, sinh action_token
 │                                               │     16 byte ngẫu nhiên, lưu AuthState.PendingActionTokens
 │                                               │     [token] = {action_context, expires_at = now+15s}
 │                                               │     (RAM-only, KHÔNG persist)
 │                                               │     Sai: tăng consecutive_failures, tính delay theo
 │                                               │     bảng PWD-021 (mục 7.7), ghi auth.dat NGAY (đồng bộ,
 │                                               │     trước khi trả response — ADR-26 tinh thần), ghi
 │                                               │     audit.log AuthAttempt{result=wrong_password}
 │◀── AuthVerifyResponse{result, action_token?, │
 │      lockout_until_unix_ms?, ...} ───────────│
 │                                               │
 │  (nếu SUCCESS: UI gửi tiếp lệnh nghiệp vụ     │
 │   thật — PauseMonitoringRequest v.v., THIẾT   │
 │   KẾ Ở ĐỢT TƯƠNG ỨNG — phải kèm field         │
 │   action_token vừa nhận, Service verify token │
 │   còn hạn + đúng action_context + dùng 1 lần  │
 │   rồi xoá khỏi PendingActionTokens TRƯỚC KHI  │
 │   thực thi hành động nhạy cảm thật)            │
```

- `action_token` **KHÔNG phải session "remember me"** — TTL 15 giây, dùng 1 lần, chỉ RAM (mất hoàn toàn khi `Service` restart) — đúng nghĩa đen "mỗi khi" của `PWD-020` (mỗi hành động nhạy cảm đòi hỏi 1 lượt `AuthVerifyRequest` mới, không có khái niệm "đã đăng nhập rồi khỏi hỏi lại trong N phút"). Vai trò của token chỉ là cầu nối kỹ thuật giữa bước "xác thực" (message này) và bước "thực thi" (message khác, thiết kế sau) khi UI cần 2 round-trip riêng biệt — **không phải** cơ chế bảo mật chính (danh tính `UI` đã được xác thực ở tầng transport qua chữ ký code-signing, `03` mục 4.2); mất/lộ 1 token chỉ hữu dụng trong đúng 15 giây và đúng `action_context` đó.
- **`action_context` — hằng số quy ước sẵn cho các Đợt sau** (tránh mỗi Đợt tự đặt chuỗi tuỳ tiện, không nhất quán):
  | Hằng số | Dùng cho | Thiết kế message thật ở |
  |---|---|---|
  | `"uninstall"` | Gỡ cài đặt (`ANTI-020`) | Đợt 4, `09-anti-tamper-architecture.md` |
  | `"pause_monitoring"` | Tạm dừng giám sát (`PAUSE-001`/`004`) | Đợt 5, amendment `02-process-architecture.md`/`03` |
  | `"change_sensitivity_threshold"` | Đổi ngưỡng nhạy cảm | Đợt 6, `10-ui-architecture.md` |
  | `"view_audit_log"` | Xem log | Đợt 6, `10-ui-architecture.md` mục 5 (`AuditLogQuery`) |
  | `"delete_audit_log"` | Xoá log | **Không dùng ở Đợt 6** — `Specification/03-frontend-ui-spec.md` (`S3`) không có yêu cầu tính năng xoá audit log nào (mâu thuẫn hash-chain tamper-evident, `MISC-010`); hằng số giữ nguyên "reserved", không xoá khỏi bảng này phòng khi có yêu cầu sản phẩm mới sau này |
  | `"manage_whitelist"` (mới Đợt 6) | Thêm/xoá process khỏi whitelist false-positive (`MISC-030`) | Đợt 6, `10-ui-architecture.md` mục 5 (`MarkFalsePositiveRequest`/`RemoveWhitelistEntryRequest`) |

  File này chỉ đặt tên hằng số + hợp đồng token — **không** thiết kế trước message `PauseMonitoringRequest`/`UninstallRequest`/... (thuộc thẩm quyền kiến trúc của Đợt tương ứng khi tới lượt, tránh phát minh trước chi tiết nghiệp vụ ngoài phạm vi Đợt 3).

### 7.3 (gộp vào 7.2 — bảng hằng số `action_context`)

### 7.4 Luồng đổi mật khẩu (`PWD-040`/`041`)

**Không qua cổng `AuthVerifyRequest`/`action_token`** — `ChangePasswordRequest` tự mang `old_password` làm bằng chứng xác thực ngay trong chính nó (đúng `PWD-040`: "yêu cầu nhập mật khẩu cũ trước khi đổi"), xử lý trong 1 round-trip duy nhất (ADR-79 — đơn giản hơn, giảm 1 lượt IPC, vẫn đúng nguyên tắc xác thực-trước-hành-động vì `old_password` chính là thứ cần verify):

```
UI ── ChangePasswordRequest{old_password, new_password,
        regenerate_recovery_key} ────────────────────▶ Service
                                                          1. Kiểm tra lockout (mục 7.7) — như 7.2 bước 1
                                                          2. Verify old_password (mục 4.3)
                                                          3. Đúng: kiểm tra new_password ≤ 50 ký tự (PWD-002a)
                                                             → hash new_password, ghi đè password.hash/
                                                             argon2_params/updated_at_unix_ms
                                                             → nếu regenerate_recovery_key=true: sinh
                                                             Recovery Key mới (mục 6), ghi đè recovery_key.*
                                                             → ghi auth.dat 1 lần (atomic)
                                                             → reset consecutive_failures=0
                                                             → audit.log: ConfigChanged{field="password"}
                                                             Sai: tăng consecutive_failures (mục 7.7)
UI ◀── ChangePasswordResponse{result, new_recovery_key_plaintext?} ─
```

### 7.5 Luồng khôi phục qua Recovery Key (`PWD-032`/`033`)

```
UI ── RecoveryResetRequest{recovery_key, new_password} ──▶ Service
                                                              1. Kiểm tra lockout (mục 7.7 — dùng CHUNG
                                                                 bộ đếm với mật khẩu, mục 7.7)
                                                              2. Chuẩn hoá recovery_key (mục 6.2, lặp lại
                                                                 phía Service dù UI đã chuẩn hoá — không
                                                                 tin input UI)
                                                              3. Verify recovery_key.hash + kiểm tra
                                                                 recovery_key.used == false
                                                              4. Đúng: hash new_password, ghi đè
                                                                 password.hash; sinh Recovery Key MỚI
                                                                 (bắt buộc, không tuỳ chọn — PWD-032),
                                                                 ghi đè recovery_key.* (used=false cho
                                                                 key mới); reset consecutive_failures=0;
                                                                 audit.log: AuthAttempt{result=recovery_success}
                                                                 Sai (hoặc used=true): tăng consecutive_failures
UI ◀── RecoveryResetResponse{result, new_recovery_key_plaintext?, lockout_until_unix_ms?} ─
```

### 7.6 Chỗ mở rộng Phase 2 (`PWD-034`/`035` — không thiết kế ở đây)

Mỗi message có credential (`SetInitialPasswordRequest`, `AuthVerifyRequest`, `ChangePasswordRequest`, `RecoveryResetRequest`) đã `reserved 10 to 15` — chừa chỗ thêm field kiểu "trả lời câu hỏi bảo mật" thay thế/bổ sung mật khẩu khi Phase 2 triển khai, không cần đổi field number đã dùng. Không thiết kế nội dung cụ thể (ngoài phạm vi `ROADMAP.md` hiện tại).

### 7.7 Rate-limit — thực thi cụ thể (`PWD-021`–`023`, số liệu đã chốt đủ trong spec)

| Lần sai liên tiếp (`consecutive_failures` sau khi tăng) | Delay trước lần thử tiếp theo (`delay_until_unix_ms = now + delay`) |
|---|---|
| 1-3 | 0 (cho thử lại ngay) |
| 4-5 | 30 giây |
| 6-8 | 5 phút |
| ≥ 9 | 30 phút + ghi `audit.log` **`AuthBruteForceThresholdReached`** (event mới, bổ sung `04` mục 5.1 cùng lượt — mức cảnh báo cao, `detail={consecutive_failures, action_context}`) |

- **Bộ đếm dùng CHUNG cho cả mật khẩu lẫn Recovery Key** (ADR-76) — 1 object `auth.dat.rate_limit` duy nhất (đúng schema đã có ở `04`, không tách 2 bộ đếm). Lý do: cả 2 credential bảo vệ cùng 1 tài nguyên (quyền điều khiển app); tách riêng chỉ tạo thêm đường vòng tăng gấp đôi tổng số lần thử trước khi bị khoá nghiêm trọng mà không có lợi ích bảo mật rõ ràng; khớp đúng chữ "tương tự" ở `PWD-033`. Áp dụng luôn cho `SetInitialPasswordRequest` không — **không**, vì hành động này chỉ xảy ra đúng 1 lần lúc chưa từng có mật khẩu (không có "sai" để đếm, không có gì để brute-force).
- **Chống bypass qua chỉnh giờ hệ thống (`PWD-022`, ADR-77)**: `Service` giữ 1 "monotonic anchor" RAM-only tại lúc `Starting`: `(tick0 = Environment.TickCount64, wall0 = DateTime.UtcNow)`. Mọi lần kiểm tra lockout trong cùng phiên chạy dùng `trusted_now = wall0 + (Environment.TickCount64 - tick0)` thay vì tin thẳng `DateTime.UtcNow` — miễn nhiễm với việc đổi giờ hệ thống xảy ra **sau khi `Service` đã khởi động** (đa số tình huống thực tế, vì `Service` thường chạy liên tục dài hạn). Không cần thêm field persist mới trong `auth.dat` (tái dùng nguyên `last_failure_at_unix_ms`/`delay_until_unix_ms` đã có).
  - **Rủi ro dư còn lại** (ghi nhận minh bạch, không giả vờ giải quyết triệt để): đổi giờ hệ thống **trước khi** `Service` khởi động lại (vd đổi giờ rồi khởi động lại máy trong lúc đang bị khoá) vẫn có thể làm lệch `trusted_now` của phiên mới. Giảm thiểu bởi: (1) đổi giờ hệ thống trên Windows mặc định đòi hỏi `SeSystemtimePrivilege` — tài khoản Standard User (khuyến nghị cho tài khoản trẻ, `SEC-006`) **không** có quyền này theo mặc định; (2) khởi động lại máy là hành động ồn ào/dễ bị phụ huynh chú ý; (3) kể cả bypass được 1 lần khoá, các lần thử tiếp theo vẫn tiếp tục leo thang delay bình thường theo bảng trên, và ngưỡng ≥9 vẫn để lại dấu vết audit log mức cảnh báo cao.
- `Service` xử lý: **kiểm tra lockout TRƯỚC KHI hash** (`now < delay_until_unix_ms` theo `trusted_now` ở trên → trả `LOCKED_OUT` ngay, không tính thêm vào `consecutive_failures`, không tốn CPU Argon2id).
- Ghi `auth.dat` (rate_limit mới) **đồng bộ, trước khi** trả response cho mọi lần sai (ADR-26 gốc ở `04` — đảm bảo dù `Service` crash ngay sau đó, lần khởi động kế tiếp vẫn đọc đúng trạng thái đã tăng).
- `AuthState` (RAM) cần 1 khoá đồng bộ hoá đơn giản (lock/semaphore) quanh việc đọc-sửa-ghi `rate_limit`/`PendingSetup`/`PendingActionTokens` — `Service` vốn đa luồng (xử lý đồng thời pipe `Vision`/`Overlay`/`UI`), dù pipe `UI` giới hạn 1 kết nối, vẫn cần tránh race condition nội bộ (ADR thuần kỹ thuật, không liệt riêng vào bảng mục 8 vì là thực hành mặc định).

### 7.8 Fail-secure khi `auth.dat` TỒN TẠI nhưng KHÔNG đọc/giải mã được (bổ sung v0.2.0)

Gap phát sinh từ code thật Đợt 3 (`feature-dev` báo cáo lại, `docs/dependency-map.md` mục "Khoảng trống đã biết — Đợt 3"): file v0.1.1 chưa định nghĩa hành vi khi `auth.dat` **tồn tại** nhưng đọc/giải mã thất bại (`AuthDataCorruptException` ở `AuthDataStore.TryLoad` — 4 byte version header sai/thiếu, `CryptographicException` lúc DPAPI `Unprotect`, hoặc JSON không parse được/thiếu field) — khác hẳn nhánh fail-secure của `config.db` (`04-data-architecture.md` mục 6), vốn **chỉ áp dụng cho domain giám sát** (monitoring config), không áp dụng cho domain xác thực danh tính.

**Vì sao 2 domain KHÔNG dùng chung 1 công thức fail-secure**: `04` mục 6.2 xử lý `config.db` hỏng bằng cách nạp giá trị mặc định an toàn vào RAM rồi **ghi đè file mới ngay** (`monitoring_enabled=true`, sinh khoá HMAC mới...) — đúng vì mất `config.db` chỉ là mất *cấu hình*, và "giám sát BẬT" luôn là lựa chọn mặc định an toàn hơn "giám sát TẮT". `auth.dat` không có tương đương: mất khả năng đọc `auth.dat` là mất khả năng **xác minh danh tính phụ huynh**, không tồn tại "giá trị mặc định an toàn hơn" nào cho 1 credential — nếu áp dụng cùng công thức "hỏng → coi như chưa có → tự tạo lại" ở đây (vd trả `password_configured=false`, cho phép `SetInitialPasswordRequest` chạy lại), hệ quả trực tiếp là **kẻ tấn công chỉ cần cố ý làm hỏng 4 byte đầu file `auth.dat`** (đúng format `{4 byte version LE}{DPAPI(JSON)}` đã chốt ở `04` mục 4) để ép hệ thống quay về trạng thái "chưa từng setup" và tự đặt mật khẩu mới của chính họ — bypass toàn bộ lớp xác thực `PWD-0xx`. Vì vậy domain này áp dụng đúng nguyên tắc **Fail-secure** (`Architecture/01` mục 5, nguồn `BE-061a`/`ANTI-070`) theo chiều ngược lại với `04`: lỗi không rõ nguyên nhân → nghiêng về phía AN TOÀN HƠN nghĩa là **VẪN YÊU CẦU xác thực**, không bao giờ tự nới lỏng.

**Hành vi chính thức (ĐÃ CHỐT, khớp đúng implementation hiện tại của `AuthCoordinator`, không đổi code)**:

| Message | Khi `auth.dat` tồn tại nhưng không đọc/giải mã được |
|---|---|
| `AuthStatusQuery` | `password_configured = true` — chặn `SetInitialPasswordRequest` chạy lại, không bao giờ hiểu nhầm "chưa từng setup" |
| `SetInitialPasswordRequest` | `SetupResult.ALREADY_CONFIGURED` — không bao giờ ghi đè `auth.dat` bằng dữ liệu Setup mới |
| `AuthVerifyRequest` | `AuthResult.WRONG_PASSWORD` — không tính vào `consecutive_failures` (không có `rate_limit` hợp lệ để đọc/ghi), nhưng vẫn là từ chối, không phải "cho qua" |
| `ChangePasswordRequest` | `ChangeResult.WRONG_OLD_PASSWORD` |
| `RecoveryResetRequest` | `RecoveryResetResult.WRONG_RECOVERY_KEY` — **kể cả khi phụ huynh gõ đúng y hệt Recovery Key gốc đã lưu offline lúc Onboarding**, vì `recovery_key.hash` bên trong chính `auth.dat` cũng không đọc được (hệ quả tất yếu của "coi toàn file là 1 khối", không phải lỗi logic) |

- **Không phân biệt "corrupt" với "sai thật" qua bất kỳ response IPC nào** (ADR-81, mục 8) — cùng đúng 1 mã lỗi như khi phụ huynh gõ sai mật khẩu/Recovery Key thật, đúng tinh thần chống oracle đã áp dụng cho HMAC verify fail ở `03-ipc-communication.md` ADR-22: nếu phân biệt rõ 2 trường hợp qua response, kẻ tấn công cố tình phá hỏng file sẽ biết ngay "nỗ lực phá hoại đã thành công" và tiếp tục khai thác theo hướng đó — giữ đồng nhất response loại bỏ hoàn toàn oracle này. **Lưu ý phạm vi (sửa v0.3.0)**: bảo đảm này ban đầu chỉ đồng nhất **nội dung** response — chưa đồng nhất **thời gian** phản hồi, để lộ 1 oracle khác qua đo round-trip IPC (FAIL 3, `security-privacy-auditor` Đợt 3: đo thực nghiệm `never_configured≈3ms`, `corrupt≈0.02ms` so với `real_wrong_password≈102ms`) — xem mục 7.9/ADR-84 cho cơ chế bù thời gian.
- **Không tự sửa/wipe/regenerate `auth.dat`** ở bất kỳ nhánh nào — nếu tự động xoá file hỏng rồi cho Setup chạy lại (như cách `04` mục 6.2 bước 5 tự tái tạo `config.db`), đó chính xác là lỗ hổng bypass đã nêu ở trên. Đây là khác biệt cốt lõi giữa 2 domain, không phải thiếu sót khi copy công thức từ `04` sang.

**Đánh giá theo yêu cầu rà soát: đây có phải gap WHAT cần `spec-maintainer` không?** — **Không.** `Specification/06-password-management-spec.md` mục 5 ("Vì sao không dùng email/SMS reset") đã tường minh chấp nhận đúng đánh đổi này ở cấp chính sách: *"nếu phụ huynh làm mất cả mật khẩu lẫn Recovery Key, không có cách khôi phục nào khác ngoài gỡ cài đặt hoàn toàn (xoá dữ liệu) và cài lại — đây là đánh đổi có chủ đích"*. Về mặt hệ quả với hệ thống, "`auth.dat` không đọc được" và "phụ huynh nhớ sai/mất cả mật khẩu lẫn Recovery Key" **tương đương nhau tuyệt đối**: cả 2 đều khiến `Service` không còn cách nào xác minh đúng danh tính qua bất kỳ credential nào đang có — đây chính xác là kịch bản đã được chốt WHAT từ trước, không phải tình huống mới phát sinh cần quyết định sản phẩm thêm.
  - Đường thoát duy nhất đã chốt sẵn ở cấp spec (không phát minh thêm ở đây): **gỡ cài đặt hoàn toàn ở tầng hệ điều hành**, không phải qua custom uninstaller của app — xác nhận qua `Specification/05-anti-uninstall-tamper-spec.md` mục 4 (`ANTI-020`: uninstaller riêng luôn yêu cầu đúng mật khẩu mới cho gỡ chạy, *"Sai mật khẩu hoặc huỷ → không gỡ gì cả"* — nên cũng bị chặn y hệt trong tình huống này, không có luồng "quên mật khẩu" nào ở tầng uninstaller). Đường thoát thực tế thuộc nhóm "giới hạn kỹ thuật đã truyền thông minh bạch với phụ huynh" ở cùng file mục 6 (`ANTI-040`/`041`: Safe Mode, tài khoản Admin Windows khác, cài lại hệ điều hành/factory reset...) — **giới hạn đã biết và đã được chấp nhận ở cấp sản phẩm**, không phải khiếm khuyết phát sinh riêng cho trường hợp `auth.dat` corrupt.
  - **Không mở rộng kiến trúc để "vá" tình huống này êm hơn** (ví dụ: thêm 1 kênh phục hồi qua tài khoản Administrator Windows bỏ qua hẳn `auth.dat`) — làm vậy mở đúng lỗ hổng bypass đã cảnh báo ở trên, vì bất kỳ cơ chế "bỏ qua xác thực khi phát hiện điều gì đó bất thường" nào cũng là điểm yếu có thể bị lợi dụng bằng cách cố tình tạo ra chính điều "bất thường" đó. Nếu chủ dự án muốn có 1 đường phục hồi mềm hơn dành riêng cho lỗi đĩa/mất điện (khác bản chất với bị tấn công, nhưng hệ thống không có cách phân biệt 2 nguyên nhân này), đó là **quyết định đánh đổi bảo mật-khả dụng mới** — phải quay lại `Specification/06-password-management-spec.md` làm đúng quy trình archive, không tự quyết định ở tài liệu kiến trúc.

**Residual risk ghi nhận minh bạch (chưa implement, không bắt buộc cho Đợt 3)**: `AuthCoordinator` hiện chỉ ghi sự kiện phát hiện corrupt vào Windows Event Log qua `ILogger` (lúc khởi tạo) — **không** ghi vào `audit.log` (phụ huynh không xem được qua Dashboard, `BE-014`) và không gửi `ShowToastCommand` cảnh báo chủ động nào, khác hẳn cách `ConfigFallbackTriggered`/`BE-061b` xử lý `config.db` hỏng (có cả audit log lẫn Toast, `04` mục 6.2). Đây **không phải điều kiện bắt buộc để đóng gap này** — hành vi bảo mật ở trên đã đúng và đủ, độc lập với tính minh bạch này. Đề xuất cải thiện không bắt buộc (tech debt nhẹ, có thể làm ở Đợt sau, tái dùng nguyên `ShowToastCommand`/`reason_code` đã có sẵn — không cần schema `.proto` mới): ghi 1 dòng `audit.log` (event `AuthDataCorruptDetected`, mức cảnh báo cao, không log nội dung file) + gửi `ShowToastCommand` đúng 1 lần lúc phát hiện (không lặp lại mỗi lần verify fail, tránh spam) — giúp phụ huynh gặp sự cố ngoài ý muốn biết sớm để chủ động xử lý (thay vì mù mờ tưởng mình gõ sai mật khẩu nhiều lần). Thuần bổ sung minh bạch (đúng nguyên tắc "Minh bạch" ở `Architecture/01` mục 5), không thay đổi bất kỳ quyết định an toàn nào ở trên.

### 7.9 Bù thời gian chống timing oracle cho nhánh `auth.dat` corrupt/chưa-setup (bổ sung v0.3.0, ADR-84)

**Vấn đề (FAIL 3, `security-privacy-auditor` Đợt 3)**: ADR-81/mục 7.8 chỉ đồng nhất **nội dung** response giữa 3 tình huống (chưa từng setup, `auth.dat` corrupt, sai credential thật) — không đồng nhất **thời gian**. Đo thực nghiệm: `never_configured≈3.05ms`, `corrupt≈0.02ms` (cả 2 trả ngay, không hash gì) so với `real_wrong_password≈101.77ms` (chạy Argon2id thật, mục 4.2). Chênh lệch hàng chục-hàng trăm ms đo được dễ dàng qua round-trip IPC nội bộ bình thường (không cần công cụ đo chuyên dụng) — kẻ tấn công cố ý phá `auth.dat` (ví dụ ghi đè 4 byte version header, đúng kịch bản đã cảnh báo ở mục 7.8) có thể xác nhận ngay "phá hoại thành công" chỉ bằng cách đo thời gian phản hồi của 1 lệnh `AuthVerifyRequest` bất kỳ — đúng oracle mà ADR-81 tuyên bố loại bỏ nhưng trên thực tế mới loại bỏ được một nửa (nội dung, chưa loại bỏ thời gian).

**Phạm vi áp dụng**: đúng 3 handler đã nêu ở mục 7.8 có bước credential-check thật sự (loại trừ `SetInitialPasswordRequest`/`AuthStatusQuery` — cả 2 không hash gì trong nhánh "đã configured"/"corrupt" dù dữ liệu hợp lệ hay hỏng, nên vốn dĩ đã đồng nhất thời gian, không có oracle ở 2 message này):

| Handler | Điều kiện kích hoạt bù thời gian |
|---|---|
| `AuthVerifyRequest` | `_dataCorrupt \|\| _cached is null` |
| `ChangePasswordRequest` | `_dataCorrupt \|\| _cached is null` |
| `RecoveryResetRequest` | `_dataCorrupt \|\| _cached is null` |

Không áp dụng cho nhánh `LOCKED_OUT` (mục 7.7 — cố tình trả nhanh, không hash, để tiết kiệm CPU) — nhánh đó không phải oracle cần che giấu: giá trị `AuthResult.LOCKED_OUT` tự nó đã công khai "đang bị khoá" một cách có chủ đích qua chính nội dung response, thời gian phản hồi nhanh của nó không tiết lộ thêm thông tin nhạy cảm nào ngoài thông tin đã công khai sẵn.

**Cơ chế đã chọn: chạy 1 lần Argon2id THẬT trên dữ liệu "mồi" (decoy), không dùng `Task.Delay`**

Phương án đã cân nhắc và loại — `Task.Delay(~100)`: **loại**, vì (1) không tiêu tốn CPU giống nhánh thật (`Task.Delay` chỉ đăng ký timer, không có compute cost) — kẻ tấn công tinh vi đo được qua lấy mẫu CPU utilization của tiến trình `Service` (nhánh thật có 1 đợt CPU-bound ~100ms trên 1 core, nhánh giả không có gì) sẽ phân biệt được ngay dù thời gian wall-clock giống nhau; (2) `Task.Delay` dùng timer wheel của .NET, độ chính xác/jitter khác hẳn đặc trưng jitter tự nhiên của Argon2id thật (cấp phát 32 MiB bộ nhớ, áp lực cache/bộ nhớ theo tải máy hiện tại) — lấy mẫu thống kê đủ nhiều lần có thể lộ ra 2 phân phối thời gian khác hình dạng dù cùng trung bình; (3) hardcode 1 con số đo được 1 lần (`~100ms`) tự tách rời khỏi tham số Argon2id thật — nếu `m/t/p` đổi sau này (ví dụ benchmark lại, mục 4.2), con số hardcode sẽ lệch mà không ai nhớ cập nhật, chính là điều đề bài yêu cầu tránh.

**Chọn: hàm `RunDecoyArgon2idAsync(byte[] attackerSuppliedBytes)`** — gọi **đúng cùng 1 hàm hash Argon2id nội bộ** dùng cho verify thật (mục 4.3, cùng thư viện `Konscious.Security.Cryptography.Argon2`, cùng object tham số `m/t/p` chính thức đọc từ mục 4.2 — 1 nguồn sự thật duy nhất, không hardcode số đo riêng), chạy trên chính **byte credential người dùng vừa gửi lên** (không cần tạo thêm 1 "mật khẩu mồi" cố định — chi phí Argon2id chỉ phụ thuộc `m/t/p`, không phụ thuộc nội dung input, nên dùng thẳng input thật vừa nhận vừa đơn giản vừa tránh phải quản lý thêm 1 hằng số bí mật vô nghĩa) ghép với **1 salt "mồi" (`DecoySalt`)** — 16 byte sinh ngẫu nhiên đúng 1 lần lúc `Service` chuyển sang trạng thái `Starting` (RAM-only, không persist, không liên quan `auth.dat`), tái dùng cho mọi lần bù thời gian trong suốt vòng đời tiến trình đó (không sinh ngẫu nhiên lại mỗi lần gọi — không cần thiết vì kết quả luôn bị huỷ, chỉ cần salt hợp lệ 16 byte để hàm hash chạy đúng luồng thật). Kết quả hash **luôn bị huỷ ngay** (`CryptographicOperations.ZeroMemory` lên output 32 byte) — không so sánh với bất kỳ giá trị nào, không rẽ nhánh theo kết quả — chỉ mục đích duy nhất là **tiêu tốn đúng effort CPU/bộ nhớ** như 1 lần verify thật.

**Vị trí gọi trong luồng xử lý**: gọi `RunDecoyArgon2idAsync` ngay tại đúng điểm mà nhánh thật sẽ gọi Argon2id verify (mục 4.3/7.2/7.4/7.5) — nghĩa là **sau** bước parse/chuẩn hoá input (mục 6.2 cho Recovery Key) nhưng **trước** khi build response — không đặt lệch vị trí (ví dụ chèn thêm ở cuối hàm sau khi mọi thứ khác đã xong) để tổng thời gian + pattern lập lịch (nếu nhánh thật offload Argon2id qua `Task.Run` ra threadpool để không chặn thread xử lý pipe, nhánh mồi phải làm giống hệt — cùng kiểu `await Task.Run(...)`) khớp nhau ở mọi lớp, không chỉ khớp tổng thời gian đo được từ ngoài.

**Đánh đổi chấp nhận**: nhánh `_dataCorrupt || _cached is null` từ nay luôn trả 1 lần chi phí Argon2id đầy đủ cho **mọi** lượt gọi (không chỉ lượt "sai", vì bản thân nhánh này không có khái niệm "đúng") — chấp nhận được vì: (1) trạng thái "chưa từng setup" chỉ tồn tại trong cửa sổ ngắn trước khi hoàn tất Onboarding, không phải hot-path bình thường; (2) trạng thái "corrupt" vốn đã là tình huống bất thường/hiếm; (3) đối xứng đúng với chi phí `Service` đã sẵn sàng chấp nhận cho nhánh sai thật (verify thật cũng luôn chạy Argon2id cho mọi lần sai chưa bị khoá, mục 7.7) — không tạo thêm bề mặt lạm dụng CPU mới so với hiện trạng (tấn công dồn dập vào file `auth.dat` corrupt giờ tốn `Service` đúng bằng tấn công dồn dập vào 1 file hợp lệ thật, không hơn).

**Không tự implement ở đây** — mục này chỉ định nghĩa cơ chế; `feature-dev` cụ thể hoá `RunDecoyArgon2idAsync`/`AuthState.DecoySalt` trong `AuthCoordinator.cs`.

## 8. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-71 | Argon2id qua `Konscious.Security.Cryptography.Argon2` (thuần managed C#), không dùng thư viện wrap native `libargon2` | Nhất quán thận trọng dependency đã áp dụng ở `06` ADR-30; tránh native DLL rời cần quản lý ký số riêng ngoài lô SignPath managed assembly |
| ADR-72 | Tham số Argon2id chính thức `m=32768 KiB (32 MiB), t=2, p=2` (nhẹ hơn đề xuất khởi điểm `m=64MiB,t=3,p=2`), sàn tối thiểu không hạ dưới khuyến nghị OWASP (`m≥19456,t≥2,p≥1`) — **ĐÃ CHỐT bởi chủ dự án 2026-09-20** | `PWD-011` tường minh giao benchmark cho bước implement; chủ dự án ưu tiên trải nghiệm mượt trên máy yếu/cũ hơn biên an toàn tối đa, vẫn giữ trên sàn tối thiểu OWASP (`PWD-014`) |
| ADR-73 | Field credential trong IPC dùng `bytes` (UTF-8), không dùng `string`; best-effort zero qua `UnsafeByteOperations.UnsafeGetBuffer`; không tách credential ra khỏi thân Protobuf thành framing riêng | `string` bất biến không zero tin cậy được (`PWD-050`); tách framing riêng tăng phức tạp không tương xứng lợi ích cho kênh chỉ nội bộ máy |
| ADR-74 | Không dùng `SecureString` — dùng `GC.AllocateArray<byte>(n, pinned:true)` + `CryptographicOperations.ZeroMemory` | `SecureString` bị Microsoft khuyến cáo tránh cho code mới; `PWD-050` đã cho phép phương án `byte[]` tự quản lý như lựa chọn tương đương |
| ADR-75 | Recovery Key 24 ký tự (biên dưới `PWD-030`, đổi từ đề xuất khởi điểm 32 ký tự), Crockford Base32 (loại `I`/`L`/`O`/`U`), 15 byte CSPRNG = 120-bit entropy; chuẩn hoá uppercase + strip `-`/khoảng trắng lúc verify — **ĐÃ CHỐT bởi chủ dự án 2026-09-20** | Chọn HOW trong khoảng WHAT đã cho (`PWD-030`); chủ dự án ưu tiên chuỗi gọn hơn cho phụ huynh chép tay/lưu giấy; 120-bit vẫn vượt xa ngưỡng an toàn thực tế (cùng bậc AES-128), thêm rate-limit dùng chung (ADR-76) khiến brute-force online bất khả thi; chuẩn hoá nhất quán tinh thần `PWD-035` (giảm rủi ro tự khoá mình do sai định dạng) |
| ADR-76 | Rate-limit dùng 1 bộ đếm chung (`auth.dat.rate_limit`) cho cả mật khẩu lẫn Recovery Key — **ĐÃ CHỐT bởi chủ dự án 2026-09-20** (giữ nguyên đề xuất ban đầu) | Khớp schema có sẵn `04` (không amendment); cả 2 bảo vệ cùng 1 tài nguyên, tách riêng chỉ tăng tổng số lần thử được phép mà không có lợi ích rõ ràng; khớp chữ "tương tự" ở `PWD-033` |
| ADR-77 | Chống bypass rate-limit qua đổi giờ hệ thống bằng "monotonic anchor" (`Environment.TickCount64` tại lúc `Service` boot), không thêm field persist mới | Miễn nhiễm với đổi giờ xảy ra sau khi `Service` đã chạy (đa số trường hợp thực tế); rủi ro dư (đổi giờ + reboot đồng thời) giảm thiểu bởi quyền hạn Windows mặc định + tính ồn ào của reboot |
| ADR-78 | `AuthVerifyRequest`/`AuthVerifyResponse` phát hành `action_token` ngắn hạn (16 byte, TTL 15 giây, dùng 1 lần, RAM-only) làm cầu nối cho lệnh nghiệp vụ nhạy cảm sẽ thiết kế ở Đợt 4/5/6 — **ĐÃ CHỐT bởi chủ dự án 2026-09-20** (giữ nguyên đề xuất ban đầu) | Đúng lý do `ROADMAP.md` đặt Đợt 3 trước Đợt 4/5 (cần "gate" sẵn để tái sử dụng); không phải session "remember me" — khớp đúng "mỗi khi" của `PWD-020` |
| ADR-79 | `ChangePasswordRequest`/`RecoveryResetRequest` nhúng thẳng credential xác thực (mật khẩu cũ/Recovery Key) ngay trong chính message, không qua cổng `AuthVerifyRequest`/`action_token` | Đơn giản hơn, giảm 1 round-trip; `old_password`/`recovery_key` tự nó đã là bằng chứng xác thực cho đúng hành động đó |
| ADR-80 | Password/Auth chiếm field 80-91 trong khối UI 80-99 đã dành ở `03`, để 92-99 cho Đợt 6 | Amendment tối thiểu, không cần khối field number mới; đủ dư (8 slot) cho Dashboard query khác ở Đợt 6 |
| ADR-81 (v0.2.0) | `auth.dat` tồn tại nhưng không đọc/giải mã được → coi như "đã configured" (chặn Setup chạy lại); mọi `AuthVerify`/`ChangePassword`/`RecoveryReset` trả đúng mã lỗi "sai" giống hệt sai credential thật (không phân biệt qua response); không tự sửa/wipe/regenerate file | Fail-secure nghiêng về phía VẪN yêu cầu xác thực (`Architecture/01` mục 5) — coi corrupt = chưa setup sẽ mở lỗ hổng bypass (cố ý phá 4 byte đầu file để ép Setup chạy lại); đồng nhất response chống oracle (cùng tinh thần ADR-22 ở `03`); tương đương đúng kịch bản "mất cả mật khẩu lẫn Recovery Key" đã được `PWD` mục 5 + `ANTI-020`/`040`/`041` chấp nhận đánh đổi từ trước — không phải gap WHAT mới, xem mục 7.8 |
| ADR-83 (v0.3.0) | Recovery Key plaintext (`recovery_key_plaintext`/`new_recovery_key_plaintext`) đổi sang `bytes`, không còn ngoại lệ `string` — zero theo THỜI ĐIỂM riêng: ngay trong `finally` sau khi response đã ghi xong vào pipe (không phải sau khi hash xong như credential đầu vào khác); `PendingSetup` không còn giữ bản sao plaintext (chỉ giữ hash) | FAIL 1 (`security-privacy-auditor` Đợt 3): ngoại lệ `string` trước đó không có phân tích rủi ro biện minh, vi phạm chính ADR-73; `string` bất biến không zero tin cậy được đúng như mọi credential khác (`PWD-050`); giá trị này là đầu ra phải hiển thị 1 lần nên "dùng xong" = "đã chuyển giao cho UI qua IPC", không phải "đã hash xong" — thu hẹp cửa sổ tồn tại của `PendingSetup` từ tối đa 30 phút xuống mili-giây, giảm rủi ro thêm ngoài yêu cầu tối thiểu |
| ADR-84 (v0.3.0) | Bù thời gian nhánh `auth.dat` corrupt/chưa-setup (`AuthVerify`/`ChangePassword`/`RecoveryReset`) bằng cách chạy 1 lần Argon2id THẬT trên dữ liệu mồi (`RunDecoyArgon2idAsync`, salt mồi RAM-only sinh 1 lần lúc `Service` Starting, tham số `m/t/p` đọc từ mục 4.2 — không hardcode số đo), huỷ kết quả ngay, không dùng `Task.Delay` | FAIL 3 (`security-privacy-auditor` Đợt 3): đo thực nghiệm `never_configured≈3ms`/`corrupt≈0.02ms` vs `real_wrong_password≈102ms` là 1 oracle lộ qua round-trip IPC thường; `Task.Delay` bị loại vì không tốn CPU giống nhánh thật (phân biệt được qua CPU utilization) và jitter pattern khác biệt (phân biệt được qua đo thống kê nhiều lần); chạy hàm hash thật với tham số đọc động tự động nhất quán nếu `m/t/p` đổi sau này, không cần nhớ cập nhật hằng số riêng |

## 9. Câu hỏi mở / vấn đề cần xác nhận

_Không có gap WHAT nào cần `spec-maintainer` — `Specification/06-password-management-spec.md` đã Approved. **Cả 4 mục từng nêu ở bản v0.1.0 (ADR-72, ADR-75, ADR-76, ADR-78) đã được chủ dự án xác nhận/chốt ngày 2026-09-20** (xem ghi chú "ĐÃ CHỐT bởi chủ dự án 2026-09-20" trực tiếp trong bảng ADR mục 8 — 2 quyết định đổi số liệu là ADR-72 (Argon2id nhẹ hơn: `m=32MiB,t=2,p=2`) và ADR-75 (Recovery Key 24 ký tự); 2 quyết định giữ nguyên đề xuất ban đầu là ADR-76 (rate-limit dùng chung bộ đếm) và ADR-78 (`action_token` 15 giây)). Gap `auth.dat` corrupt phát sinh từ code thật (Đợt 3) đã chính thức hoá thành mục 7.8/ADR-81 (v0.2.0) — xác nhận thuần HOW, không cần `spec-maintainer` (xem lý do đầy đủ ở mục 7.8). 1 residual risk chưa implement được ghi nhận minh bạch cuối mục 7.8 (audit log/Toast cho sự kiện corrupt) — không bắt buộc, có thể làm sau như tech debt nhẹ.

**Cập nhật v0.3.0**: 2 FAIL cứng phát hiện qua audit bảo mật Đợt 3 (`security-privacy-auditor`, TEST-001 zero-tolerance) đã được thiết kế lại — cả 2 thuần HOW, không cần `spec-maintainer`: FAIL 1 (Recovery Key plaintext dùng `string` không có phân tích rủi ro biện minh) → mục 5.2/5.5/ADR-83, đồng bộ `ipc.proto`. FAIL 3 (timing oracle lộ trạng thái `auth.dat` corrupt/chưa-setup qua chênh lệch thời gian phản hồi, dù nội dung đã đồng nhất từ ADR-81) → mục 7.9/ADR-84. FAIL 2 (thứ tự xử lý logic, không đụng schema/kiến trúc) không thuộc phạm vi file này — giao thẳng `feature-dev` sửa code. Không còn câu hỏi mở nào khác trong file này tại thời điểm cập nhật._

## 10. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.3.1 | 2026-09-20 | PATCH — Đợt 6 (`ROADMAP.md`, Dashboard UI), amendment cùng lượt viết `10-ui-architecture.md`. Mục 7.2 bảng hằng số `action_context`: thêm `"manage_whitelist"` (mới, `MISC-030`), làm rõ `"view_audit_log"` trỏ đúng message `AuditLogQuery`, làm rõ `"delete_audit_log"` **không dùng** ở Đợt 6 (không có yêu cầu tính năng xoá audit log trong `Specification/03-frontend-ui-spec.md`, giữ nguyên reserved). Không đổi cơ chế `action_token`/rate-limit nào khác. Theo chỉ đạo — không dừng chờ review |
| v0.3.0 | 2026-09-20 | MINOR — sửa 2 FAIL cứng do `security-privacy-auditor` phát hiện lúc audit Đợt 3 (TEST-001 zero-tolerance), thuộc phạm vi `architecture-writer` (2 FAIL còn lại/FAIL 2 giao `feature-dev`, không đụng schema/kiến trúc). **FAIL 1** (mục 5.2/5.5, ADR-83): `recovery_key_plaintext`/`new_recovery_key_plaintext` đổi từ `string` sang `bytes` (đồng bộ `ipc.proto` — 3 field: `SetInitialPasswordResponse`, `ChangePasswordResponse`, `RecoveryResetResponse`) — bỏ ngoại lệ `string` trước đó không có phân tích rủi ro biện minh, vi phạm chính ADR-73. Thiết kế thời điểm zero riêng cho field "phải hiển thị 1 lần" này (khác credential đầu vào khác): zero trong `finally` ngay sau khi `Service` ghi xong response vào pipe (không phải sau khi hash xong) — vì từ thời điểm đó dữ liệu đã "thuộc về" phiên hiển thị phía `UI`. Đồng thời bỏ field `recovery_key_plaintext` khỏi `AuthState.PendingSetup` (mục 7.1) — trước đây giữ tới tận lúc `ConfirmRecoveryKeySavedRequest` xử lý xong (tối đa 30 phút), nay thu hẹp xuống mili-giây vì bước Confirm chưa từng cần đọc lại plaintext, chỉ cần hash đã tính sẵn. **FAIL 3** (mục 7.9, ADR-84): ADR-81/mục 7.8 mới đồng nhất nội dung response cho nhánh `auth.dat` corrupt/chưa-setup, chưa đồng nhất thời gian phản hồi (đo thực nghiệm `never_configured≈3ms`/`corrupt≈0.02ms` vs `real_wrong_password≈102ms` — timing oracle lộ qua round-trip IPC thường). Thiết kế cơ chế bù thời gian: chạy 1 lần Argon2id THẬT (`RunDecoyArgon2idAsync`) trên chính byte credential vừa nhận + 1 salt mồi RAM-only sinh 1 lần lúc `Service` Starting, dùng đúng tham số `m/t/p` chính thức đọc động từ mục 4.2 (không hardcode số đo), huỷ kết quả ngay — áp dụng cho cả 3 handler `AuthVerify`/`ChangePassword`/`RecoveryReset` khi `_dataCorrupt \|\| _cached is null`; loại phương án `Task.Delay` vì không tốn CPU/jitter pattern khác nhánh thật, có thể phân biệt qua đo CPU utilization hoặc thống kê thời gian nhiều lần. Cả 2 FAIL đều thuần HOW (đúng framework `PWD-050`/`051`/`020`–`023` đã Approved, không đổi WHAT), không kéo theo sửa `Specification/`. Không tự sửa code `.cs` — bàn giao `feature-dev` implement theo đúng thiết kế mục 5.5/7.9 |
| v0.2.0 | 2026-09-20 | MINOR — chính thức hoá mục 7.8 (mới) + ADR-81: hành vi khi `auth.dat` TỒN TẠI nhưng KHÔNG đọc/giải mã được (`AuthDataCorruptException`), gap `feature-dev` báo cáo lại sau khi implement Đợt 3 (`docs/dependency-map.md` mục "Khoảng trống đã biết — Đợt 3"). Xác nhận hành vi hiện tại của `AuthCoordinator` (coi corrupt = "đã configured", mọi verify trả "sai" đồng nhất không phân biệt qua response, không tự sửa/wipe/regenerate) là đúng đắn/an toàn — đối chiếu nguyên tắc fail-secure `Architecture/01` mục 5 và giải thích rõ vì sao KHÔNG dùng chung công thức "hỏng → nạp default → tự ghi đè" như `config.db` ở `04` mục 6 (mất credential không có "giá trị mặc định an toàn hơn"). Đánh giá tường minh: đây **không phải gap WHAT** — `Specification/06-password-management-spec.md` mục 5 đã chấp nhận đúng đánh đổi "mất cả mật khẩu lẫn Recovery Key → chỉ còn đường gỡ cài đặt hoàn toàn ở tầng OS" từ trước, `auth.dat` corrupt tương đương hệ quả với kịch bản đó (không mở rộng kiến trúc để "vá" thêm 1 kênh phục hồi bỏ qua xác thực — sẽ mở lại đúng lỗ hổng bypass đang chặn). 1 residual risk chưa implement được ghi nhận minh bạch (audit log/Toast cho sự kiện corrupt — không bắt buộc, tech debt nhẹ có thể làm sau, tái dùng nguyên `ShowToastCommand` đã có, không cần schema mới). Không kéo theo sửa `Specification/` hay file `Architecture/` khác. Đóng gap 1/2 nêu ở `docs/dependency-map.md` Đợt 3 (gap 2 — "chữ ký rỗng" HMAC `Hello` đầu tiên — xử lý ở amendment `03-ipc-communication.md` cùng lượt) |
| v0.1.0 | 2026-09-19 | Khởi tạo — Đợt 3 (`ROADMAP.md`). Xác nhận `04`/`06` đã đủ schema/ACL cho Password & Auth (không amendment nội dung lưu trữ). Tham số Argon2id cụ thể (thư viện `Konscious.Security.Cryptography.Argon2`, `m/t/p` khởi điểm, PHC format), memory hygiene xuyên suốt `UI`↔`Service` (kiểu `bytes` thay `string`, pinned array + `CryptographicOperations.ZeroMemory` thay `SecureString`, giới hạn dư của Protobuf `ByteString` ghi nhận minh bạch), format Recovery Key (32 ký tự Crockford Base32, 160-bit entropy), toàn bộ IPC Setup/Auth Modal (`action_token` gate)/Đổi mật khẩu/Khôi phục (field 80-91 khối UI), rate-limit thực thi cụ thể (bộ đếm chung, chống bypass đổi giờ qua monotonic anchor, không cần amendment schema `04`), 10 ADR (71-80), 4 mục cần chủ dự án xác nhận (không blocking). Amendment cùng lượt: `00-INDEX.md` (renumbering `08`→`09`/`09`→`10`/`10`→`11`/`11`→`12`), `02-process-architecture.md` (PATCH, cập nhật con trỏ `AuthState`), `03-ipc-communication.md` (MINOR, thêm message Password/Auth field 80-91 + sửa renumbering), `04-data-architecture.md` (PATCH, thêm 2 dòng `event_type`: `AuthBruteForceThresholdReached` + `VisionNetworkBlocked` còn treo từ `06`), `06-security-architecture.md` (PATCH, renumbering + đóng câu hỏi mở `VisionNetworkBlocked`) |
| v0.1.1 | 2026-09-20 | Trạng thái Draft → Approved — chủ dự án đã xác nhận/chốt cả 4 mục nêu ở mục 9 bản v0.1.0. (1) ADR-72: đổi tham số Argon2id chính thức từ đề xuất khởi điểm `m=64MiB,t=3,p=2` sang phương án **nhẹ hơn** `m=32MiB (32768 KiB), t=2, p=2` (mục 4.2), ưu tiên trải nghiệm mượt trên máy yếu/cũ, vẫn trên sàn tối thiểu OWASP (`m≥19MiB,t≥2,p≥1`) — benchmark Đợt 3 vẫn thực hiện để xác nhận ngân sách UX, không còn quyết định số cuối cùng. (2) ADR-75: đổi Recovery Key từ 32 ký tự (160-bit, 20 byte) xuống **24 ký tự** (120-bit, 15 byte CSPRNG) — biên dưới khoảng `PWD-030`, cập nhật lại phép tính entropy (15 byte × 8 ÷ 5 = 24 ký tự tròn, 6 nhóm × 4) và bổ sung đoạn xác nhận 120-bit vẫn đủ an toàn (cùng bậc AES-128, kết hợp rate-limit dùng chung) (mục 6.1). (3) ADR-76 (rate-limit dùng chung 1 bộ đếm) và (4) ADR-78 (`action_token` 15 giây, dùng 1 lần, RAM-only) giữ nguyên như đề xuất ban đầu, chỉ bổ sung ghi chú "ĐÃ CHỐT bởi chủ dự án 2026-09-20". Xoá toàn bộ checkbox câu hỏi mở ở mục 9, thay bằng xác nhận không còn câu hỏi mở — đủ điều kiện giao `feature-dev` triển khai code Đợt 3. Không có thay đổi nào kéo theo sửa `Specification/` (thuần chọn số liệu HOW trong khoảng WHAT đã chốt ở `PWD-011`/`PWD-030`/`PWD-033`/`PWD-020`) hay các file `Architecture/` khác. |

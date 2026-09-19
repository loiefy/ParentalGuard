# 04 — Data Architecture

> Version: v0.2.1 | Trạng thái: Approved | Cập nhật: 2026-09-19

## 1. Mục đích

File này trả lời **HOW** cho toàn bộ dữ liệu lưu trữ local của `ParentalGuard.Service` đã nêu sơ bộ ở `02-process-architecture.md` (state trung tâm chia theo domain) và `03-ipc-communication.md` (khoá HMAC bootstrap): schema `config.db` (SQLite), format audit log tamper-evident (hash-chain), layout file đầy đủ trên đĩa, và luồng fail-secure chi tiết khi đọc/giải mã cấu hình thất bại. Không phát minh yêu cầu sản phẩm mới — mọi quyết định trích dẫn ngược `Specification/` (chủ yếu `BE-060`/`061`/`061a`/`061b`, `SEC-040`/`041`, `PWD-010`–`014`/`030`–`035`, `MISC-010`, `ANTI-070`).

Nguyên tắc xuyên suốt file này (bám `ROADMAP.md` mục 2): schema phải **thêm field mới không phá cấu trúc cũ** (yêu cầu #5 ở nhiệm vụ viết file này), và phải nhất quán tuyệt đối với ràng buộc đã chốt ở `03-ipc-communication.md` mục 5.2: **`Vision`/`Overlay` bị cấm tự đọc `config.db`** (`SEC-017`) — file này không thiết kế bất kỳ cơ chế nào cho phép 2 process đó đọc trực tiếp, khoá HMAC vẫn chỉ đến với chúng qua anonymous pipe lúc spawn như đã chốt.

## 2. Layout file trên đĩa (đầy đủ)

| Đường dẫn | Nội dung | ACL | Nguồn |
|---|---|---|---|
| `%ProgramFiles%\ParentalGuard\*.exe` | 5 executable (`Service`/`Watchdog`/`Vision`/`Overlay`/`UI`) | User thường: Read+Execute. Write/Delete: chỉ SYSTEM + Administrators (qua uninstaller đã xác thực) | `ANTI-030` |
| `%ProgramFiles%\ParentalGuard\models\*.onnx` | Trọng số model AI (`GantMan/nsfw_model` convert ONNX) | Giống trên — Read-only với user thường, verify checksum mỗi lần `Vision` load (`MISC-090`, chi tiết cơ chế để ở `05-image-pipeline-architecture.md`) | `IMG-014`, `MISC-090` |
| `%ProgramData%\ParentalGuard\config.db` | SQLite — cấu hình giám sát, trạng thái pause, khoá HMAC IPC (mục 3) | **Chỉ `NT AUTHORITY\SYSTEM` Full Control** — không Allow cho Administrators/Users/Authenticated Users, ACL không kế thừa từ `%ProgramData%` (protected DACL) | `BE-060`, `SEC-040` |
| `%ProgramData%\ParentalGuard\config.db-wal`, `config.db-shm` | File phụ của SQLite khi dùng WAL journal mode (ADR-23) | Giống `config.db` | ADR-23 |
| `%ProgramData%\ParentalGuard\auth.dat` | Password hash + Recovery Key hash (Argon2id) — **tách biệt vật lý khỏi `config.db`** (mục 4) | Giống `config.db` — chỉ SYSTEM | `PWD-013`, `PWD-014` |
| `%ProgramData%\ParentalGuard\audit.log` | Audit log hash-chain, append-only, plaintext JSONL (mục 5) | Giống `config.db` — chỉ SYSTEM (ACL bảo vệ **ghi**, không phải bảo vệ đọc nội dung — xem mục 5.1 vì sao không mã hoá) | `MISC-010`, `SEC-041` |

- Cả `%ProgramData%\ParentalGuard\` lẫn `%ProgramFiles%\ParentalGuard\` đều **không có bất kỳ file nào user thường hoặc chính tài khoản Administrator của phụ huynh đọc/ghi trực tiếp được** đối với dữ liệu nhạy cảm (`config.db`/`auth.dat`/`audit.log`) — kể cả phụ huynh cũng chỉ xem được qua Dashboard (`UI`) sau khi xác thực mật khẩu, đi qua `Service` bằng Named Pipe (`03-ipc-communication.md`), không đọc file trực tiếp. Đây là hệ quả trực tiếp của "chỉ SYSTEM đọc được" đã chốt ở `BE-060`/`PWD-014` — nghiêm ngặt hơn cả `ANTI-030` (thư mục cài đặt cho phép Read+Execute với user thường vì đó chỉ là file thực thi, không phải dữ liệu).
- Cơ chế set ACL cụ thể (thời điểm cài đặt hay lần chạy đầu của `Service`, dùng `icacls`/`FileSystemAccessRule`) — để ở `06-security-architecture.md` (ACL cụ thể theo mô tả ở `Architecture/00-INDEX.md` mục 2), file này chỉ định nghĩa **kết quả ACL mong muốn**, không phải lệnh thực thi.

## 3. `config.db` (SQLite)

### 3.1 Nguyên tắc mã hoá (ADR-24 — trả lời "cả file hay từng field")

SQLite không có cơ chế mã hoá minh bạch tích hợp sẵn tương thích DPAPI (mã hoá cả file cần thư viện thứ ba như SQLCipher — chưa có quyết định nào trong `Specification/` chọn dependency đó, không tự thêm ở đây). Thay vào đó:

- **Mỗi bảng domain-state chỉ có đúng 1 dòng** (mẫu hình *singleton row*, hợp lý vì đây là app single-user local, không có nhu cầu nhiều bộ cấu hình song song — `PRIMARY KEY` ràng buộc `CHECK (id = 1)`).
- Toàn bộ field nhạy cảm của 1 dòng được **serialize thành JSON rồi mã hoá nguyên khối bằng DPAPI machine-scope** (`CryptProtectData` không cờ `CRYPTPROTECT_LOCAL_MACHINE`... thực chất dùng scope máy vì `Service` chạy LocalSystem — chi tiết API để lúc code), lưu vào 1 cột `BLOB` duy nhất (`data_encrypted`). Chỉ các cột **không nhạy cảm, cần đọc được trước khi có key để quyết định luồng xử lý** (id, `schema_version` của chính dòng đó, `updated_at_unix_ms`) để ở dạng plaintext ngoài blob — đúng yêu cầu `SEC-040` ("toàn bộ config nhạy cảm... mã hoá DPAPI") mà không mã hoá kim loại (không cần mã hoá metadata kỹ thuật không nhạy cảm).
- Lý do chọn "1 blob JSON/dòng" thay vì mã hoá từng field riêng: đơn giản hoá code (1 lần decrypt/encrypt cho cả domain-state), và **giải quyết luôn yêu cầu #5 (schema versioning không phá huỷ)** — thêm field mới vào JSON không cần `ALTER TABLE`, code cũ đọc JSON thiếu field mới tự áp dụng giá trị mặc định (giống tinh thần `oneof` mở rộng được ở `03-ipc-communication.md` ADR-16).

### 3.2 Bảng `schema_meta` (plaintext — không nhạy cảm, phải đọc được trước khi biết cách giải mã phần còn lại)

```sql
CREATE TABLE schema_meta (
  id                        INTEGER PRIMARY KEY CHECK (id = 1),
  schema_version            INTEGER NOT NULL,        -- version cấu trúc DB tổng thể (số bảng, tên cột plaintext)
  db_created_at_unix_ms     INTEGER NOT NULL,
  app_version_at_creation   TEXT NOT NULL,
  last_fallback_event_unix_ms INTEGER NULL           -- mốc lần gần nhất BE-061 fail-secure kích hoạt, NULL nếu chưa từng
);
```

### 3.3 Bảng `monitoring_state` (`MonitoringState` — `02-process-architecture.md` mục 5.3)

```sql
CREATE TABLE monitoring_state (
  id                INTEGER PRIMARY KEY CHECK (id = 1),
  row_schema_version INTEGER NOT NULL,
  data_encrypted    BLOB NOT NULL,   -- DPAPI(JSON), xem cấu trúc JSON bên dưới
  updated_at_unix_ms INTEGER NOT NULL
);
```

JSON bên trong `data_encrypted`:

```json
{
  "monitoring_enabled": true,
  "risk_threshold": 0.0,
  "capture_interval_baseline_ms": 0,
  "exclude_process_names": ["Taskmgr.exe", "regedit.exe", "..."],
  "using_fallback_config": false
}
```

- `monitoring_enabled`: mặc định `true` (fail-secure, `BE-061`).
- `risk_threshold`: giá trị **placeholder** — con số cụ thể do benchmark thực tế quyết định ở Đợt 1 (`BE-090`, `09-image-processing-spec.md`), không chốt số ở đây.
- `capture_interval_baseline_ms`: tần suất capture nền, con số cụ thể để tuning ở Đợt 7 (`PERF-010`/`011`), field có mặt từ Đợt 0 để tránh migration sau.
- `exclude_process_names`: khởi tạo lúc **cài đặt lần đầu bình thường** bằng danh sách khởi điểm `BE-073a`. **Khi rơi vào nhánh fail-secure (`BE-061`/`ANTI-070`), field này reset về mảng rỗng** — đúng nguyên tắc "Whitelist rỗng (không có ngoại lệ nào được áp dụng khi chưa xác thực lại được cấu hình gốc)" đã chốt tường minh ở `ANTI-070`, không dùng lại `BE-073a` cho nhánh này dù có vẻ trực giác là "an toàn" (danh sách đó chỉ an toàn khi chính nó được xác thực toàn vẹn qua `config.db` hợp lệ).
- `using_fallback_config`: cờ runtime hiện thực hoá `ADR-14` ở `02-process-architecture.md` (song song với `Running·Monitoring`, không phải state loại trừ). Ghi vào đây để `Dashboard` (Đợt 6) hiển thị được kể cả sau khi `Service` đã restart bình thường, nhưng **`Service` tự đặt lại `false` ngay khi ghi đè `config.db` mới thành công** (mục 6) — chỉ giữ mốc lịch sử qua `schema_meta.last_fallback_event_unix_ms` (mục 3.2), không phải cờ "mãi mãi degraded".

### 3.4 Bảng `pause_state` (`PauseState` — `PAUSE-030`)

```sql
CREATE TABLE pause_state (
  id                INTEGER PRIMARY KEY CHECK (id = 1),
  row_schema_version INTEGER NOT NULL,
  data_encrypted    BLOB NOT NULL,
  updated_at_unix_ms INTEGER NOT NULL
);
```

JSON:

```json
{
  "is_paused": false,
  "pause_started_at_unix_ms": null,
  "pause_expires_at_unix_ms": null
}
```

- Đây là **trạng thái hiện tại duy nhất**, dùng để phục hồi đúng ở bước `Starting` (`02-process-architecture.md` mục 3: đọc lại timestamp để quyết định quay vào `Running·Paused` hay `Running·Monitoring`) — khớp `PAUSE-030`.
- **Không lưu lịch sử/tần suất pause trong bảng này** — số lần và tổng thời lượng tạm dừng (`PAUSE-020`) lấy từ chính `audit.log` (mục 5, event `PauseActivated`/`PauseResumed`) khi cần tính (lúc kích hoạt pause mới, hoặc lúc Dashboard hiển thị) — tránh 2 nguồn sự thật song song cho cùng 1 dữ liệu lịch sử. Cảnh báo tần suất bất thường (`PAUSE-021`) cũng suy ra từ quét `audit.log` theo cửa sổ thời gian, không cache riêng ở Đợt 0 (tối ưu hoá nếu cần để Đợt 5/7).

### 3.5 Bảng `ipc_keys` (khoá HMAC — `03-ipc-communication.md` mục 5.2)

```sql
CREATE TABLE ipc_keys (
  channel           TEXT PRIMARY KEY,   -- hiện tại chỉ có 1 dòng: 'vision_overlay'
  row_schema_version INTEGER NOT NULL,
  key_encrypted     BLOB NOT NULL,      -- DPAPI(32 byte key thô) — KHÔNG bọc JSON vì đã là binary secret thuần
  created_at_unix_ms INTEGER NOT NULL
);
```

- Đúng khớp `03-ipc-communication.md` mục 5.2: "1 khoá HMAC 32 byte sinh ngẫu nhiên 1 lần duy nhất lúc cài đặt, lưu trong `config.db`, mã hoá DPAPI machine-scope" — hiện tại dùng chung cho cả kênh `Vision` lẫn `Overlay` (1 dòng duy nhất `channel = 'vision_overlay'`). Thiết kế bảng theo khoá chính `channel` (không phải 1 bảng đơn dòng cứng) là ADR thuần kỹ thuật (ADR-25): chừa chỗ tách khoá riêng cho từng kênh sau này (nếu cần) chỉ bằng cách thêm dòng mới, không phải đổi cấu trúc bảng — không đổi hành vi hiện tại (vẫn 1 khoá dùng chung, đúng spec).
- `key_encrypted` **không có trong nhánh fail-secore reset** — nếu `config.db` bị coi là hỏng toàn bộ (mục 6), khoá này cũng mất và bị sinh lại mới hoàn toàn (không thể "chỉ khôi phục phần này") — xem hệ quả ở mục 6.3.

### 3.6 Bảng `audit_meta` (plaintext — checkpoint tăng tốc verify `audit.log`, không phải nguồn sự thật)

```sql
CREATE TABLE audit_meta (
  id                        INTEGER PRIMARY KEY CHECK (id = 1),
  chain_id                  TEXT NOT NULL,   -- GUID của đoạn chain hiện hành, xem mục 5.1
  last_seq                  INTEGER NOT NULL,
  last_hash                 TEXT NOT NULL,   -- hash (hex) của record cuối cùng đã ghi, dùng làm prev_hash cho record kế tiếp
  last_full_verify_at_unix_ms INTEGER NULL
);
```

- Bảng này **chỉ là cache** để `Service` không phải đọc lại toàn bộ `audit.log` mỗi lần khởi động chỉ để biết "ghi tiếp từ đâu". Không mã hoá (không chứa dữ liệu nhạy cảm — chỉ số thứ tự/hash, chính `audit.log` mới là nguồn sự thật, xem mục 5.4 khi cache này bị mất do `config.db` bị recreate).

### 3.6a Bảng `icon_positions` (plaintext — mới v0.2.0, `FE-020a`, `07-overlay-architecture.md` mục 4.1.4)

```sql
CREATE TABLE icon_positions (
  device_name        TEXT PRIMARY KEY,   -- MONITORINFOEX.szDevice, vd "\\.\DISPLAY1" — KHÔNG phải monitor_id
                                          -- ipc dùng cho OverlayRect (03-ipc-communication.md), 2 khái niệm tách biệt
  x                   INTEGER NOT NULL,
  y                   INTEGER NOT NULL,
  updated_at_unix_ms  INTEGER NOT NULL
);
```

- **Không mã hoá DPAPI**: toạ độ vị trí icon trên màn hình không phải dữ liệu nhạy cảm (không tiết lộ hành vi giám sát/nội dung bị chặn) — cùng lý do `audit_meta` (mục 3.6) để plaintext, khác các bảng domain-state ở mục 3.1 (vốn chứa cấu hình giám sát thật sự nhạy cảm theo `SEC-040`).
- **Không đi qua `MonitoringState`/domain-state khác** — bảng riêng vì vòng đời khác hẳn (cập nhật bởi hành động kéo-thả của `Overlay`, không liên quan state machine trung tâm `02-process-architecture.md` mục 3), không mã hoá DPAPI (không cần key để đọc). **Vẫn bị mất khi `config.db` rơi vào nhánh fail-secure** (mục 6, ADR-27 "coi corrupt là toàn file" — `icon_positions` không phải ngoại lệ, cùng số phận `ipc_keys` mục 3.5): hệ quả chỉ là icon quay về vị trí mặc định góc dưới-phải, không phải rủi ro an toàn/bảo mật gì (khác hẳn hệ quả mất `monitoring_enabled`/`exclude_process_names` — vốn phải fail-secure về giá trị an toàn nhất) — vì vậy không cần thiết kế đường phục hồi riêng nào cho bảng này.
- 1 dòng/màn hình từng được kéo ít nhất 1 lần (không có dòng nào cho màn hình chưa từng kéo — `Overlay` tự dùng vị trí mặc định góc dưới-phải khi không tìm thấy `device_name` khớp trong `IconLayoutSync`, `03-ipc-communication.md` v0.4.0).
- **Yêu cầu migration** (đúng quy tắc mục 3.7 — "thêm bảng mới" phải bump `schema_meta.schema_version`): thêm bảng này là bước migration đầu tiên thật sự (`v1 → v2`) của cơ chế đã dựng khung sẵn từ Đợt 0 — `feature-dev` cần thêm bước migration tương ứng (`CREATE TABLE icon_positions` nếu chưa tồn tại) khi implement, không tạo bảng trực tiếp trong schema khởi tạo ban đầu nếu `config.db` từ Đợt 0/1 đã tồn tại trên máy dev.

### 3.7 Schema versioning (yêu cầu #5)

- `schema_meta.schema_version` (số nguyên tăng dần) đại diện cho **cấu trúc DB tổng thể** (số bảng, tên cột plaintext) — chỉ bump khi có thay đổi cấu trúc thật sự (thêm bảng mới, đổi tên cột plaintext). Thêm field bên trong JSON của 1 domain-state **không** bump `schema_version` tổng, chỉ bump `row_schema_version` của riêng dòng đó nếu cần đánh dấu shape JSON đã đổi ý nghĩa (không phải chỉ thêm field optional).
- Quy tắc migration lúc boot: `Service` đọc `schema_meta.schema_version` trước tiên (plaintext, không cần giải mã) → so với version code hiện tại kỳ vọng:
  - Bằng nhau → đọc bình thường.
  - Nhỏ hơn → chạy tuần tự các bước migration đã biết (v1→v2→v3...) trước khi dùng — migration chỉ thao tác plaintext (thêm bảng/cột) và JSON shape sau khi decrypt, không đổi ngữ nghĩa field cũ đã có (giống nguyên tắc "không xoá/đổi ý nghĩa field cũ" đã áp dụng cho Protobuf ở `03-ipc-communication.md` mục 3.1).
  - Lớn hơn (downgrade — ví dụ cài lại bản cũ hơn) → không đoán mò cách đọc, coi như tương đương "không đọc được" → đi vào nhánh fail-secure mục 6 (an toàn hơn là cố parse sai).
- Đợt 0 chỉ có `schema_version = 1` — cơ chế migration này là khung sẵn có, chưa có bước migration thật nào cho tới khi có thay đổi cấu trúc thật sự ở Đợt sau.

## 4. `auth.dat` — lưu trữ password hash & Recovery Key (tách biệt khỏi `config.db`)

**Lưu ý traceability quan trọng**: `PWD-013` quy định tường minh "Hash + salt lưu trong file riêng biệt với `config.db`" — vì vậy `AuthState` **không** có bảng trong `config.db` (khác cách diễn đạt tóm tắt ở đầu nhiệm vụ viết file này, tài liệu này bám theo `Specification/` là nguồn sự thật). `AuthState` runtime (phiên xác thực tạm thời trong RAM, theo `02-process-architecture.md` mục 5.3) vẫn không persist — chỉ phần **rate-limit counter** (mục 4.2) là ngoại lệ được persist để tránh bị bypass bằng cách restart `Service` (ADR-26, giải thích bên dưới), toàn bộ phần còn lại của `AuthState` không có bản lưu trên đĩa.

File `%ProgramData%\ParentalGuard\auth.dat`: không phải SQLite (dữ liệu quá đơn giản, 1 blob JSON là đủ, tránh mở thêm 1 DB engine instance chỉ cho vài trăm byte) — là 1 file nhị phân gồm `{4 byte version plaintext}{DPAPI(JSON)}`, cấu trúc JSON:

```json
{
  "password": {
    "hash": "<argon2id hash string, PHC format>",
    "argon2_params": { "memory_kb": 0, "iterations": 0, "parallelism": 0 },
    "updated_at_unix_ms": 0
  },
  "recovery_key": {
    "hash": "<argon2id hash string>",
    "argon2_params": { "memory_kb": 0, "iterations": 0, "parallelism": 0 },
    "created_at_unix_ms": 0,
    "used": false
  },
  "rate_limit": {
    "consecutive_failures": 0,
    "last_failure_at_unix_ms": null,
    "delay_until_unix_ms": null
  },
  "security_questions": null
}
```

- `password.hash`/`recovery_key.hash`: định dạng chuẩn Argon2id PHC string (đã bao gồm salt + tham số bên trong chuỗi hash theo chuẩn thư viện Argon2id — không cần cột `salt` tách riêng, đây là thực hành chuẩn của mọi thư viện Argon2id hiện đại) — hiện thực hoá `PWD-011`/`012`. Tham số `argon2_params` cụ thể (memory cost/iterations/parallelism) để benchmark ở Đợt 3, không chốt số ở đây (`PWD-011` cũng chưa chốt số).
- `recovery_key.used`: đánh dấu sau khi dùng recovery key thành công 1 lần → sinh key mới, key cũ vô hiệu (`PWD-032`) — field này để tránh dùng lại key cũ nếu logic sinh key mới có lỗi ghi đè giữa chừng (defense in depth, ADR thuần kỹ thuật).
- `rate_limit` (ADR-26): `PWD-021`/`022` yêu cầu rate-limit "theo thời gian thực tại Service" và chống bypass bằng cách chỉnh giờ máy, nhưng không có `Requirement ID` nào nói rõ counter có phải sống sót qua restart `Service` hay không. Nếu chỉ giữ trong RAM (`AuthState` thuần runtime), 1 đứa trẻ biết `Service` có Watchdog tự restart (`ANTI-010`) có thể cố tình làm crash `Service` liên tục để reset bộ đếm delay — làm vô hiệu hoá tinh thần chống bruteforce của `PWD-021`. Vì vậy quyết định kiến trúc: **persist `rate_limit` xuống `auth.dat`**, ghi lại mỗi lần verify thất bại, đọc lại lúc `Service` khởi động để tiếp tục đúng bậc delay đang dở dang. Đây là quyết định HOW thuần tuý (không đổi bậc delay/ngưỡng đã chốt ở `PWD-021`), nhưng cần chủ dự án lưu ý khi review vì mở rộng nhẹ phạm vi "cái gì được lưu trữ" so với chữ nghĩa gốc của `PWD-0xx` — nêu ở mục 8 để xác nhận, không tự coi là hiển nhiên đúng.
- `security_questions`: `null` cố định ở Phase 1 (`PWD-034` là Phase 2, ngoài phạm vi `ROADMAP.md`) — field có mặt sẵn trong shape JSON để không phải đổi cấu trúc file khi Phase 2 triển khai, không có ý nghĩa gì ở Đợt 0-9.
- ACL/mã hoá: giống hệt `config.db` (mục 2) — DPAPI machine-scope (`PWD-013`), ACL chỉ SYSTEM (`PWD-014`).

## 5. Audit log tamper-evident (`audit.log`, `MISC-010`/`SEC-041`)

### 5.1 Format record & vì sao không mã hoá nội dung

- **1 dòng JSON/record (JSONL)**, append-only, không mã hoá nội dung (`SEC-041`: "không cần mã hoá nội dung vì không chứa dữ liệu nhạy cảm, chỉ cần đảm bảo tính toàn vẹn"). Chọn JSONL (thay vì binary) để đúng tinh thần minh bạch (`SEC-003`, `Architecture/01` mục 5) — về sau nếu có công cụ verify độc lập (`MISC-010`: "cho phép... người audit độc lập verify mà không cần tin tưởng mù quáng vào app đang chạy") thì định dạng text dễ viết tool ngoài hơn binary tự chế.
- ACL thư mục vẫn giới hạn ghi cho SYSTEM (mục 2) — đây là lớp bảo vệ **toàn vẹn qua kiểm soát truy cập** (ai được ghi), tách biệt với việc **không mã hoá nội dung** (nội dung không bí mật, ai đọc được file cũng không rò rỉ gì — nhưng thực tế chỉ SYSTEM đọc trực tiếp được, phụ huynh xem qua Dashboard đã xác thực).

```json
{"seq": 1, "ts_unix_ms": 1758000000000, "chain_id": "b3f1...-genesis", "event_type": "ServiceStarted", "detail": {}, "prev_hash": "0000000000000000000000000000000000000000000000000000000000000000", "hash": "<sha256 hex>"}
```

- `seq`: tăng dần tuyệt đối từ 1, không có khoảng trống trong 1 `chain_id` (khoảng trống chỉ xuất hiện khi bắt đầu `chain_id` mới sau sự kiện chain bị đứt, mục 5.3).
- `chain_id`: GUID của đoạn chain hiện hành — record đầu tiên của `audit.log` (hoặc đầu tiên sau khi phát hiện đứt chain) có `prev_hash` là chuỗi 64 ký tự `0` (sentinel "genesis"), đánh dấu điểm bắt đầu 1 đoạn chain mới. 1 file `audit.log` có thể chứa **nhiều đoạn chain nối tiếp nhau theo thời gian** — không xoá đoạn cũ khi đoạn mới bắt đầu (yêu cầu "không mất audit log cũ" ở mục 6 nhiệm vụ viết file này).
- `detail`: metadata theo `event_type`, **tuyệt đối không chứa dữ liệu ảnh/pixel** (`BE-060`). Danh sách `event_type` tối thiểu theo `MISC-010`, ánh xạ Requirement ID:

| `event_type` | Khi nào ghi | Nguồn |
|---|---|---|
| `MonitoringToggled` | Bật/tắt giám sát | `MISC-010` |
| `PauseActivated` / `PauseResumed` | Tạm dừng / resume (chủ động hoặc tự động hết hạn) | `MISC-010`, `PAUSE-020` |
| `ContentBlocked` | Overlay chặn 1 cửa sổ vi phạm (kèm `risk_score`, toạ độ — KHÔNG kèm ảnh) | `MISC-010`, `BE-060` |
| `ForceCloseRequested` | `Service` nhận `ForceCloseRequest` từ `Overlay` và xử lý xong (xoá overlay khỏi danh sách active) — `OverlayDecisionCoordinator.HandleForceCloseAsync`, đã ghi từ code Đợt 1, bổ sung tường minh vào bảng này ở v0.2.0 (trước đó là gap câu chữ, không phải gap hành vi — code đã ghi đúng event này) | `BE-032`, `BE-089b` |
| `AuthAttempt` | Mỗi lần xác thực mật khẩu/Recovery Key (thành công/thất bại) | `MISC-010`, `PWD-020`/`021` |
| `ProcessRestarted` | `Vision`/`Overlay` bị crash-restart | `MISC-010`, `BE-023` |
| `ConfigChanged` | Đổi cấu hình nhạy cảm | `MISC-010` |
| `ConfigFallbackTriggered` | `Service` rơi vào nhánh fail-secure (mục 6) | `BE-061`, `ANTI-070` |
| `AttackPatternDetected` | Restart liên tục vượt ngưỡng (`ANTI-060`) | `ANTI-060` |
| `AuditChainBrokenDetected` | Phát hiện chain đứt lúc verify (mục 5.3) | Suy ra từ `SEC-041`/`MISC-010` (không phải Requirement ID riêng — HOW-level) |
| `SoftwareUpdated` | Cài đè bản mới qua installer thủ công | `MISC-010`, `MISC-020` |
| `VisionNetworkBlocked` | WFP chặn 1 kết nối network từ `Vision` (phát hiện qua Security Event 5157) — bổ sung v0.2.1, đã định nghĩa `detail` ở `06-security-architecture.md` mục 3.3 (`dest_addr`/`dest_port`/`protocol`/`blocked_at_unix_ms`) từ v0.1.0, trước đó thiếu dòng này ở bảng — gap câu chữ, không phải gap hành vi | `SEC-010`, `06-security-architecture.md` ADR-33/34 |
| `AuthBruteForceThresholdReached` | `rate_limit.consecutive_failures` đạt ngưỡng ≥ 9 (mức cao nhất `PWD-021`) — mức cảnh báo cao, `detail = {consecutive_failures, action_context}`, bổ sung v0.2.1 | `PWD-021`, `08-password-authentication-architecture.md` mục 7.7 |

**`ForceCloseRequested.detail` (bổ sung v0.2.0, `BE-089b`)**:

```json
{ "windowHandle": 123456, "overlayId": 7, "source": "manual" }
```

- `source`: **bắt buộc**, đúng 2 giá trị literal theo yêu cầu tường minh `BE-089b`: `"manual"` (người dùng bấm nút "Tắt nội dung", cả chế độ đơn lẫn chế độ gộp) hoặc `"auto-timeout"` (overlay full-screen lock tự động force-close sau 30 giây không thao tác — chỉ xảy ra ở chế độ gộp, `07-overlay-architecture.md` mục 3.4.2). `Service` map trực tiếp từ enum `CloseSource` nhận được qua IPC (`03-ipc-communication.md` v0.4.0: `MANUAL → "manual"`, `AUTO_TIMEOUT → "auto-timeout"`) — không tự suy luận nguồn từ dữ liệu khác, luôn lấy nguyên giá trị `Overlay` đã gửi (đúng nguyên tắc "Service không rẽ nhánh nghiệp vụ thay Overlay" đã áp dụng nhất quán ở các message khác).

### 5.2 Quy tắc tính `hash`

```
canonical = sorted-key JSON của {seq, ts_unix_ms, chain_id, event_type, detail, prev_hash}  (không gồm field "hash")
hash      = hex(SHA-256(UTF8(canonical)))
```

Record kế tiếp dùng `hash` (chuỗi hex) của record này làm `prev_hash` — **không chain theo toàn bộ byte của dòng đã ghi** (tránh phụ thuộc vào chi tiết format hiển thị/whitespace, chỉ phụ thuộc nội dung logic).

### 5.3 Verify & xử lý khi phát hiện chain bị đứt (đối chiếu nguyên tắc fail-secure `BE-061`/`ANTI-070`)

- **Không verify lại toàn bộ file mỗi lần khởi động** (chi phí tăng theo thời gian chạy, không cần thiết) — `Service` dùng checkpoint `audit_meta` (mục 3.6) để biết `last_seq`/`last_hash`, chỉ verify **N record cuối cùng** (con số N cụ thể để benchmark ở Đợt 8, tạm dùng N=50 làm giá trị khởi điểm hợp lý) mỗi lần khởi động — đủ để phát hiện sớm tamper xảy ra gần thời điểm restart mà không tốn chi phí quét toàn bộ lịch sử.
- **Verify toàn bộ chain** là hành động **on-demand** (Dashboard yêu cầu, hoặc job nền tần suất thấp) — thuộc phạm vi công cụ/UI ở Đợt 6/8 (`ROADMAP.md` mục 4), không phải cơ chế bắt buộc ở Đợt 0. Format record (mục 5.1/5.2) đã đủ để công cụ này verify độc lập, đúng ý `MISC-010`.
- **Khi phát hiện đứt chain** (record nào đó có `prev_hash` không khớp `hash` của record liền trước, hoặc `seq` có khoảng trống không giải thích được bởi 1 lần "chain mới" hợp lệ):
  1. **Không xoá/sửa bất kỳ record cũ nào** — giữ nguyên làm bằng chứng (yêu cầu "không mất audit log cũ").
  2. Append 1 record mới `AuditChainBrokenDetected` vào cuối file, với `chain_id` **mới** (GUID mới) và `prev_hash = sentinel genesis` (bắt đầu đoạn chain mới hoàn toàn, không cố nối tiếp đoạn đã bị nghi ngờ) — `detail` ghi rõ: `last_known_good_seq`, `broken_at_seq` (nếu xác định được), thời điểm phát hiện.
  3. Cập nhật `audit_meta` (`config.db`) trỏ sang `chain_id` mới, `last_seq` reset theo đoạn mới.
  4. Gửi Windows Toast Notification qua kênh IPC `Service → Overlay` đã có sẵn (tái dùng đúng cơ chế `BE-061b`, không phát minh kênh mới) cảnh báo phụ huynh: nhật ký giám sát có dấu hiệu bị can thiệp.
  5. **Không dừng giám sát** ở bất kỳ bước nào trong quy trình này — đúng nguyên tắc Fail-secure xuyên suốt (`Architecture/01` mục 5), tương tự tinh thần `BE-061` áp dụng cho `config.db` nhưng đây là dữ liệu audit, không phải cấu hình giám sát, nên **không** kích hoạt lại toàn bộ luồng mục 6 (không có gì để "fallback cấu hình" ở đây, `config.db` vẫn đọc được bình thường).

### 5.4 Khi checkpoint `audit_meta` bị mất (do `config.db` bị recreate ở mục 6) nhưng `audit.log` vẫn nguyên vẹn

- `audit_meta` chỉ là cache tăng tốc (mục 3.6) — nếu `config.db` bị coi là hỏng và tạo mới (mục 6), bảng này trắng theo. `Service` **không** coi đây là "chain bị đứt": đọc lại **dòng cuối cùng hợp lệ** của `audit.log` trực tiếp (không phải toàn bộ file) để tái tạo `last_seq`/`last_hash`/`chain_id` cho `audit_meta` mới, rồi tiếp tục ghi nối vào đúng đoạn chain hiện có — không tự ý mở đoạn chain mới trong trường hợp này vì `audit.log` bản thân nó không có dấu hiệu bị tamper.

## 6. Fail-secure loading flow chi tiết (`BE-061`/`061a`/`061b`, `ANTI-070`/`070a`/`070b`, `SEC-040a`)

### 6.1 Phân loại "đọc/giải mã thất bại" (cụ thể hoá, không có trong spec gốc — HOW-level)

| Tình huống | Phát hiện bằng | Coi là fail-secure case? |
|---|---|---|
| Lần đầu cài đặt (`config.db` và `auth.dat` đều chưa từng tồn tại) | Cả 2 file không tồn tại | **Không** — luồng Onboarding bình thường, tạo mới với default, không ghi `ConfigFallbackTriggered`, không Toast |
| `config.db` bị xoá nhưng `auth.dat` đã tồn tại từ trước (đã từng Onboarding) | File không tồn tại nhưng có bằng chứng đã từng cấu hình | **Có** — trạng thái bất thường (`ANTI-070` liệt kê A9: xoá/hỏng file) |
| File tồn tại nhưng không mở được như SQLite hợp lệ | `SQLITE_CORRUPT`/`SQLITE_NOTADB` khi mở connection | **Có** |
| Mở được nhưng `CryptUnprotectData` (DPAPI) ném lỗi ở bất kỳ dòng nào (`monitoring_state`/`pause_state`/`ipc_keys`) | Exception giải mã | **Có** (`SEC-040a`) |
| Giải mã được nhưng JSON không parse được, hoặc thiếu field bắt buộc, hoặc `schema_meta.schema_version` lớn hơn version code hỗ trợ (mục 3.7) | Lỗi parse/validate | **Có** |

- Đơn giản hoá có chủ đích (ADR-27): coi corrupt là **toàn file**, không cố phục hồi từng bảng riêng lẻ dù về lý thuyết SQLite có thể mở được nhưng chỉ 1 dòng bị hỏng DPAPI — khớp đúng nghĩa đen `BE-061`/`ANTI-070` ("không đọc được `config.db`" nói chung, "phá cấu trúc khiến app không đọc được"), tránh logic phục hồi từng phần phức tạp không được yêu cầu.

### 6.2 Luồng xử lý khi rơi vào fail-secure case

```
1. Ghi audit.log: event "ConfigFallbackTriggered"
   detail = { reason: <1 trong 5 lý do bảng 6.1>, previous_schema_version: <nếu đọc được> }
   (audit.log độc lập với config.db — ghi được ngay cả khi config.db hỏng hoàn toàn)

2. Nạp MonitoringState vào RAM bằng hard-code default:
   - monitoring_enabled = true
   - risk_threshold = DEFAULT_RISK_THRESHOLD (hằng số code, benchmark ở BE-090/Đợt 1)
   - capture_interval_baseline_ms = DEFAULT_CAPTURE_INTERVAL_MS (hằng số code, PERF-010/Đợt 7)
   - exclude_process_names = []   (rỗng — ANTI-070, KHÔNG dùng BE-073a ở nhánh này)
   - using_fallback_config = true (runtime, phiên hiện tại)

3. Nạp PauseState vào RAM: is_paused = false (không tin bất kỳ timestamp cũ nào)

4. Sinh MỚI khoá HMAC 32 byte cho ipc_keys (không thể khôi phục khoá cũ nếu config.db hỏng toàn bộ, mục 3.5)
   — hệ quả: Vision/Overlay đang chạy (nếu có) phải được Service restart để nhận khoá mới qua
   anonymous pipe lúc spawn lại (đúng cơ chế bootstrap đã chốt ở 03-ipc-communication.md ADR-18,
   không cần thiết kế thêm kênh "rotate khoá khi đang chạy" ở đây).

5. Tạo file config.db MỚI HOÀN TOÀN (xoá file cũ nếu tồn tại nhưng hỏng, không sửa tại chỗ):
   schema_meta (schema_version = version hiện tại code, last_fallback_event_unix_ms = now)
   monitoring_state (data từ bước 2, using_fallback_config = false ← đã ghi đè thành công, xem mục 3.3)
   pause_state (data từ bước 3)
   ipc_keys (khoá mới từ bước 4)
   audit_meta (tái tạo từ dòng cuối audit.log thật, mục 5.4 — vì audit.log không bị đụng tới ở luồng này)
   icon_positions (rỗng — bổ sung v0.2.0, mục 3.6a; hệ quả duy nhất là icon quay về vị trí mặc định, không phải rủi ro an toàn)
   (BE-061a — ghi đè để các lần khởi động sau đọc được config hợp lệ ngay)

6. Gửi ControlVisionCommand/OverlayRectListCommand phản ánh MonitoringState mới cho Vision/Overlay
   (đúng trình tự handshake đã có ở 03-ipc-communication.md mục 4.3)

7. Gửi Windows Toast Notification qua Overlay (BE-061b/ANTI-070b) — nội dung: "Cấu hình giám sát
   không còn toàn vẹn, vui lòng kiểm tra lại."

8. Service chuyển state machine sang Running·Monitoring (flag using_fallback_config chỉ còn ý nghĩa
   lịch sử qua schema_meta.last_fallback_event_unix_ms, không phải trạng thái treo mãi mãi — khớp
   Degraded·FailSecure là state SONG SONG theo ADR-14 ở 02-process-architecture.md, không phải
   nhánh loại trừ Running·Monitoring)
```

- Bước 1 đặt **trước** bước 4/5 có chủ đích: nếu `Service` crash giữa chừng lúc đang ghi đè `config.db` (bước 5), audit log vẫn còn ghi nhận sự kiện gốc — lần khởi động kế tiếp lại rơi vào case "file không mở được"/tồn tại nhưng dở dang, tiếp tục đúng luồng này (idempotent theo thiết kế — chạy lại từ đầu luôn an toàn, không có bước nào giả định trạng thái trung gian cụ thể).

### 6.3 Không mất `audit.log` cũ

Toàn bộ luồng mục 6.2 **không đụng tới `audit.log`** — chỉ `config.db` bị xoá/tạo mới. `audit.log` là file độc lập hoàn toàn (mục 5), nên dù `config.db` bị tamper/hỏng nặng tới đâu, lịch sử audit trước đó vẫn nguyên vẹn để phụ huynh xem lại (đọc lại checkpoint theo mục 5.4).

## 7. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-23 | SQLite dùng WAL journal mode | Chống crash-corruption tốt hơn rollback-journal mặc định cho write pattern của `Service` (ghi định kỳ, đọc lúc boot); đánh đổi thêm file `-wal`/`-shm` cùng ACL |
| ADR-24 | Mã hoá DPAPI theo đơn vị "1 dòng = 1 blob JSON", không mã hoá cả file SQLite lẫn từng field riêng lẻ | SQLite không hỗ trợ mã hoá file minh bạch sẵn (cần dependency thứ ba chưa được quyết định); mã hoá theo blob JSON vừa đủ đơn giản vừa cho phép thêm field không cần `ALTER TABLE` (yêu cầu schema versioning không phá huỷ) |
| ADR-25 | Bảng `ipc_keys` khoá chính theo `channel` dù hiện chỉ có 1 dòng dùng chung | Chừa chỗ tách khoá riêng Vision/Overlay sau này bằng cách thêm dòng, không cần đổi cấu trúc bảng — không đổi hành vi hiện tại (vẫn 1 khoá chung theo đúng `03-ipc-communication.md` mục 5.2) |
| ADR-26 | Persist `rate_limit` counter (`PWD-021`/`022`) trong `auth.dat` thay vì chỉ giữ RAM | Tránh bị bypass rate-limit bằng cách cố tình làm `Service` crash để Watchdog restart (`ANTI-010`) reset bộ đếm — mở rộng nhẹ phạm vi lưu trữ so với chữ nghĩa gốc `PWD-0xx`, cần chủ dự án xác nhận (mục 8) |
| ADR-27 | Coi mọi lỗi đọc `config.db` là hỏng **toàn file**, không phục hồi từng bảng | Khớp đúng nghĩa đen `BE-061`/`ANTI-070`, tránh logic phục hồi từng phần phức tạp ngoài phạm vi yêu cầu |
| ADR-28 | Audit log verify toàn bộ chain là on-demand (Đợt 6/8), boot-time chỉ verify N record cuối (N=50 khởi điểm) | Cân bằng chi phí hiệu năng (quét toàn bộ lịch sử mỗi boot không cần thiết) với khả năng phát hiện sớm tamper gần thời điểm restart |
| ADR-29 | `audit.log` dùng JSONL plaintext, hash-chain theo `hash` field (không chain theo byte thô của dòng đã ghi) | Minh bạch (`SEC-003`), dễ viết công cụ verify độc lập (`MISC-010`); tách hash-chain khỏi chi tiết format hiển thị tránh lỗi vặt do whitespace/encoding |
| ADR-70 (v0.2.0) | Bảng mới `icon_positions` (mục 3.6a) plaintext, khoá chính `device_name` (Win32 `szDevice`, không phải `monitor_id` của `Vision`/IPC) — không có đường phục hồi riêng khi rơi vào nhánh fail-secure, chấp nhận mất theo `config.db` (ADR-27) | Toạ độ icon không nhạy cảm, không cần DPAPI; `device_name` là định danh ổn định duy nhất `Overlay` có sẵn cho 1 màn hình vật lý xuyên suốt các lần khởi động (`monitor_id` chỉ ổn định trong 1 phiên — `03-ipc-communication.md` mục 3.3); hệ quả mất dữ liệu (icon về vị trí mặc định) không đủ nghiêm trọng để cần logic phục hồi riêng, giữ đúng tinh thần đơn giản hoá của ADR-27 |

## 8. Câu hỏi mở / vấn đề cần xác nhận

- [ ] **Không nhất quán nhỏ giữa 2 file spec đã Approved**: `02-backend-spec.md` mục 4 (bảng lưu trữ, dòng "Audit log") ghi cột mã hoá là *"DPAPI + hash chain chống sửa log"*, trong khi `04-security-spec.md` `SEC-041` nói rõ *"không cần mã hoá nội dung (vì không chứa dữ liệu nhạy cảm) nhưng cần đảm bảo tính toàn vẹn"*. File này (mục 5) đã **chọn theo `SEC-041`** (không mã hoá nội dung, chỉ hash-chain + ACL ghi) vì đây là quyết định chi tiết hơn, có lý do rõ ràng, và audit log design cần biết chắc trước khi code. Đề nghị `spec-maintainer` sửa lại dòng bảng ở `02-backend-spec.md` mục 4 cho khớp `SEC-041` (bỏ chữ "DPAPI" ở cột mã hoá của dòng Audit log) để 2 file không mâu thuẫn nhau — đây là sửa câu chữ làm rõ, không đổi ý nghĩa yêu cầu, nhưng vẫn cần qua đúng quy trình archive vì đụng vào 1 dòng bảng đã approved.
- [ ] **ADR-26 (persist rate-limit counter)** mở rộng nhẹ phạm vi lưu trữ so với `PWD-021`/`022` gốc (vốn không nói rõ counter có cần sống sót qua restart `Service` hay không) — đề nghị chủ dự án xác nhận đây là cách hiểu đúng tinh thần chống bypass đã có, không phải thêm yêu cầu sản phẩm mới ngoài ý định ban đầu.
- [ ] Giá trị cụ thể `DEFAULT_RISK_THRESHOLD`/`DEFAULT_CAPTURE_INTERVAL_MS` và N (số record verify lúc boot ở ADR-28) — để benchmark ở Đợt 1/7/8 tương ứng, không chốt số ở tài liệu kiến trúc này.

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.1 | 2026-09-19 | PATCH — amendment cùng lượt viết `08-password-authentication-architecture.md` (Đợt 3). Thêm 2 dòng vào bảng `event_type` mục 5.1: `VisionNetworkBlocked` (đóng nợ kỹ thuật còn treo từ `06-security-architecture.md` v0.1.0 mục 7 — nội dung `detail` đã định nghĩa từ trước, chỉ thiếu dòng ở bảng này) và `AuthBruteForceThresholdReached` (mới, `PWD-021` mức cảnh báo cao ≥9 lần sai liên tiếp). Xác nhận (không sửa nội dung): schema `auth.dat` mục 4 đã đủ cho Đợt 3, không cần amendment nào khác — xem `08` mục 2 |
| v0.2.0 | 2026-09-19 | MINOR — amendment cùng lượt viết lại `07-overlay-architecture.md` v0.2.0 (`BE-088a`/`BE-089a`/`BE-089b`, `FE-016f`/`FE-016g`, `FE-020`–`022`). (1) Bổ sung event `ForceCloseRequested` vào bảng event_type mục 5.1 (đã có trong code Đợt 1, trước đó thiếu trong bảng — gap câu chữ) + field `source` bắt buộc trong `detail` (`"manual"`/`"auto-timeout"`, `BE-089b`), map trực tiếp từ enum `CloseSource` nhận qua IPC. (2) Bảng mới `icon_positions` (mục 3.6a, ADR-70) — lưu vị trí icon sau kéo-thả (`FE-020a`), khoá theo `device_name` (Win32 `szDevice`, tách biệt hoàn toàn `monitor_id`), plaintext, không có đường phục hồi riêng khi fail-secure (chấp nhận mất theo `config.db`, hệ quả không nghiêm trọng). Cập nhật luồng fail-secure mục 6.2 bước 5 (thêm `icon_positions` rỗng vào danh sách bảng tái tạo). 1 ADR mới (70) |
| v0.1.0 | 2026-09-17 | Khởi tạo — layout file đầy đủ trên đĩa (`%ProgramFiles%`/`%ProgramData%`, ACL chỉ SYSTEM), schema `config.db` (`schema_meta`/`monitoring_state`/`pause_state`/`ipc_keys`/`audit_meta`, mã hoá DPAPI theo blob JSON/dòng), `auth.dat` tách biệt cho password/recovery key hash (`PWD-013`), format `audit.log` hash-chain JSONL + quy trình verify/xử lý chain đứt, luồng fail-secure chi tiết hoá `BE-061`/`061a`/`061b`/`ANTI-070`, schema versioning, 7 ADR (23-29), phát hiện 1 điểm không nhất quán nhỏ giữa `02-backend-spec.md` và `SEC-041` cần `spec-maintainer` sửa |

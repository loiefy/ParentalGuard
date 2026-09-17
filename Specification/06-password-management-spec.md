# 06 — Password Management Spec

> Version: v0.3.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Phạm vi

Mật khẩu quản trị (parent password) bảo vệ các hành động: gỡ cài đặt, tạm dừng giám sát, đổi cấu hình nhạy cảm, xem/xoá audit log. Đây **không phải** mật khẩu đăng nhập Windows — là mật khẩu riêng của app.

## 2. Đặt mật khẩu lần đầu (Setup)

- `PWD-001`: Bắt buộc đặt mật khẩu trong luồng Onboarding (`FE-030`), không cho phép bỏ qua bước này.
- `PWD-002`: Yêu cầu độ mạnh tối thiểu: ≥ 8 ký tự, khuyến nghị (không bắt buộc cứng nhắc) kết hợp chữ+số để cân bằng giữa bảo mật và khả năng phụ huynh nhớ được — hiện thanh đo độ mạnh (weak/medium/strong) để hướng dẫn, không chặn cứng nếu phụ huynh chấp nhận rủi ro với mật khẩu ngắn hơn khuyến nghị.
- `PWD-002a` (ĐÃ CHỐT v0.2.0): Giới hạn độ dài **tối đa 50 ký tự** — đủ dài cho passphrase hợp lý, đồng thời tránh input khổng lồ gây tốn tài nguyên/DoS khi hash bằng Argon2id (chi phí hash tỉ lệ với độ dài input). Chặn cứng ở UI (không cho gõ/paste quá 50 ký tự) lẫn ở backend khi xử lý.
- `PWD-003`: Yêu cầu nhập lại (confirm) để tránh gõ nhầm.
- `PWD-004`: Ngay sau khi đặt mật khẩu, bắt buộc thiết lập **cơ chế khôi phục** (mục 5) trước khi hoàn tất onboarding — không cho phép trì hoãn, vì đây là nguyên nhân chính gây khoá tài khoản vĩnh viễn nếu bỏ qua.

## 3. Lưu trữ mật khẩu an toàn

- `PWD-010`: **Không bao giờ lưu plaintext** dưới bất kỳ hình thức nào (kể cả tạm thời trong RAM lâu hơn mức cần thiết để hash).
- `PWD-011`: Thuật toán hash: **Argon2id** (khuyến nghị hiện tại cho password hashing, chống được cả tấn công GPU-based lẫn side-channel tốt hơn bcrypt/PBKDF2), tham số tối thiểu tham khảo theo khuyến nghị OWASP hiện hành (memory cost, iterations, parallelism — cần benchmark cụ thể trên máy cấu hình thấp để cân bằng UX chờ xác thực).
- `PWD-012`: Salt ngẫu nhiên riêng cho mỗi lần đặt/đổi mật khẩu, lưu cùng hash (không cần bí mật salt, đây là thực hành chuẩn).
- `PWD-013`: Hash + salt lưu trong file riêng biệt với `config.db`, mã hoá bổ sung bằng DPAPI machine-scope (defense in depth — kể cả nếu attacker đọc được file, vẫn cần phá cả Argon2id lẫn DPAPI).
- `PWD-014`: File chứa hash đặt ACL chặn Read của user thường (chỉ SYSTEM/Service đọc được) — tránh việc trẻ copy file ra máy khác để bruteforce offline thoải mái.

## 4. Xác thực khi thực hiện hành động nhạy cảm

- `PWD-020`: Modal xác thực (`S5`) xuất hiện mỗi khi: gỡ cài đặt, tạm dừng giám sát, đổi ngưỡng nhạy cảm, xem/xoá log, đổi mật khẩu.
- `PWD-021`: **Rate-limiting chống bruteforce**:
  | Lần sai liên tiếp | Hành động |
  |---|---|
  | 1-3 | Cho thử lại ngay |
  | 4-5 | Delay 30 giây trước khi cho thử lại |
  | 6-8 | Delay 5 phút |
  | ≥ 9 | Delay 30 phút + ghi audit log mức cảnh báo cao |
- `PWD-022`: Rate-limit áp dụng theo **thời gian thực tại Service** (không dựa vào đồng hồ hệ thống hoàn toàn để tránh bypass bằng cách chỉnh giờ máy — dùng thêm monotonic clock/tick count của hệ thống làm tham chiếu phụ).
- `PWD-023`: Không giới hạn cứng số lần thử trong ngày (tránh tự khoá phụ huynh vĩnh viễn nếu gõ sai nhiều), chỉ tăng delay luỹ tiến.

## 5. Cơ chế quên mật khẩu (Recovery) — thiết kế cân bằng giữa an toàn và khả dụng

Đây là phần nhạy cảm nhất: **cơ chế recovery quá dễ = lỗ hổng để trẻ tự "quên mật khẩu" giả vờ và chiếm quyền; quá khó = phụ huynh bị khoá khỏi máy của chính họ.**

### Phương án chính: Recovery Key sinh ra lúc setup (offline, không qua email/SMS)

- `PWD-030`: Lúc Onboarding, sau khi đặt mật khẩu, hệ thống sinh 1 **Recovery Key** ngẫu nhiên (dạng chuỗi 24-32 ký tự, chia nhóm dễ đọc kiểu `XXXX-XXXX-XXXX-XXXX`), chỉ hiển thị **một lần duy nhất** và yêu cầu phụ huynh tự lưu trữ offline (in ra giấy, lưu password manager riêng...).
- `PWD-030a` (ĐÃ CHỐT v0.2.0): Màn hình hiển thị Recovery Key bắt buộc có checkbox xác nhận **"Tôi đã lưu lại Recovery Key"**, không tick chọn thì không cho bấm Tiếp tục. Nếu phụ huynh thoát/huỷ mà chưa tick xác nhận, toàn bộ luồng **thiết lập mật khẩu coi như chưa thành công** — Onboarding không hoàn tất, app chưa bắt đầu kích hoạt giám sát (liên kết `PWD-004`, `FE-030`), buộc phụ huynh quay lại từ đầu bước đặt mật khẩu để tránh trường hợp có mật khẩu nhưng không có Recovery Key hợp lệ nào được xác nhận đã lưu.
- `PWD-031`: Hash của Recovery Key (không phải plaintext) được lưu local theo cùng cơ chế bảo mật như mật khẩu chính (`PWD-010` đến `PWD-014`).
- `PWD-032`: Luồng khôi phục: phụ huynh chọn "Quên mật khẩu" → nhập Recovery Key → nếu đúng → cho phép đặt mật khẩu mới ngay lập tức, đồng thời sinh Recovery Key mới (key cũ bị vô hiệu sau khi dùng).
- `PWD-033`: Sai Recovery Key cũng áp dụng rate-limiting tương tự `PWD-021`.

### Vì sao không dùng email/SMS reset (khác với đa số app thông thường)

- Việc gửi email/SMS đòi hỏi network call và thu thập thông tin liên hệ của phụ huynh → vi phạm nguyên tắc `SEC-001` (local-first tuyệt đối) và tăng bề mặt tấn công (nếu email phụ huynh bị chiếm, kẻ tấn công remote có thể reset mật khẩu app từ xa — rủi ro không tồn tại với thiết kế offline).
- Đánh đổi: nếu phụ huynh làm mất cả mật khẩu lẫn Recovery Key, không có cách khôi phục nào khác ngoài gỡ cài đặt hoàn toàn (xoá dữ liệu) và cài lại — đây là đánh đổi có chủ đích, cần nêu rõ trong tài liệu người dùng ngay từ bước Onboarding.

### Phương án dự phòng: Câu hỏi bảo mật bổ sung (ĐÃ CHỐT v0.2.0 — triển khai Phase 2)

- `PWD-034` (ĐÃ CHỐT v0.2.0 — **Phase 2, không nằm trong Phase 1**): Ngoài Recovery Key, cho phép thiết lập 2-3 câu hỏi bảo mật tự chọn (không dùng câu hỏi có sẵn dễ đoán kiểu "tên thú cưng đầu tiên" — để phụ huynh tự soạn câu hỏi và câu trả lời) như lớp fallback thứ 2 nếu mất luôn Recovery Key. Rủi ro: trẻ có thể biết câu trả lời nếu phụ huynh chọn câu hỏi không đủ riêng tư — cần cảnh báo rõ trong UI lúc thiết lập. Quyết định để Phase 2 vì cần feedback thực tế về tần suất phụ huynh mất cả 2 lớp bảo vệ đầu trước khi đầu tư xây thêm lớp thứ 3.
- `PWD-035` (ĐÃ CHỐT v0.3.0): Quy tắc chuẩn hoá câu trả lời câu hỏi bảo mật, để giảm rủi ro phụ huynh tự khoá mình do nhập sai định dạng lúc khôi phục:
  - Không phân biệt chữ hoa/chữ thường (so sánh sau khi lowercase — áp dụng đúng cho cả chữ có dấu tiếng Việt, ví dụ `Đ`/`đ` coi như nhau).
  - **Ký tự hợp lệ được phép nhập**: chữ cái Latin cơ bản (a-z, A-Z) **và chữ cái tiếng Việt có dấu** (à á ả ã ạ ăằắẳẵặ âầấẩẫậ è é ẻ ẽ ẹ êềếểễệ ì í ỉ ĩ ị ò ó ỏ õ ọ ôồốổỗộ ơờớởỡợ ù ú ủ ũ ụ ưừứửữự ỳ ý ỷ ỹ ỵ đ và bản viết hoa tương ứng) — đây là chữ cái, **không phải** ký tự đặc biệt.
  - **Cấm chữ số** (0-9).
  - **Cấm ký tự đặc biệt/symbol**, cụ thể: `! " # $ % & ' ( ) * + - / : ; < = > ? @ [ \ ] ^ _ \` { | } ~` và mọi emoji/ký tự biểu tượng Unicode khác.
  - Khoảng trắng, dấu phẩy `,`, dấu chấm `.` **không bị chặn nhập** — nhưng trước khi hash & lưu (lúc thiết lập) cũng như trước khi so sánh (lúc xác thực), hệ thống tự động loại bỏ 3 ký tự này khỏi câu trả lời đã nhập, vì phụ huynh dễ gõ thiếu chúng khi nhớ lại câu trả lời sau này.

## 6. Đổi mật khẩu (khi đã đăng nhập hợp lệ)

- `PWD-040`: Yêu cầu nhập mật khẩu cũ trước khi đổi mật khẩu mới (trừ trường hợp qua luồng Recovery Key ở mục 5).
- `PWD-041`: Sau khi đổi mật khẩu thành công, tự động hỏi có muốn sinh lại Recovery Key mới không (khuyến nghị có, để đảm bảo Recovery Key không "già" quá lâu so với mật khẩu hiện tại — dù về mặt kỹ thuật 2 thứ độc lập nhau).

## 7. Bảo vệ trong bộ nhớ khi xử lý mật khẩu (Runtime memory hygiene)

- `PWD-050`: Dùng kiểu dữ liệu cho phép zero-out sau khi dùng (ví dụ `SecureString` cân nhắc, hoặc tự quản lý `byte[]` và ghi đè 0 ngay sau khi hash xong) để giảm thời gian plaintext password tồn tại trong RAM.
- `PWD-051`: Không log giá trị mật khẩu/Recovery Key dưới bất kỳ log level nào, kể cả debug build (cần lint/test tự động kiểm tra, xem `11-testing-qa-process.md`).

## 8. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.3.0 | 2026-09-17 | **Chốt nốt câu hỏi mở cuối**: `PWD-035` bổ sung định nghĩa tường minh "ký tự đặc biệt" — liệt kê rõ danh sách symbol bị cấm, xác nhận chữ cái tiếng Việt có dấu (ă, â, ê, ô, ơ, ư, đ...) là chữ cái hợp lệ, không phải ký tự đặc biệt. Không còn câu hỏi mở |
| v0.2.0 | 2026-09-17 | **Chốt 3 câu hỏi mở**: `PWD-002a` giới hạn mật khẩu tối đa 50 ký tự; `PWD-030a` bắt buộc tick xác nhận "Tôi đã lưu lại Recovery Key" trước khi hoàn tất thiết lập mật khẩu (không xác nhận = thiết lập không thành công); `PWD-034` chốt triển khai câu hỏi bảo mật ở Phase 2 + thêm `PWD-035` quy tắc chuẩn hoá câu trả lời (không phân biệt hoa/thường, cấm ký tự đặc biệt/chữ số, tự lược bỏ khoảng trắng/dấu phẩy/dấu chấm trước khi hash). Phát sinh câu hỏi mở mới về xử lý dấu tiếng Việt trong câu trả lời |
| v0.1.0 | 2026-09-17 | Khởi tạo |

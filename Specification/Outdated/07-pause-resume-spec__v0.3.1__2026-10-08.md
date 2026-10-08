# 07 — Pause / Resume Mechanism Spec

> Version: v0.3.1 | Trạng thái: Approved | Cập nhật: 2026-10-07

## 1. Mục đích

Cho phép phụ huynh tạm dừng giám sát trong tình huống hợp lệ (ví dụ chính phụ huynh cần dùng máy để xử lý công việc riêng tư, hoặc troubleshooting hiệu năng) mà không cần gỡ hẳn app — đồng thời tránh biến tính năng này thành lỗ hổng để trẻ lợi dụng.

## 2. Yêu cầu chức năng

- `PAUSE-001`: Chỉ có thể kích hoạt tạm dừng sau khi xác thực mật khẩu thành công (dùng chung modal `S5` / cơ chế ở `06-password-management-spec.md`).
- `PAUSE-002`: Bắt buộc chọn **thời lượng tạm dừng** từ danh sách định sẵn khi kích hoạt — **không có tuỳ chọn "tạm dừng vô thời hạn"**:
  - 15 phút / 30 phút / 1 giờ / 4 giờ / Hết ngày hôm nay.
- `PAUSE-002a` (ĐÃ CHỐT v0.2.0): Mốc "Hết ngày hôm nay" tính đến **23:59 theo giờ hệ thống máy tính**, dùng cứng — **không có cấu hình "giờ đi ngủ" riêng** ở Phase 1.
- `PAUSE-002b` (ĐÃ CHỐT v0.2.0): Phase 1 **chỉ hỗ trợ tạm dừng toàn bộ giám sát**, không phân biệt/loại trừ theo từng app hoặc browser cụ thể — vì user case thực tế cần tạm dừng rất đa dạng (không chỉ giới hạn ở 1-2 tình huống có thể liệt kê trước), tương tự lý do đã bỏ whitelist theo app ở `BE-072`/`BE-073`.
- `PAUSE-003`: Sau khi hết thời lượng đã chọn, giám sát **tự động resume** mà không cần thao tác gì thêm — không phụ thuộc vào việc app UI có đang mở hay không (logic đếm giờ nằm ở Service, không phải UI).
- `PAUSE-004`: Phụ huynh có thể chủ động resume sớm hơn thời lượng đã chọn (xác thực mật khẩu lại để resume sớm — đối xứng với việc pause, tránh trường hợp ai đó khác resume hộ nếu không nên).

## 3. Hiển thị trạng thái tạm dừng (liên kết `FE-021`, `S9`)

- `PAUSE-010`: Trong suốt thời gian tạm dừng, icon trạng thái (`S8`) chuyển màu vàng + hiện đếm ngược thời gian còn lại khi hover.
- `PAUSE-011`: Banner nhỏ (`S9`) xuất hiện định kỳ (ví dụ mỗi 10 phút một lần, không liên tục gây phiền) nhắc rằng giám sát đang tạm dừng và còn lại bao lâu — mục đích minh bạch, tránh quên bật lại và tránh trẻ lợi dụng khoảng "im lặng" quá lâu mà không ai để ý.

## 4. Giới hạn tần suất tạm dừng (chống lạm dụng)

- `PAUSE-020`: Ghi nhận số lần và tổng thời lượng tạm dừng trong audit log (metadata: thời điểm bắt đầu/kết thúc, ai xác thực — không cần thiết phải biết "ai" nếu chỉ có 1 mật khẩu chung, nhưng ghi nhận đủ để phụ huynh tự đối chiếu).
- `PAUSE-021` (ĐÃ CHỐT v0.2.2, APPROVED — chủ dự án xác nhận trực tiếp 2026-09-20): Nếu tần suất tạm dừng bất thường cao trong 1 khoảng thời gian ngắn (**> 5 lần/ngày, con số chính thức, không còn là ví dụ minh hoạ**) → hiện cảnh báo nhẹ trên Dashboard lần tiếp theo phụ huynh mở app, gợi ý xem lại — đây không phải để chặn hành vi (phụ huynh có toàn quyền), mà để tăng nhận thức, phòng trường hợp mật khẩu đã bị lộ và ai đó khác đang tạm dừng liên tục.

## 4a. Chế độ "Bảo vệ cả phụ huynh" (mới v0.3.0, 2026-10-07 — chủ dự án yêu cầu trực tiếp (TODO mục 9); chi tiết trò chơi do đội phát triển chọn — chủ dự án yêu cầu "làm hết" 2026-10-07; chi tiết do đội phát triển chọn, ghi rõ để chủ dự án điều chỉnh)

- `PAUSE-040`: Mục **"Bảo vệ cả phụ huynh"** ở `S4` (cần đăng nhập — `FE-080`), mặc định **tắt**. Khi bật, hiện thông báo giải thích: dù có mật khẩu, phụ huynh cũng không thể tạm dừng giám sát ngay — phải hoàn thành 1 thử thách ngắn.
- `PAUSE-041`: Khi chế độ đang bật, **tạm dừng giám sát** = nhập đúng mật khẩu (`PAUSE-001`) **và** vượt qua thử thách: **5 phép tính** (cộng/trừ/nhân, số có 1–2 chữ số) trong **60 giây**, phải đúng **5/5**. Sai hoặc hết giờ → làm lại với bộ câu hỏi mới; **thất bại 3 lần liên tiếp → khoá thử thách 5 phút**.
- `PAUSE-042`: Câu hỏi do `Service` sinh ngẫu nhiên và `Service` tự chấm (giao diện không tự chấm), kết quả "đã vượt qua" chỉ có hiệu lực cho đúng 1 lần tạm dừng trong 2 phút. **Tắt** chế độ này cũng phải vượt qua thử thách (tránh lách bằng cách tắt rồi tạm dừng). Bật chế độ không cần thử thách.
- `PAUSE-043`: Chế độ này chỉ áp dụng cho **tạm dừng** và **tắt chế độ**; tiếp tục giám sát sớm (`PAUSE-004`) và gỡ cài đặt giữ nguyên như cũ. Cảnh báo tần suất tạm dừng (`PAUSE-021`) vẫn tính bình thường. Thẻ "Bảo vệ cả phụ huynh" ở màn hình giới thiệu lần đầu (`FE-032`) nay luôn được hiển thị vì tính năng đã có (thẻ chỉ giới thiệu tính năng, không phụ thuộc chế độ đang bật hay tắt). Vì mã xác thực tạm dừng chỉ có hiệu lực 15 giây, thử thách được làm **trước** bước nhập mật khẩu.

## 5. Ràng buộc kỹ thuật

- `PAUSE-030`: Trạng thái pause được lưu trong `config.db` (mã hoá DPAPI như đã mô tả ở `BE-060`/`SEC-040`) kèm timestamp hết hạn — để nếu máy tắt/khởi động lại giữa lúc đang pause, Service khi khởi động đọc lại trạng thái và tự tính toán còn pause hay đã hết hạn (không tự ý resume ngay khi khởi động lại nếu vẫn còn trong thời gian đã chọn, và không tự ý pause thêm nếu đã hết hạn).
- `PAUSE-031`: Khi đang pause, `Vision` process có thể được tạm dừng thực sự (suspend) thay vì tắt hẳn, để tiết kiệm CPU nhưng vẫn giữ heartbeat với Service ở tần suất thấp hơn — tránh watchdog hiểu nhầm là bị tấn công (liên kết `ANTI-060`).

## 6. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 7. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.3.1 | 2026-10-07 | **PATCH (làm rõ câu chữ)**: `PAUSE-043` — thẻ giới thiệu `FE-032` luôn hiển thị (bản cũ viết nhầm "khi chế độ đang bật"); ghi rõ thứ tự thử thách trước mật khẩu. Archive: `Outdated/07-pause-resume-spec__v0.3.0__2026-10-07.md` |
| v0.3.0 | 2026-10-07 | **MINOR (chủ dự án yêu cầu "làm hết" 2026-10-07; chi tiết do đội phát triển chọn, ghi rõ để chủ dự án điều chỉnh)**: mục 4a "Bảo vệ cả phụ huynh" — `PAUSE-040`–`PAUSE-043` (thử thách 5 phép tính/60 giây, Service sinh và chấm, khoá 5 phút sau 3 lần thất bại; áp dụng cho tạm dừng và tắt chế độ) |
| v0.2.2 | 2026-09-20 | **Vá gap quy trình `PROPOSED → APPROVED`**: `architecture-writer` phát hiện `PAUSE-021` vẫn còn tag `(PROPOSED)` trong văn bản dù toàn file đã đóng dấu `Approved` từ v0.2.1 và mục 6 ghi "không còn câu hỏi mở" (2 tầng trạng thái file vs requirement không khớp nhau). Chủ dự án xác nhận trực tiếp 2026-09-20: DUYỆT `PAUSE-021`, giữ nguyên ngưỡng đã có sẵn trong spec **> 5 lần/ngày** làm con số chính thức (trước đó chỉ ghi là "ví dụ minh hoạ"). Đổi tag từ `(PROPOSED)` sang `(ĐÃ CHỐT v0.2.2, APPROVED)`. Rà soát toàn file: không còn requirement `PAUSE-0xx` nào khác sót tag `PROPOSED`. Archive: `Specification/Outdated/07-pause-resume-spec__v0.2.1__2026-09-20.md` |
| v0.2.1 | 2026-09-17 | Chủ dự án approve toàn bộ requirement trong file này — chuyển trạng thái file từ `Draft` sang `Approved` |
| v0.2.0 | 2026-09-17 | **Chốt 2 câu hỏi mở**: `PAUSE-002a` — mốc "Hết ngày hôm nay" dùng cứng 23:59 theo giờ hệ thống, không cấu hình "giờ đi ngủ" riêng ở Phase 1. `PAUSE-002b` — Phase 1 chỉ hỗ trợ tạm dừng toàn bộ giám sát, không phân biệt theo app/browser cụ thể (cùng lý do đã bỏ whitelist app ở `BE-072`/`BE-073`). Không còn câu hỏi mở |
| v0.1.0 | 2026-09-17 | Khởi tạo |

# 07 — Pause / Resume Mechanism Spec

> Version: v0.2.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

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
- `PAUSE-021` (PROPOSED): Nếu tần suất tạm dừng bất thường cao trong 1 khoảng thời gian ngắn (ví dụ > 5 lần/ngày) → hiện cảnh báo nhẹ trên Dashboard lần tiếp theo phụ huynh mở app, gợi ý xem lại — đây không phải để chặn hành vi (phụ huynh có toàn quyền), mà để tăng nhận thức, phòng trường hợp mật khẩu đã bị lộ và ai đó khác đang tạm dừng liên tục.

## 5. Ràng buộc kỹ thuật

- `PAUSE-030`: Trạng thái pause được lưu trong `config.db` (mã hoá DPAPI như đã mô tả ở `BE-060`/`SEC-040`) kèm timestamp hết hạn — để nếu máy tắt/khởi động lại giữa lúc đang pause, Service khi khởi động đọc lại trạng thái và tự tính toán còn pause hay đã hết hạn (không tự ý resume ngay khi khởi động lại nếu vẫn còn trong thời gian đã chọn, và không tự ý pause thêm nếu đã hết hạn).
- `PAUSE-031`: Khi đang pause, `Vision` process có thể được tạm dừng thực sự (suspend) thay vì tắt hẳn, để tiết kiệm CPU nhưng vẫn giữ heartbeat với Service ở tần suất thấp hơn — tránh watchdog hiểu nhầm là bị tấn công (liên kết `ANTI-060`).

## 6. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 7. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.0 | 2026-09-17 | **Chốt 2 câu hỏi mở**: `PAUSE-002a` — mốc "Hết ngày hôm nay" dùng cứng 23:59 theo giờ hệ thống, không cấu hình "giờ đi ngủ" riêng ở Phase 1. `PAUSE-002b` — Phase 1 chỉ hỗ trợ tạm dừng toàn bộ giám sát, không phân biệt theo app/browser cụ thể (cùng lý do đã bỏ whitelist app ở `BE-072`/`BE-073`). Không còn câu hỏi mở |
| v0.1.0 | 2026-09-17 | Khởi tạo |

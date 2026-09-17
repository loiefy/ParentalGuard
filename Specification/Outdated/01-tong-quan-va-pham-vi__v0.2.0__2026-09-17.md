# 01 — Tổng quan & Phạm vi

> Version: v0.2.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Bài toán

Các giải pháp chặn nội dung khiêu dâm truyền thống (DNS blocklist, domain filtering) không theo kịp tốc độ thay đổi domain của các trang porn hiện đại. ParentalGuard chuyển hướng sang **phát hiện nội dung thực tế trên màn hình bằng AI local**, độc lập với domain/URL, để phòng thủ bền vững hơn.

## 2. Mục tiêu sản phẩm (Goals)

| ID | Mục tiêu |
|---|---|
| GEN-010 | Phát hiện nội dung khiêu dâm hiển thị trên màn hình theo thời gian thực, **không giới hạn ở trình duyệt** — bao gồm browser, video player (VLC, Windows Media Player, PotPlayer...), ứng dụng xem ảnh, và về nguyên tắc là bất kỳ ứng dụng nào hiển thị nội dung trực quan trên màn hình |
| GEN-011 | Che (blur) nội dung phát hiện được ngay lập tức trên đúng cửa sổ ứng dụng vi phạm (bất kể loại ứng dụng), giảm thiểu thời gian trẻ tiếp xúc với nội dung, đồng thời không che khuất vùng điều khiển cửa sổ (nút đóng) để người dùng luôn thoát được (xem `GEN-016`) |
| GEN-012 | Đảm bảo không có dữ liệu hình ảnh nào rời khỏi máy hoặc bị lưu trữ lâu dài |
| GEN-013 | Không thể bị trẻ tắt/gỡ mà không có sự đồng ý của phụ huynh (xác thực mật khẩu) |
| GEN-014 | Minh bạch: luôn hiển thị icon cho biết app đang hoạt động, không ẩn giấu |
| GEN-015 | Hiệu năng đủ nhẹ để chạy nền liên tục mà không ảnh hưởng đáng kể trải nghiệm sử dụng máy |
| GEN-016 | Overlay chặn nội dung phải luôn chừa lối để người dùng tự đóng ứng dụng vi phạm qua nút đóng gốc của cửa sổ (ví dụ nút X trên title bar Windows), không được vô tình khoá người dùng lại trong màn hình nội dung phản cảm |

## 3. Đối tượng người dùng (Personas)

| Persona | Mô tả | Quyền hạn |
|---|---|---|
| **Phụ huynh / Người quản trị** | Cài đặt, cấu hình, đặt mật khẩu, xem log, tạm dừng/gỡ app | Toàn quyền, xác thực bằng mật khẩu |
| **Trẻ em / Người dùng máy** | Sử dụng máy tính hàng ngày, bị giám sát | Không có quyền cấu hình, chỉ thấy overlay khi bị chặn và icon trạng thái |

## 4. Phạm vi Phase 1 (In-scope)

- Nền tảng: Windows 10 (21H2+) và Windows 11, kiến trúc x64.
- Giám sát nội dung hiển thị trên **cửa sổ ứng dụng đang active (foreground)**, không giới hạn theo whitelist loại ứng dụng — bao gồm nhưng không giới hạn ở: trình duyệt web (Chrome, Edge, Firefox), video player (VLC, Windows Media Player, PotPlayer, MPC-HC...), ứng dụng xem ảnh, và các ứng dụng khác hiển thị nội dung trực quan. Chi tiết cách tiếp cận "giám sát theo cửa sổ active" thay vì whitelist theo tên ứng dụng ở `02-backend-spec.md` mục `BE-071`.
- Phát hiện ảnh tĩnh khiêu dâm bằng AI classifier local.
- Overlay blur khớp kích thước cửa sổ ứng dụng vi phạm, đồng thời chừa vùng nút đóng cửa sổ để người dùng luôn thoát được thủ công (xem `FE-016`).
- Force-close ứng dụng vi phạm sau khi người dùng xác nhận qua nút trên overlay.
- Cơ chế chống gỡ cài đặt bằng mật khẩu.
- Cơ chế quên mật khẩu (recovery).
- Cơ chế tạm dừng giám sát có kiểm soát.
- Tối ưu hiệu năng CPU/GPU.
- Audit log local (không có nội dung ảnh, chỉ metadata).

## 5. Ngoài phạm vi Phase 1 (Out-of-scope / Non-goals)

| Mục | Lý do loại trừ (tạm thời) |
|---|---|
| macOS, Android, iOS | Ưu tiên làm chắc 1 nền tảng trước, kiến trúc capture/overlay khác biệt hoàn toàn giữa các OS |
| Phát hiện nội dung trong video động (motion) độ chính xác cao | Phase 1 tập trung frame tĩnh; xử lý video (temporal analysis) phức tạp hơn, để phase 2 |
| Giám sát tin nhắn mã hoá đầu cuối (WhatsApp, Messenger, Signal...) | Không khả thi kỹ thuật (đã phân tích ở giai đoạn nghiên cứu) và ngoài mục tiêu ban đầu (chặn hình ảnh, không phải giám sát chat) |
| Đồng bộ cấu hình multi-device qua cloud | Trái với nguyên tắc local-first; nếu làm sau, phải là tuỳ chọn tường minh và mã hoá đầu-cuối do người dùng tự quản key |
| Dashboard xem từ xa qua điện thoại phụ huynh | Yêu cầu kênh truyền dữ liệu ra khỏi máy — mâu thuẫn với nguyên tắc bảo mật cốt lõi, cần bàn riêng nếu muốn làm ở phase sau |

## 6. Giả định (Assumptions)

- `GEN-020`: Phụ huynh có quyền quản trị (admin) trên máy Windows cần cài đặt.
- `GEN-021`: Máy chạy Windows bản chính hãng, không bị can thiệp sâu vào kernel bởi phần mềm khác gây xung đột driver.
- `GEN-022`: Trẻ em sử dụng máy không có quyền admin cấp hệ điều hành (khuyến nghị, không bắt buộc — cần ghi rõ rủi ro nếu trẻ có quyền admin).
- `GEN-023`: Người dùng chấp nhận đánh đổi hiệu năng máy (CPU/pin) ở mức độ hợp lý để đổi lấy khả năng bảo vệ.

## 7. Ràng buộc (Constraints)

- `GEN-030`: Không được có bất kỳ network request nào trong toàn bộ pipeline capture → phân tích → quyết định chặn.
- `GEN-031`: Không lưu ảnh chụp màn hình xuống disk hoặc bất kỳ vùng cache nào dưới mọi hình thức, kể cả tạm thời.
- `GEN-032`: Toàn bộ log phải loại trừ nội dung ảnh, chỉ chứa metadata (timestamp, hành động, mức độ tin cậy).
- `GEN-033`: App phải ký số (code signing) trước khi phát hành để giảm rủi ro bị Windows Defender/SmartScreen gắn cờ.

## 8. Rủi ro đã biết (Known Risks — theo dõi xuyên suốt dự án)

| Rủi ro | Mức độ | Ghi chú |
|---|---|---|
| False positive che nhầm nội dung hợp lệ (y tế, nghệ thuật, thời trang) | Cao | Cần cơ chế feedback + ngưỡng tin cậy có thể tinh chỉnh (xem `10-additional-mechanisms-spec.md`) |
| Bị Windows Defender/SmartScreen gắn cờ do hành vi giống stalkerware | Cao | Cần chiến lược code-signing + minh bạch, chi tiết ở `04-security-spec.md` |
| Tiêu tốn CPU/pin quá nhiều khiến người dùng muốn gỡ | Trung bình | Xem `08-performance-cpu-spec.md` |
| Trẻ có kỹ thuật vượt qua (dùng máy ảo, safe mode, boot từ USB khác) | Trung bình-Cao | Không thể giải quyết triệt để bằng software; ghi nhận là giới hạn cố hữu, cần truyền thông rõ với phụ huynh |
| Quên mật khẩu quản trị dẫn đến khoá luôn phụ huynh khỏi máy của chính họ | Trung bình | Cần cơ chế recovery an toàn nhưng không tạo lỗ hổng cho trẻ tự gỡ (xem `06-password-management-spec.md`) |

## 9. Tiêu chí thành công (Definition of Done cho Phase 1)

- Tất cả requirement `APPROVED` trong 11 file spec được implement và pass test tương ứng ở `11-testing-qa-process.md`.
- App chạy ổn định 72 giờ liên tục trên máy test mà không crash, không memory leak đáng kể.
- Tỷ lệ false positive/false negative được đo và ghi nhận trên bộ test ảnh chuẩn (xem `09-image-processing-spec.md`).
- Vượt qua kiểm thử chống gỡ bởi người dùng có hiểu biết kỹ thuật trung bình (không phải chuyên gia bảo mật).

## Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.0 | 2026-09-17 | Mở rộng phạm vi giám sát từ "chỉ browser" sang "mọi ứng dụng hiển thị nội dung trực quan" (GEN-010); thêm yêu cầu GEN-016 đảm bảo overlay không che nút đóng cửa sổ |
| v0.1.0 | 2026-09-17 | Khởi tạo |

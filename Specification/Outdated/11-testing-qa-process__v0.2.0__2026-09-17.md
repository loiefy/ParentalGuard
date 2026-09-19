# 11 — Testing & QA Process Spec

> Version: v0.2.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Nguyên tắc quy trình dev từng giai đoạn (Feature Gate Process)

Đúng theo yêu cầu: **mỗi feature phải Dev → Test → Review → Approve trước khi sang feature tiếp theo**. Quy trình chuẩn cho mỗi giai đoạn code:

```
[Dev]
  Implement 1 feature theo đúng requirement ID đã APPROVED trong spec
        ↓
[Self-test]
  Dev tự chạy unit test + smoke test cơ bản trước khi báo hoàn thành
        ↓
[Test theo checklist]
  Chạy đầy đủ bộ test case tương ứng feature đó (mục 3 bên dưới)
        ↓
[Review kết quả]
  Đối chiếu kết quả test với Acceptance Criteria trong spec gốc
        ↓
   ┌─── Đạt ───→ [Approve] → Đánh dấu requirement là VERIFIED → Sang feature tiếp theo
   │
   └─── Không đạt ───→ Quay lại [Dev], ghi nhận lý do fail, không được bỏ qua bước test
```

- `TEST-001`: Không có ngoại lệ "để sau" cho các test liên quan bảo mật/privacy (IMG-040, PWD-*, ANTI-*) — đây là các mục bắt buộc pass 100% trước khi feature được coi là hoàn thành, khác với test UX/hiệu năng có thể có threshold linh hoạt hơn.
- `TEST-002` (ĐÃ CHỐT v0.2.0): Có tự động hoá — nhưng thay vì (hoặc bổ sung) pipeline CI truyền thống chạy script cố định, cơ chế tự động hoá chính là **Claude Code Agent** đảm nhiệm các bước `[Dev]`/`[Self-test]`/`[Test theo checklist]`/`[Review kết quả]` ở trên: 1 agent code feature theo đúng requirement ID đã `APPROVED`, 1 agent chạy bộ test case tương ứng (mục 3), 1 agent debug khi test fail và báo cáo lại nguyên nhân, 1 agent tổng hợp report kết quả để trình lên bước `[Approve]`. Việc thiết lập CI truyền thống (nếu vẫn cần, ví dụ để chạy song song đối chiếu) không bị loại trừ nhưng không phải trọng tâm. **Chi tiết cấu hình agent cụ thể (workflow, vai trò từng agent, điều kiện trigger) để ở giai đoạn System Design/triển khai** — không setup ngay trong giai đoạn viết spec hiện tại (đúng nguyên tắc ở `CLAUDE.md`: chưa sang System Design, chưa code).
- `TEST-003` (ĐÃ CHỐT v0.2.0): Vai trò **Approve** trong quy trình ở mục 1 do **chủ dự án (bạn) đảm nhiệm duy nhất** — không có người thứ 2 trong nhóm. Kết hợp với `TEST-002` (Dev/Test do Agent thực hiện, Approve do người thật), nguyên tắc "tránh dev tự test tự duyệt" (lo ngại ban đầu của câu hỏi mở) vẫn được đảm bảo tự nhiên — người/agent làm Dev+Test không phải người ra quyết định Approve cuối cùng.

## 2. Phân loại test theo tầng

| Tầng | Mục đích | Công cụ đề xuất |
|---|---|---|
| Unit test | Test logic đơn lẻ (hash password, tính risk score threshold, parse IPC message) | xUnit/NUnit (.NET) |
| Integration test | Test tương tác giữa module (Service ↔ Vision qua Named Pipe thật) | Test harness riêng, chạy trên máy ảo Windows sạch |
| Security test | Test các kịch bản tấn công đã liệt kê ở `05-anti-uninstall-tamper-spec.md` mục 2 | Kịch bản thủ công + script tự động hoá được phần nào |
| Performance test | Đo CPU/RAM theo benchmark ở `08-performance-cpu-spec.md` mục 7 | Windows Performance Counter, PerfView, hoặc dotnet-trace |
| Privacy compliance test | Quét không có dữ liệu ảnh sót lại (`IMG-040`) | Script quét thư mục temp + kiểm tra log content bằng regex/pattern tìm base64/binary bất thường |
| UX/Manual test | Trải nghiệm thực tế overlay, onboarding, password flow | Người kiểm thử thật, checklist thủ công |
| Long-run stability test | Chạy 72 giờ liên tục theo Definition of Done | Máy test riêng chạy nền dài hạn, giám sát log |

## 3. Checklist test mẫu theo từng nhóm feature (áp dụng khi đến giai đoạn code tương ứng)

### 3.1 Khi code xong Password Management
- [ ] Đặt mật khẩu lần đầu thành công, hash lưu đúng bằng Argon2id, không tìm thấy plaintext ở đâu trong RAM dump/log.
- [ ] Rate-limiting hoạt động đúng bảng delay ở `PWD-021`.
- [ ] Luồng Recovery Key: đúng key → reset thành công; sai key → rate-limit áp dụng; key cũ bị vô hiệu sau khi dùng 1 lần.
- [ ] Không log giá trị mật khẩu/Recovery Key ở bất kỳ log level nào (kiểm tra bằng cách grep toàn bộ log output sau khi chạy test).

### 3.2 Khi code xong Anti-Uninstall/Tamper
- [ ] Kill process Service qua Task Manager → Watchdog phát hiện và restart trong thời gian quy định.
- [ ] Kill cả 2 service cùng lúc → ít nhất 1 trong 2 tự phục hồi qua Recovery Options của SCM.
- [ ] Thử gỡ qua Settings/Control Panel → bị chặn/yêu cầu mật khẩu đúng như thiết kế.
- [ ] Thử xoá file trong Program Files bằng user thường → bị từ chối bởi ACL.
- [ ] Thử sửa registry Start value bằng user thường → bị từ chối bởi ACL.

### 3.3 Khi code xong Image Processing Pipeline
- [ ] Chạy app với nội dung test 30 phút, sau đó quét toàn bộ %TEMP%, thư mục cài đặt, clipboard — xác nhận không có dữ liệu ảnh nào.
- [ ] Kiểm tra log sinh ra trong quá trình test không chứa binary/base64 của ảnh.
- [ ] Đo thời gian buffer RAM tồn tại từ lúc capture đến lúc zero-out (nên gần tức thời, không có độ trễ bất thường).

### 3.4 Khi code xong Overlay/Force-close
- [ ] Overlay hiện đúng vị trí/kích thước cửa sổ browser test, theo dõi đúng khi resize/di chuyển cửa sổ.
- [ ] Bấm nút "Tắt nội dung" → browser bị đóng đúng như thiết kế, sự kiện được ghi log.
- [ ] Overlay không thể bị click-through hoặc tắt bằng phím tắt hệ thống thông thường (Alt+F4 trên overlay window cần được xử lý có chủ đích — quyết định xem có cho phép hay chặn).

### 3.5 Khi code xong Performance Optimization
- [ ] Đo CPU trung bình theo kịch bản sử dụng thực tế (duyệt web bình thường 1 giờ) — đối chiếu ngưỡng ở `08-performance-cpu-spec.md`.
- [ ] Xác nhận adaptive frame rate hoạt động đúng: giảm tần suất khi browser không active, tăng khi có nội dung thay đổi.
- [ ] Test trên ít nhất 2 cấu hình máy khác nhau (1 máy cấu hình thấp, 1 máy cấu hình trung bình/cao) để xác nhận ngưỡng hiệu năng hợp lý cho cả 2.

### 3.6 Khi code xong Pause/Resume
- [ ] Pause 15 phút → tự động resume đúng thời điểm, không cần thao tác thêm.
- [ ] Tắt máy giữa lúc đang pause, khởi động lại → trạng thái pause được khôi phục đúng logic (`PAUSE-030`).
- [ ] Banner nhắc nhở xuất hiện đúng tần suất đã thiết kế.

## 4. Tiêu chí Go/No-Go trước khi release phiên bản đầu tiên ra ngoài nhóm dev nội bộ

- Toàn bộ requirement `APPROVED` liên quan Phase 1 đạt trạng thái `VERIFIED`.
- Không còn lỗi mức Critical/High mở (liên quan crash, rò rỉ dữ liệu, bypass bảo mật).
- Đã chạy ít nhất 1 lần Long-run stability test 72 giờ không phát sinh sự cố nghiêm trọng.
- Đã có bản build ký số (code signing) để test luôn cả kịch bản SmartScreen/Defender.

## 5. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 6. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.0 | 2026-09-17 | **Chốt cả 2 câu hỏi mở**: `TEST-002` — tự động hoá quy trình Dev/Test/Debug/Report bằng Claude Code Agent thay vì CI script truyền thống, chi tiết cấu hình để ở System Design. `TEST-003` — chủ dự án là người Approve duy nhất, không có người thứ 2. File này không còn câu hỏi mở |
| v0.1.0 | 2026-09-17 | Khởi tạo |

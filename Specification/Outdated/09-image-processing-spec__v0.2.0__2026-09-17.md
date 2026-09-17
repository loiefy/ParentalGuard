# 09 — Image Processing Pipeline Spec

> Version: v0.2.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Nguyên tắc tuyệt đối

- `IMG-001`: Không có bước nào trong pipeline này ghi dữ liệu ảnh xuống **disk** (không file tạm, không swap file cố ý, không temp folder) dưới bất kỳ hình thức nào.
- `IMG-002`: Không có bước nào copy ảnh vào **clipboard** hệ thống.
- `IMG-003`: Buffer RAM chứa dữ liệu ảnh phải được **ghi đè bằng 0 (zero-out)** ngay sau khi không còn cần dùng nữa trong cùng chu kỳ xử lý, không chờ Garbage Collector tự dọn (vì GC không đảm bảo zero-out ngay, dữ liệu cũ có thể còn tồn tại trong vùng nhớ đã "free" cho đến khi bị ghi đè bởi dữ liệu khác).

## 2. Pipeline chi tiết (từng bước)

```
[Bước 1] Capture (Desktop Duplication API / DXGI)
   → Output: raw frame trong GPU texture / RAM buffer
   → Chỉ capture vùng màn hình chứa cửa sổ browser đang active (PERF-021)

[Bước 2] Downscale / Resize
   → Resize về kích thước input của model (ví dụ 224×224 hoặc kích thước model yêu cầu)
   → Mục đích kép: giảm tải inference (PERF) VÀ giảm lượng dữ liệu nhạy cảm cần giữ trong RAM

[Bước 3] Chuẩn hoá màu (Normalization)
   → Convert sang định dạng tensor model yêu cầu (thường float32, normalize theo mean/std của model)

[Bước 4] Inference (ONNX Runtime)
   → Input: tensor đã chuẩn hoá
   → Output: risk score (0.0 - 1.0) + có thể kèm bounding box nếu model hỗ trợ object detection

[Bước 5] Zero-out buffer
   → Ghi đè toàn bộ buffer RAM đã dùng ở bước 1-4 bằng 0 ngay lập tức
   → Giải phóng (dispose/free) sau khi zero-out, không giải phóng trước

[Bước 6] Ra quyết định
   → So risk score với ngưỡng cấu hình
   → Chỉ gửi qua IPC: risk score (số) + toạ độ cửa sổ (không phải ảnh) tới Service/Overlay
```

## 3. Kỹ thuật nén/giảm dữ liệu để tối ưu (liên kết `08-performance-cpu-spec.md`)

- `IMG-010`: Downscale (bước 2) đóng vai trò như một dạng "nén" thực dụng — giảm cả kích thước dữ liệu cần giữ trong RAM lẫn khối lượng tính toán, đây là ưu tiên chính chứ không phải nén ảnh theo định dạng file (JPEG/PNG không cần thiết vì **không bao giờ ghi ảnh ra file**).
- `IMG-011`: Có thể áp dụng thêm **perceptual hashing** (đã đề cập ở `PERF-011`) để quyết định có cần chạy qua bước 3-4 hay không, nếu frame gần như giống hệt frame trước — hash chỉ là 1 chuỗi số ngắn, không phải dữ liệu ảnh nhạy cảm, an toàn để giữ trong RAM lâu hơn 1 chút để so sánh giữa các frame.

## 4. Xử lý trường hợp đa cửa sổ / đa nội dung

- `IMG-020`: Nếu có nhiều cửa sổ cùng foreground khả dĩ (hiếm với Windows, nhưng có thể xảy ra với cấu hình đa màn hình) → xử lý tuần tự theo thứ tự ưu tiên cửa sổ đang có focus thực sự, không xử lý song song không cần thiết (tránh tăng đột biến CPU). Áp dụng cho mọi loại cửa sổ theo phạm vi mở rộng ở `BE-071` (browser, video player, hoặc ứng dụng bất kỳ), không riêng browser.

## 5. Xử lý ảnh động (video) — giới hạn Phase 1

- `IMG-030`: Phase 1 chỉ phân tích **frame tĩnh theo chu kỳ** (không phải phân tích video liên tục theo temporal context) — nghĩa là video có thể có độ trễ phát hiện bằng đúng chu kỳ capture hiện tại (`PERF-010`), không đảm bảo bắt được ngay khung hình đầu tiên của video vi phạm.
- `IMG-031` (đề xuất Phase 2, ghi nhận để không quên): Phân tích temporal (nhiều frame liên tiếp) để tăng độ chính xác và giảm false positive/negative cho nội dung video — cần model và pipeline phức tạp hơn, không đưa vào Phase 1.

## 6. Kiểm tra tuân thủ nguyên tắc "không lưu trữ" (Compliance check)

- `IMG-040`: Phải có cơ chế test tự động (xem `11-testing-qa-process.md`) kiểm tra: sau khi app chạy X phút với nội dung mô phỏng, quét toàn bộ:
  - Thư mục temp của user và system.
  - File log thực tế sinh ra (đảm bảo không chứa binary ảnh hoặc base64 của ảnh).
  - Bộ nhớ heap dump (nếu công cụ cho phép) để xác nhận không có buffer ảnh còn sót chưa zero-out bất thường lâu.
- `IMG-041`: Đây là một trong các tiêu chí **bắt buộc pass** trước khi bất kỳ tính năng nào liên quan xử lý ảnh được coi là "hoàn thành" trong quy trình dev từng giai đoạn.

## 7. Câu hỏi mở

- [ ] Có cần giữ lại **1 thumbnail rất nhỏ, mờ, đã qua xử lý** (không phải ảnh gốc) trong audit log để phụ huynh có ngữ cảnh tối thiểu khi xem lịch sử không? → Nếu có, đây là ngoại lệ lớn với nguyên tắc `IMG-001`/`GEN-031`, cần bàn kỹ về rủi ro/lợi ích trước khi quyết định, không nên tự ý thêm vào code nếu chưa được duyệt rõ ràng ở bước spec.
- [ ] Ngưỡng risk score mặc định (liên kết câu hỏi mở ở `02-backend-spec.md`) nên dựa trên benchmark nào — cần chuẩn bị bộ ảnh test (dataset) để đo precision/recall trước khi chốt số.
- [ ] (Mới, v0.2.0) Bộ dataset test cần bổ sung thêm mẫu trích từ ngữ cảnh video player (khung hình video, có thể khác đặc tính ảnh so với ảnh chụp trang web — nén khác, tỉ lệ khung hình khác) để đảm bảo model hoạt động tốt trên nguồn nội dung mới, không chỉ tối ưu cho ảnh web như benchmark ban đầu.

## 8. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.0 | 2026-09-17 | Tổng quát hoá `IMG-020` từ "browser" sang mọi loại cửa sổ theo phạm vi mới; thêm câu hỏi mở về dataset test cho nguồn video player |
| v0.1.0 | 2026-09-17 | Khởi tạo |

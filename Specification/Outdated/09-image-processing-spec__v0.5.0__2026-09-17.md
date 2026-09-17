# 09 — Image Processing Pipeline Spec

> Version: v0.5.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Nguyên tắc tuyệt đối

- `IMG-001`: Không có bước nào trong pipeline này ghi dữ liệu ảnh xuống **disk** (không file tạm, không swap file cố ý, không temp folder) dưới bất kỳ hình thức nào.
- `IMG-001a` (ĐÃ CHỐT v0.4.0): **Không** có ngoại lệ cho `IMG-001`/`GEN-031` — không giữ lại thumbnail (kể cả đã mờ hoá/xử lý) trong audit log dưới bất kỳ hình thức nào. Audit log chỉ chứa metadata (timestamp, risk score, toạ độ cửa sổ, tên process) như đã mô tả ở Bước 7, không có ngoại lệ hình ảnh nào phục vụ mục đích "ngữ cảnh cho phụ huynh".
- `IMG-002`: Không có bước nào copy ảnh vào **clipboard** hệ thống.
- `IMG-003`: Buffer RAM chứa dữ liệu ảnh phải được **ghi đè bằng 0 (zero-out)** ngay sau khi không còn cần dùng nữa trong cùng chu kỳ xử lý, không chờ Garbage Collector tự dọn (vì GC không đảm bảo zero-out ngay, dữ liệu cũ có thể còn tồn tại trong vùng nhớ đã "free" cho đến khi bị ghi đè bởi dữ liệu khác).

## 2. Pipeline chi tiết (từng bước)

```
[Bước 1] Capture (Desktop Duplication API / DXGI)
   → Output: raw frame trong GPU texture / RAM buffer
   → Chỉ capture vùng màn hình (nguyên monitor) chứa cửa sổ đang active (PERF-021) — đây là chọn màn hình nào, chưa phải crop theo cửa sổ

[Bước 2] Crop GPU-side về đúng bounding rect cửa sổ (IMG-012)
   → Cắt (CopySubresourceRegion, vẫn trên GPU, chưa readback CPU) đúng vùng cửa sổ đang giám sát ra khỏi frame nguyên màn hình ở Bước 1
   → Toạ độ lấy từ DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS), không dùng GetWindowRect thô để tránh lệch do shadow/rounded corner do DWM vẽ thêm

[Bước 3] Downscale / Resize
   → Resize **vùng đã crop** (không phải nguyên màn hình) về kích thước input của model (ví dụ 224×224 hoặc kích thước model yêu cầu)
   → Mục đích kép: giảm tải inference (PERF) VÀ giảm lượng dữ liệu nhạy cảm cần giữ trong RAM

[Bước 4] Chuẩn hoá màu (Normalization)
   → Convert sang định dạng tensor model yêu cầu (thường float32, normalize theo mean/std của model)

[Bước 5] Inference (ONNX Runtime)
   → Input: tensor đã chuẩn hoá
   → Output: risk score (0.0 - 1.0, chiều tăng dần = càng chắc chắn vi phạm — xem IMG-013) + có thể kèm bounding box nếu model hỗ trợ object detection

[Bước 6] Zero-out buffer
   → Ghi đè toàn bộ buffer RAM/GPU đã dùng ở bước 1-5 bằng 0 ngay lập tức
   → Giải phóng (dispose/free) sau khi zero-out, không giải phóng trước

[Bước 7] Ra quyết định
   → So risk score với ngưỡng cấu hình
   → Chỉ gửi qua IPC: risk score (số) + toạ độ cửa sổ (không phải ảnh) tới Service/Overlay
```

## 3. Model AI: lựa chọn cụ thể (ĐÃ CHỐT v0.5.0)

- `IMG-014`: Model AI dùng cho bước Inference (Bước 5) dựa trên **trọng số đã train sẵn của [`GantMan/nsfw_model`](https://github.com/GantMan/nsfw_model)** — kiến trúc **MobileNetV2**, license **MIT**, phân loại 5 lớp: `drawing` / `hentai` / `neutral` / `porn` / `sexy` (risk score tổng hợp từ các lớp nhạy cảm, chi tiết công thức để ở System Design).
  - Chỉ lấy **trọng số model**, convert sang định dạng `.onnx` (qua `tf2onnx` hoặc tương đương) — **không đóng gói/chạy code Python gốc** (`nsfw_detector` package) dưới bất kỳ hình thức nào. Toàn bộ app chạy trên C#/.NET 8 + ONNX Runtime (`GEN-003`), sản phẩm cuối **không có runtime Python** nào.
  - Yêu cầu duy nhất của MIT license: giữ lại file LICENSE gốc của `GantMan/nsfw_model` trong tài liệu bên thứ ba đi kèm bản phân phối (ví dụ `THIRD-PARTY-LICENSES.txt`).
  - Bắt buộc validate lại (precision/recall) trên bộ dataset benchmark riêng của dự án trước khi khoá cứng ngưỡng risk score mặc định (liên kết câu hỏi mở còn lại ở mục 7, `BE-090`) — model gốc không được train riêng cho bối cảnh Việt Nam/ngữ cảnh sử dụng thực tế của app.
- `IMG-015` (ĐÃ CHỐT v0.5.0): **An toàn network của model được đảm bảo bởi 2 lớp độc lập**, không phụ thuộc vào việc "tin tưởng" thư viện/model đã chọn:
  1. File `.onnx` sau khi convert là **dữ liệu tĩnh** (trọng số số học) — không chứa code thực thi, về bản chất không có khả năng tự thực hiện network call dưới bất kỳ hình thức nào.
  2. `Vision` process (nơi load model + chạy ONNX Runtime) đã bị **chặn network tuyệt đối ở tầng OS** (Windows Filtering Platform outbound rule) theo `SEC-016`/`SEC-017`/`SEC-018` ở `04-security-spec.md` — lớp phòng thủ độc lập này chặn được kể cả nếu có thành phần nào đó (ONNX Runtime, dependency ẩn...) cố gắng kết nối mạng ngoài dự kiến, không phụ thuộc vào việc model/thư viện có "sạch" hay không.
  - Bổ sung 1 bước hardening cụ thể: tắt tường minh tính năng **telemetry tuỳ chọn của ONNX Runtime** (API `DisableTelemetryEvents`/tương đương trong `SessionOptions`) khi khởi tạo session — dù ONNX Runtime mặc định không cần network cho inference, đây là lớp phòng thủ bổ sung đúng tinh thần "nhiều lớp" đã có ở `SEC-018`.

## 4. Kỹ thuật nén/giảm dữ liệu để tối ưu (liên kết `08-performance-cpu-spec.md`)

- `IMG-010`: Downscale (bước 2) đóng vai trò như một dạng "nén" thực dụng — giảm cả kích thước dữ liệu cần giữ trong RAM lẫn khối lượng tính toán, đây là ưu tiên chính chứ không phải nén ảnh theo định dạng file (JPEG/PNG không cần thiết vì **không bao giờ ghi ảnh ra file**).
- `IMG-011`: Có thể áp dụng thêm **perceptual hashing** (đã đề cập ở `PERF-011`) để quyết định có cần chạy qua bước 3-4 hay không, nếu frame gần như giống hệt frame trước — hash chỉ là 1 chuỗi số ngắn, không phải dữ liệu ảnh nhạy cảm, an toàn để giữ trong RAM lâu hơn 1 chút để so sánh giữa các frame.
- `IMG-012` (ĐÃ CHỐT v0.3.0): **Crop GPU-side về đúng bounding rect cửa sổ đang giám sát trước khi resize** (Bước 2 mới ở pipeline mục 2, chèn giữa Capture và Downscale cũ). Đây là tối ưu **khác cấp** với `PERF-021` (chọn màn hình nào để capture) — `IMG-012` xử lý bước tiếp theo: trong màn hình đã chọn, chỉ giữ lại đúng vùng cửa sổ, không giữ nguyên cả màn hình. Lợi ích kép:
  1. **Hiệu năng**: resize (bước 3) nhận input nhỏ hơn hẳn (kích thước cửa sổ thay vì nguyên màn hình) → rẻ hơn cả về băng thông đọc lẫn compute; bản thân bước crop gần như miễn phí (GPU op `CopySubresourceRegion`, không cần readback CPU trước).
  2. **Độ chính xác phát hiện**: nếu không crop, 1 cửa sổ nhỏ trên màn hình lớn/4K sẽ bị resize thẳng từ nguyên màn hình xuống kích thước model (vd. 224×224), khiến nội dung thực sự của cửa sổ chỉ còn chiếm 1 phần rất nhỏ trong ảnh cuối cùng đưa vào model — giảm khả năng nhận diện nội dung vi phạm. Crop trước giúp dành toàn bộ "ngân sách độ phân giải" của model cho đúng nội dung cần phân tích.
  - Toạ độ crop lấy từ `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, không dùng `GetWindowRect` thô, để tránh lệch do vùng shadow/rounded corner mà DWM vẽ thêm quanh cửa sổ.
  - Trường hợp cửa sổ nằm vắt qua nhiều màn hình (crop/ghép từ nhiều output texture khác nhau) để lại chi tiết kỹ thuật cụ thể cho System Design, không quyết định trong spec này.
- `IMG-013` (ĐÃ CHỐT v0.4.0 — làm rõ định nghĩa, không đổi hành vi): Risk score là 1 giá trị float `[0.0, 1.0]`, thể hiện **độ tin cậy của model rằng nội dung đang phân tích là nội dung vi phạm** — **càng cao (gần 1.0) càng chắc chắn là vi phạm**, càng thấp (gần 0.0) càng chắc chắn là nội dung an toàn/bình thường. Overlay chỉ kích hoạt khi risk score **≥ (vượt)** ngưỡng cấu hình (`BE-090`) — đúng chiều đã dùng xuyên suốt data flow ở `02-backend-spec.md` ("nếu vượt ngưỡng"). Đây chỉ là làm rõ tường minh 1 quy ước vốn đã ngầm hiểu trong spec trước đó, không phải thay đổi hành vi hệ thống.

## 5. Xử lý trường hợp đa cửa sổ / đa nội dung

- `IMG-020`: Nếu có nhiều cửa sổ cùng foreground khả dĩ (hiếm với Windows, nhưng có thể xảy ra với cấu hình đa màn hình) → xử lý tuần tự theo thứ tự ưu tiên cửa sổ đang có focus thực sự, không xử lý song song không cần thiết (tránh tăng đột biến CPU). Áp dụng cho mọi loại cửa sổ theo phạm vi mở rộng ở `BE-071` (browser, video player, hoặc ứng dụng bất kỳ), không riêng browser.

## 6. Xử lý ảnh động (video) — giới hạn Phase 1

- `IMG-030`: Phase 1 chỉ phân tích **frame tĩnh theo chu kỳ** (không phải phân tích video liên tục theo temporal context) — nghĩa là video có thể có độ trễ phát hiện bằng đúng chu kỳ capture hiện tại (`PERF-010`), không đảm bảo bắt được ngay khung hình đầu tiên của video vi phạm.
- `IMG-031` (đề xuất Phase 2, ghi nhận để không quên): Phân tích temporal (nhiều frame liên tiếp) để tăng độ chính xác và giảm false positive/negative cho nội dung video — cần model và pipeline phức tạp hơn, không đưa vào Phase 1.
- **ĐÃ CHỐT quy trình (v0.4.0)**: Việc bổ sung mẫu dataset test riêng cho ngữ cảnh video player (đặc tính nén/tỉ lệ khung hình khác ảnh web) được **hoãn sang giai đoạn triển khai hệ thống** (System Design/implementation), không quyết định phương pháp cụ thể ở bước spec này.

## 7. Kiểm tra tuân thủ nguyên tắc "không lưu trữ" (Compliance check)

- `IMG-040`: Phải có cơ chế test tự động (xem `11-testing-qa-process.md`) kiểm tra: sau khi app chạy X phút với nội dung mô phỏng, quét toàn bộ:
  - Thư mục temp của user và system.
  - File log thực tế sinh ra (đảm bảo không chứa binary ảnh hoặc base64 của ảnh).
  - Bộ nhớ heap dump (nếu công cụ cho phép) để xác nhận không có buffer ảnh còn sót chưa zero-out bất thường lâu.
- `IMG-041`: Đây là một trong các tiêu chí **bắt buộc pass** trước khi bất kỳ tính năng nào liên quan xử lý ảnh được coi là "hoàn thành" trong quy trình dev từng giai đoạn.

## 8. Câu hỏi mở

- [ ] Ngưỡng risk score mặc định (liên kết câu hỏi mở ở `02-backend-spec.md`) nên dựa trên benchmark nào — cần chuẩn bị bộ ảnh test (dataset) để đo precision/recall trước khi chốt số. (Chiều risk score đã làm rõ ở `IMG-013`: càng cao càng chắc chắn vi phạm — câu hỏi còn lại là **phương pháp/dataset benchmark cụ thể** để chọn ra con số ngưỡng, vẫn chưa có quyết định.)

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.5.0 | 2026-09-17 | **Chốt model AI cụ thể**: `IMG-014` — dùng trọng số `GantMan/nsfw_model` (MobileNetV2, MIT), convert sang ONNX, không đóng gói runtime Python. `IMG-015` — an toàn network đảm bảo bởi 2 lớp độc lập (model là dữ liệu tĩnh + chặn network tầng OS đã có ở `SEC-016`-`018`), thêm hardening tắt telemetry ONNX Runtime. Section 3 mới, renumber các section 3-8 cũ thành 4-9 |
| v0.4.0 | 2026-09-17 | **Chốt 2/3 câu hỏi mở**: `IMG-001a` — không có ngoại lệ thumbnail trong audit log, `IMG-001`/`GEN-031` giữ nguyên tuyệt đối. `IMG-013` — làm rõ tường minh chiều risk score (càng cao càng chắc chắn vi phạm), không đổi hành vi. Hoãn quyết định dataset video sang giai đoạn triển khai hệ thống (ghi chú ở mục 5). Câu hỏi về phương pháp benchmark chọn ngưỡng risk score cụ thể vẫn còn mở |
| v0.3.0 | 2026-09-17 | Thêm `IMG-012` — crop GPU-side về đúng bounding rect cửa sổ trước khi resize (Bước 2 mới trong pipeline mục 2, renumber Bước 2-6 cũ thành 3-7), lợi ích kép về hiệu năng và độ chính xác phát hiện với cửa sổ nhỏ trên màn hình lớn |
| v0.2.0 | 2026-09-17 | Tổng quát hoá `IMG-020` từ "browser" sang mọi loại cửa sổ theo phạm vi mới; thêm câu hỏi mở về dataset test cho nguồn video player |
| v0.1.0 | 2026-09-17 | Khởi tạo |

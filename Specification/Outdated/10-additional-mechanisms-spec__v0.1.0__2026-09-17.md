# 10 — Additional Mechanisms Spec (tự nghiên cứu, đề xuất để review)

> Version: v0.1.0 | Trạng thái: Draft — TOÀN BỘ mục trong file này ở trạng thái `PROPOSED`, chưa `APPROVED`
> Cập nhật: 2026-09-17

Đây là các cơ chế chưa được yêu cầu trực tiếp nhưng cần thiết cho một sản phẩm hoàn chỉnh, dựa trên kinh nghiệm từ các sản phẩm parental control hiện có và các rủi ro đã phân tích xuyên suốt các buổi thảo luận trước. Đề xuất để bạn review và quyết định đưa vào Phase 1 hay để sau.

## 1. Audit Log tamper-evident (MISC-010)

- Đã đề cập ở `SEC-041`, chi tiết hoá ở đây: mỗi entry log có cấu trúc `{timestamp, event_type, detail_metadata, hash(entry_truoc + noi_dung_hien_tai)}`.
- Cho phép phụ huynh (và về sau, người audit độc lập nếu cần) verify tính toàn vẹn của toàn bộ chuỗi log mà không cần tin tưởng mù quáng vào app đang chạy.
- Loại sự kiện cần log tối thiểu: bật/tắt giám sát, pause/resume, chặn nội dung (kèm risk score, KHÔNG kèm ảnh), thử xác thực mật khẩu (thành công/thất bại), watchdog restart, thay đổi cấu hình, cập nhật phần mềm.

## 2. Cơ chế cập nhật phần mềm an toàn (MISC-020)

- App và model AI cần cơ chế update riêng biệt (model có thể update thường xuyên hơn app khi có phiên bản classifier tốt hơn, không cần release lại toàn bộ app).
- Yêu cầu: mọi package update phải ký số, verify chữ ký trước khi áp dụng, có khả năng rollback nếu bản mới gây lỗi.
- Cân nhắc: đây là **kênh network duy nhất** được phép tồn tại trong toàn hệ thống — cần tách biệt hoàn toàn khỏi module `Vision` (chỉ `Service` hoặc 1 module updater riêng được phép gọi network, và chỉ gọi đúng 1 endpoint kiểm tra version, không gửi bất kỳ dữ liệu người dùng nào kèm theo request).
- Đề xuất: cho phép tắt hẳn auto-update, chuyển sang cập nhật thủ công, cho người dùng ưu tiên tuyệt đối "zero network" lựa chọn.

## 3. Whitelist / báo cáo False Positive từ UI (MISC-030)

- Khi phụ huynh xem lại audit log, cho phép đánh dấu 1 sự kiện chặn là "sai" (false positive) → thêm domain/app đó vào whitelist cục bộ để không bị chặn lại.
- Không gửi feedback này ra ngoài (không có cơ chế "gửi để cải thiện model chung" trừ khi có quyết định riêng sau này về việc xây dựng cơ chế đóng góp dữ liệu ẩn danh, hoàn toàn tự nguyện — hiện tại ngoài scope).

## 4. Hỗ trợ đa hồ sơ / đa người dùng máy (MISC-040)

- Nếu 1 máy có nhiều tài khoản Windows (nhiều con dùng chung máy), cân nhắc cho phép cấu hình ngưỡng nhạy cảm khác nhau theo từng tài khoản Windows (ví dụ con lớn tuổi hơn có ngưỡng khác con nhỏ).
- Đề xuất để Phase 2 — Phase 1 áp dụng 1 cấu hình chung cho toàn máy để giảm độ phức tạp ban đầu.

## 5. Self-diagnostic / Health Check (MISC-050)

- Màn hình "Kiểm tra sức khoẻ hệ thống" trong Dashboard: xác nhận Service đang chạy, Watchdog đang chạy, model AI load thành công, GPU/DirectML khả dụng hay đang fallback CPU, dung lượng còn lại cho log.
- Giúp phụ huynh tự chẩn đoán vấn đề trước khi cần hỗ trợ kỹ thuật (đặc biệt quan trọng vì đây có thể là dự án cộng đồng, không có đội support lớn).

## 6. Chính sách khi phát hiện máy ảo / môi trường bất thường (MISC-060)

- Trẻ có thể cố chạy browser trong máy ảo (VM) hoặc sandbox riêng để né capture — về mặt kỹ thuật, nếu Vision Engine chạy trên máy host thật, Desktop Duplication API vẫn capture được nội dung hiển thị trong cửa sổ VM (vì đó vẫn là pixel hiển thị trên màn hình host) miễn là VM chạy ở chế độ windowed, không phải fullscreen exclusive trên 1 màn hình vật lý riêng.
- Ghi nhận giới hạn: nếu trẻ có kiến thức đủ sâu để chạy VM với GPU passthrough hiển thị qua màn hình vật lý thứ 2 hoàn toàn tách biệt khỏi capture của host — đây là giới hạn kỹ thuật cố hữu cần nêu rõ, không hứa hẹn quá mức trong tài liệu marketing/giới thiệu sản phẩm.

## 7. Minh bạch hoá qua "Software Behavior Disclosure" công khai (MISC-070)

- Liên kết `SEC-031`: xuất bản tài liệu công khai (trên trang dự án/README GitHub) mô tả chính xác app làm gì, không làm gì — vừa tăng uy tín với cộng đồng, vừa hỗ trợ quá trình xin whitelist từ các AV vendor, vừa là cam kết đạo đức với chính người dùng.

## 8. Cơ chế "Emergency Override" cho phụ huynh khi máy có sự cố khẩn (MISC-080, PROPOSED — cân nhắc kỹ)

- Kịch bản: máy con đang cần dùng gấp cho việc quan trọng (thi online, họp học trực tuyến) mà bảo vệ gây gián đoạn do false positive giữa chừng, phụ huynh không có mặt để nhập mật khẩu.
- Đề xuất khả dĩ: mã override dùng 1 lần, sinh sẵn cùng lúc với Recovery Key lúc Onboarding, đưa cho con trong tình huống đặc biệt (phụ huynh tự quyết định có làm việc này hay không) — **đây là tính năng có rủi ro bị lạm dụng cao nếu thiết kế không cẩn thận**, khuyến nghị để Phase 2 và bàn kỹ UX/threat model riêng trước khi implement, không vội đưa vào Phase 1.

## 9. Kiểm tra tính toàn vẹn model AI khi load (MISC-090)

- Verify checksum/chữ ký số của file `.onnx` mỗi lần Service khởi động Vision Engine — chống trường hợp file model bị thay thế bởi 1 file khác (dù không rõ động cơ tấn công cụ thể là gì trong ngữ cảnh này, đây là thực hành bảo mật chuẩn cho mọi file thực thi được app tin tưởng load).

## 10. Tổng hợp bảng ưu tiên đề xuất (để bạn quyết định Phase 1 vs Phase 2)

| Mục | Đề xuất mức ưu tiên | Lý do |
|---|---|---|
| MISC-010 Audit log tamper-evident | **Phase 1** | Nền tảng cho toàn bộ tính minh bạch/security khác |
| MISC-020 Update mechanism an toàn | **Phase 1** (cơ bản), nâng cao ở Phase 2 | Cần có từ đầu để vá lỗi/cập nhật model, nhưng không cần phức tạp ngay |
| MISC-030 Whitelist từ UI | **Phase 1** | Giảm friction false positive ngay từ đầu, chi phí implement thấp |
| MISC-040 Đa hồ sơ | Phase 2 | Tăng độ phức tạp đáng kể, không phải nhu cầu lõi ban đầu |
| MISC-050 Self-diagnostic | **Phase 1** (bản đơn giản) | Hỗ trợ vận hành cho dự án cộng đồng không có support team lớn |
| MISC-060 Nhận biết VM | Ghi nhận giới hạn, không cần implement chủ động | Chi phí/lợi ích không tương xứng ở Phase 1 |
| MISC-070 Behavior Disclosure | **Phase 1** | Chi phí thấp (chỉ viết tài liệu), lợi ích cao cho uy tín + AV whitelisting |
| MISC-080 Emergency Override | Phase 2, cân nhắc kỹ | Rủi ro lạm dụng cao, cần thiết kế threat model riêng |
| MISC-090 Verify model checksum | **Phase 1** | Chi phí implement thấp, best practice bảo mật cơ bản |

## 11. Câu hỏi mở

- [ ] Bạn có đồng ý với bảng ưu tiên ở mục 10 không, hay muốn điều chỉnh mục nào lên/xuống Phase?
- [ ] MISC-080 (Emergency Override) — có nên loại bỏ hẳn khỏi roadmap thay vì để Phase 2, nếu rủi ro bị lạm dụng được đánh giá là quá cao so với lợi ích?

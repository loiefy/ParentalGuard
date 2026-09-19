# 10 — Additional Mechanisms Spec (tự nghiên cứu, đề xuất để review)

> Version: v0.2.1 | Trạng thái: Approved — chủ dự án đã approve toàn bộ mục trong file này (bảng ưu tiên mục 10 và từng mục con), trừ `MISC-020`/`MISC-080` đã `REJECTED`
> Cập nhật: 2026-09-17

Đây là các cơ chế chưa được yêu cầu trực tiếp nhưng cần thiết cho một sản phẩm hoàn chỉnh, dựa trên kinh nghiệm từ các sản phẩm parental control hiện có và các rủi ro đã phân tích xuyên suốt các buổi thảo luận trước. Đề xuất để bạn review và quyết định đưa vào Phase 1 hay để sau.

## 1. Audit Log tamper-evident (MISC-010)

- Đã đề cập ở `SEC-041`, chi tiết hoá ở đây: mỗi entry log có cấu trúc `{timestamp, event_type, detail_metadata, hash(entry_truoc + noi_dung_hien_tai)}`.
- Cho phép phụ huynh (và về sau, người audit độc lập nếu cần) verify tính toàn vẹn của toàn bộ chuỗi log mà không cần tin tưởng mù quáng vào app đang chạy.
- Loại sự kiện cần log tối thiểu: bật/tắt giám sát, pause/resume, chặn nội dung (kèm risk score, KHÔNG kèm ảnh), thử xác thực mật khẩu (thành công/thất bại), watchdog restart, thay đổi cấu hình, cập nhật phần mềm.

## 2. Cơ chế cập nhật phần mềm (MISC-020, ĐÃ CHỐT v0.2.0 — loại bỏ auto-update)

- **REJECTED**: đề xuất auto-update/check-update qua network (bản đề xuất ban đầu ở trên) **bị loại bỏ hoàn toàn**. Quyết định của chủ dự án: ứng dụng **tuyệt đối không được truy cập internet** dưới bất kỳ hình thức nào, kể cả để check version hay tải bản cập nhật — không có ngoại lệ "kênh network duy nhất" nào được phép tồn tại (khác với đề xuất ban đầu ở trên).
- Cập nhật app/model AI chỉ thực hiện **thủ công**: phát hành bản cài đặt mới trên trang dự án/GitHub Releases, người dùng tự tải về và chạy lại installer (đã ký số theo `GEN-033`/`SEC-030`) để cài đè lên bản cũ — không có bất kỳ cơ chế nào trong app tự kiểm tra/tải/áp dụng bản cập nhật.
- Xem đồng bộ: `02-backend-spec.md` (bảng Network access, mục 1), `04-security-spec.md` (mục 8, `SEC-050` cũ bị loại bỏ), `01-tong-quan-va-pham-vi.md` (`GEN-034` mới), `05-anti-uninstall-tamper-spec.md` (A8).

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

## 8. Cơ chế "Emergency Override" cho phụ huynh khi máy có sự cố khẩn (MISC-080, ĐÃ CHỐT v0.2.0 — REJECTED, loại khỏi roadmap)

- Kịch bản: máy con đang cần dùng gấp cho việc quan trọng (thi online, họp học trực tuyến) mà bảo vệ gây gián đoạn do false positive giữa chừng, phụ huynh không có mặt để nhập mật khẩu.
- Đề xuất ban đầu (mã override dùng 1 lần, sinh cùng Recovery Key lúc Onboarding) **bị loại bỏ hoàn toàn khỏi roadmap**, không chỉ đẩy sang Phase 2. Lý do: nhu cầu thực sự đứng sau kịch bản này (cần đảm bảo giám sát không gây gián đoạn cho việc quan trọng đã biết trước) **đã được giải quyết bởi tính năng Tạm dừng giám sát hiện có** (`07-pause-resume-spec.md`) — phụ huynh chủ động pause trước khi có nhu cầu (ví dụ trước giờ thi online), không cần thêm 1 cơ chế "phá kính khẩn cấp" riêng vốn có rủi ro bị lạm dụng cao hơn nhiều so với lợi ích tăng thêm.

## 9. Kiểm tra tính toàn vẹn model AI khi load (MISC-090)

- Verify checksum/chữ ký số của file `.onnx` mỗi lần Service khởi động Vision Engine — chống trường hợp file model bị thay thế bởi 1 file khác (dù không rõ động cơ tấn công cụ thể là gì trong ngữ cảnh này, đây là thực hành bảo mật chuẩn cho mọi file thực thi được app tin tưởng load).

## 10. Tổng hợp bảng ưu tiên (ĐÃ CHỐT v0.2.0)

| Mục | Trạng thái | Lý do |
|---|---|---|
| MISC-010 Audit log tamper-evident | **Phase 1** | Nền tảng cho toàn bộ tính minh bạch/security khác |
| MISC-020 Update mechanism | **REJECTED** — không auto-update, zero internet tuyệt đối | Chủ dự án ưu tiên tuyệt đối "zero network" hơn tiện lợi tự động cập nhật; cập nhật chỉ thủ công qua cài lại installer |
| MISC-030 Whitelist từ UI | **Phase 1** | Giảm friction false positive ngay từ đầu, chi phí implement thấp |
| MISC-040 Đa hồ sơ | Phase 2 | Tăng độ phức tạp đáng kể, không phải nhu cầu lõi ban đầu |
| MISC-050 Self-diagnostic | **Phase 1** (bản đơn giản) | Hỗ trợ vận hành cho dự án cộng đồng không có support team lớn |
| MISC-060 Nhận biết VM | Ghi nhận giới hạn, không cần implement chủ động | Chi phí/lợi ích không tương xứng ở Phase 1 |
| MISC-070 Behavior Disclosure | **Phase 1** | Chi phí thấp (chỉ viết tài liệu), lợi ích cao cho uy tín + AV whitelisting |
| MISC-080 Emergency Override | **REJECTED** — loại khỏi roadmap | Nhu cầu thực sự đã được giải quyết bởi tính năng Tạm dừng (`07-pause-resume-spec.md`); rủi ro lạm dụng cao hơn lợi ích |
| MISC-090 Verify model checksum | **Phase 1** | Chi phí implement thấp, best practice bảo mật cơ bản |

## 11. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 12. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.1 | 2026-09-17 | Chủ dự án approve toàn bộ mục trong file này (không chỉ bảng ưu tiên mục 10, mà cả từng mục con) — chuyển trạng thái file từ `Draft` sang `Approved` |
| v0.2.0 | 2026-09-17 | **Chốt cả 2 câu hỏi mở**: duyệt bảng ưu tiên mục 10, ngoại trừ 2 điều chỉnh — `MISC-020` REJECTED (loại bỏ auto-update, app tuyệt đối zero internet, cập nhật chỉ thủ công); `MISC-080` REJECTED (loại hẳn khỏi roadmap, không chỉ đẩy Phase 2 — nhu cầu đã được `07-pause-resume-spec.md` giải quyết). Đồng bộ thay đổi sang `02-backend-spec.md`, `04-security-spec.md`, `01-tong-quan-va-pham-vi.md`, `05-anti-uninstall-tamper-spec.md`. File này không còn câu hỏi mở |
| v0.1.0 | 2026-09-17 | Khởi tạo |

# 07 — Pause / Resume Mechanism Spec

> Version: v0.6.0 | Trạng thái: Approved | Cập nhật: 2026-10-08

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
- `PAUSE-041` **(DEPRECATED v0.4.0 — thay bởi `PAUSE-044`–`PAUSE-046`)**: Khi chế độ đang bật, **tạm dừng giám sát** = nhập đúng mật khẩu (`PAUSE-001`) **và** vượt qua thử thách: **5 phép tính** (cộng/trừ/nhân, số có 1–2 chữ số) trong **60 giây**, phải đúng **5/5**. Sai hoặc hết giờ → làm lại với bộ câu hỏi mới; **thất bại 3 lần liên tiếp → khoá thử thách 5 phút**.
- `PAUSE-042` **(DEPRECATED v0.4.0 — thay bởi `PAUSE-047`/`PAUSE-048`)**: Câu hỏi do `Service` sinh ngẫu nhiên và `Service` tự chấm (giao diện không tự chấm), kết quả "đã vượt qua" chỉ có hiệu lực cho đúng 1 lần tạm dừng trong 2 phút. **Tắt** chế độ này cũng phải vượt qua thử thách (tránh lách bằng cách tắt rồi tạm dừng). Bật chế độ không cần thử thách.
- `PAUSE-043`: Chế độ này chỉ áp dụng cho **tạm dừng** và **tắt chế độ**; tiếp tục giám sát sớm (`PAUSE-004`) và gỡ cài đặt giữ nguyên như cũ. Cảnh báo tần suất tạm dừng (`PAUSE-021`) vẫn tính bình thường. Thẻ "Bảo vệ cả phụ huynh" ở màn hình giới thiệu lần đầu (`FE-032`) nay luôn được hiển thị vì tính năng đã có (thẻ chỉ giới thiệu tính năng, không phụ thuộc chế độ đang bật hay tắt). Vì mã xác thực tạm dừng chỉ có hiệu lực 15 giây, thử thách được làm **trước** bước nhập mật khẩu. *(Câu cuối DEPRECATED v0.4.0 — thứ tự mới ở `PAUSE-044`.)*

### 4b. Trò chơi nhảy vượt rào (mới v0.4.0, 2026-10-08 — chủ dự án yêu cầu trực tiếp, supersedes thử thách phép tính `PAUSE-041`/`PAUSE-042`)

- `PAUSE-044` **(ĐÃ CHỐT v0.4.0)**: Khi chế độ đang bật, **tạm dừng giám sát** = nhập đúng mật khẩu (`PAUSE-001`) **TRƯỚC**, sau đó **1 cửa sổ trò chơi nhảy vượt rào** hiện lên. **Về đích** → giám sát được tạm dừng theo thời lượng đã chọn. **Vấp rào** hoặc **thoát trò chơi** (đóng cửa sổ) → **không** tạm dừng; muốn thử lại phải bấm Tạm dừng và nhập mật khẩu lại từ đầu.
- `PAUSE-045` **(DEPRECATED v0.5.0 phần "tốc độ cố định" và thời gian ước tính — thay bởi `PAUSE-045a`; phần chọn độ khó 1.000/2.000/3.000 m giữ nguyên) (ĐÃ CHỐT v0.4.0)**: Độ khó do phụ huynh chọn ở `S4` = **quãng đường phải chạy**: **Dễ 1.000 m / Vừa 2.000 m / Khó 3.000 m** (mặc định Dễ); quãng đường dài hơn thì rào cũng dày hơn. Nhân vật chạy với **tốc độ cố định cho mọi độ khó**, đảm bảo chạy **1.000 m mất ít nhất 3 phút** (≈ 3 phút 2 giây; 2.000 m ≈ 6 phút; 3.000 m ≈ 9 phút).
- `PAUSE-046` **(ĐÃ CHỐT v0.4.0, bổ sung bởi `PAUSE-046a`)**: Cách chơi: nhân vật tự chạy trên đường có nhiều rào; nhấn **phím Space** hoặc **chuột trái** để nhảy qua rào (chỉ nhảy khi đang chạm đất). Màn hình hiển thị quãng đường đã chạy / mục tiêu, thanh tiến độ và thời gian.
- `PAUSE-045a` **(DEPRECATED v0.6.0 — thay bởi `PAUSE-045b`) (ĐÃ CHỐT v0.5.0, 2026-10-08 — chủ dự án yêu cầu trực tiếp, supersedes phần tốc độ của `PAUSE-045`)**: Chạy càng lâu càng nhanh: **pace 10 phút/km lúc xuất phát, giảm dần đều tới 4 phút/km ở mét thứ 500**, sau đó giữ 4 phút/km — như nhau ở mọi độ khó. Thời gian chạy: 500 m đầu 3 phút 30 giây; **1.000 m ≈ 5 phút 30 giây** (vẫn ≥ 3 phút); 2.000 m ≈ 9 phút 30 giây; 3.000 m ≈ 13 phút 30 giây. Màn hình hiện pace hiện tại.
- `PAUSE-046a` **(ĐÃ CHỐT v0.5.0, bổ sung/điều chỉnh bởi `PAUSE-046b`) (2026-10-08 — chủ dự án yêu cầu trực tiếp)**: Chạy càng nhanh thì **ở trên không càng lâu và nhảy càng xa**. Mật độ rào **cao hơn trước** và **tăng dần theo quãng đường** (độ khó cao thì dày hơn); cho phép **2 rào đặt gần nhau** khi 1 cú nhảy ở tốc độ lúc đó vượt được cả 2. Mọi bố trí rào phải **khả thi**: luôn có khoảng thời điểm nhảy hợp lệ và đủ thời gian phản xạ sau khi tiếp đất.
- `PAUSE-045b` **(ĐÃ CHỐT v0.6.0, 2026-10-08 — chủ dự án yêu cầu trực tiếp, supersedes `PAUSE-045a`)**: Pace xuất phát **6 phút/km**, giảm dần đều tới **2 phút/km** (nhanh nhất) ở mét thứ 500, sau đó giữ 2 phút/km — như nhau ở mọi độ khó. Thời gian chạy: **1.000 m = 3 phút** (đúng mức tối thiểu `PAUSE-045`), 2.000 m = 5 phút, 3.000 m = 7 phút.
- `PAUSE-046b` **(ĐÃ CHỐT v0.6.0, 2026-10-08 — chủ dự án yêu cầu trực tiếp)**: (1) Ngoài rào còn có **hố**, xuất hiện ngẫu nhiên xen kẽ với rào; chạm đất trong lòng hố = **thua**. (2) Mật độ chướng ngại **tăng gấp ~2 lần** (đo theo số chướng ngại gặp mỗi giây); cụm tới 5 rào sát nhau khi 1 cú nhảy ở tốc độ đó vượt được cả cụm. (3) **Độ cao cú nhảy hạ thấp** (đỉnh 1,4–1,6 m thay 1,8–2,0 m), độ dài cú nhảy giữ nguyên theo tốc độ. Mọi bố trí vẫn phải khả thi (`PAUSE-046a`).
- `PAUSE-049` **(ĐÃ CHỐT v0.6.0, 2026-10-08 — chủ dự án yêu cầu trực tiếp)**: Mỗi lần **thua** (vấp rào / rơi hố) hiện thông báo **"Bạn đã thua, nhưng bạn vẫn đang được bảo vệ!"** (kèm vị trí thua). Khi thua **liên tiếp 5–10 lần** (ngưỡng ngẫu nhiên trong khoảng này), thông báo đổi thành **"Hay là không tạm dừng nữa?"** kèm nút **"Donate us"** (đóng trò chơi, mở tab Ủng hộ dự án) và nút **"Để sau"**; sau đó đếm lại với ngưỡng ngẫu nhiên mới. Về đích thì đếm lại từ đầu. Thoát trò chơi (đóng cửa sổ) không tính là thua cho bộ đếm này. Bộ đếm chỉ sống trong phiên Dashboard.
- `PAUSE-047` **(ĐÃ CHỐT v0.4.0)**: **Tắt** chế độ hoặc **giảm** độ khó khi chế độ đang bật cũng phải về đích trò chơi ở **độ khó hiện hành** (tránh lách bằng cách tắt/giảm rồi tạm dừng). Bật chế độ hoặc tăng độ khó chỉ cần đăng nhập phụ huynh. Không còn khoá sau nhiều lần thua (mỗi lượt chơi đã tốn vài phút).
- `PAUSE-048` **(ĐÃ CHỐT v0.4.0)**: `Service` ghi nhận ván chơi và quyết định kết quả: mật khẩu (mã xác thực) được tiêu thụ ngay khi bắt đầu ván; mỗi ván chỉ kết thúc được 1 lần; `Service` **từ chối** kết quả về đích sớm hơn thời gian tối thiểu để chạy hết quãng đường ở tốc độ cố định. Tạm dừng do về đích có mọi hệ quả như tạm dừng thường (nhật ký, cảnh báo tần suất `PAUSE-021`). Nhật ký ghi bắt đầu/thắng/thua của từng ván.

## 5. Ràng buộc kỹ thuật

- `PAUSE-030`: Trạng thái pause được lưu trong `config.db` (mã hoá DPAPI như đã mô tả ở `BE-060`/`SEC-040`) kèm timestamp hết hạn — để nếu máy tắt/khởi động lại giữa lúc đang pause, Service khi khởi động đọc lại trạng thái và tự tính toán còn pause hay đã hết hạn (không tự ý resume ngay khi khởi động lại nếu vẫn còn trong thời gian đã chọn, và không tự ý pause thêm nếu đã hết hạn).
- `PAUSE-031`: Khi đang pause, `Vision` process có thể được tạm dừng thực sự (suspend) thay vì tắt hẳn, để tiết kiệm CPU nhưng vẫn giữ heartbeat với Service ở tần suất thấp hơn — tránh watchdog hiểu nhầm là bị tấn công (liên kết `ANTI-060`).

## 6. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 7. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.6.0 | 2026-10-08 | **MINOR (chủ dự án yêu cầu trực tiếp)**: `PAUSE-045b` pace 6 → 2 phút/km (supersedes `PAUSE-045a`); `PAUSE-046b` thêm hố, mật độ ×2, nhảy thấp hơn; `PAUSE-049` thông báo khi thua + gợi ý Donate sau 5–10 lần thua liên tiếp (nút "Donate us" / "Để sau"). Archive: `Outdated/07-pause-resume-spec__v0.5.0__2026-10-08.md` |
| v0.5.0 | 2026-10-08 | **MINOR (chủ dự án yêu cầu trực tiếp)**: `PAUSE-045a` tăng tốc dần (pace 10 → 4 phút/km trong 500 m đầu) supersedes tốc độ cố định của `PAUSE-045`; `PAUSE-046a` nhảy xa/lâu hơn khi chạy nhanh, rào dày hơn và dày dần, cho phép cặp rào gần nhau nếu nhảy qua được. Chi tiết do đội phát triển chọn: bay 0,9 → 1,2 giây, cửa sổ nhảy ≥ 0,22 giây, phản xạ sau tiếp đất ≥ 0,4 giây. Archive: `Outdated/07-pause-resume-spec__v0.4.0__2026-10-08.md` |
| v0.4.0 | 2026-10-08 | **MINOR (chủ dự án yêu cầu trực tiếp)**: mục 4b — trò chơi nhảy vượt rào thay thử thách phép tính: `PAUSE-044`–`PAUSE-048` mới; `PAUSE-041`/`PAUSE-042` và câu cuối `PAUSE-043` DEPRECATED. Chi tiết do đội phát triển chọn (ghi rõ để chủ dự án điều chỉnh): độ khó = quãng đường + mật độ rào, tốc độ cố định 5,5 m/giây, tắt/giảm độ khó phải chơi, bỏ khoá sau nhiều lần thua. Archive: `Outdated/07-pause-resume-spec__v0.3.1__2026-10-08.md` |
| v0.3.1 | 2026-10-07 | **PATCH (làm rõ câu chữ)**: `PAUSE-043` — thẻ giới thiệu `FE-032` luôn hiển thị (bản cũ viết nhầm "khi chế độ đang bật"); ghi rõ thứ tự thử thách trước mật khẩu. Archive: `Outdated/07-pause-resume-spec__v0.3.0__2026-10-07.md` |
| v0.3.0 | 2026-10-07 | **MINOR (chủ dự án yêu cầu "làm hết" 2026-10-07; chi tiết do đội phát triển chọn, ghi rõ để chủ dự án điều chỉnh)**: mục 4a "Bảo vệ cả phụ huynh" — `PAUSE-040`–`PAUSE-043` (thử thách 5 phép tính/60 giây, Service sinh và chấm, khoá 5 phút sau 3 lần thất bại; áp dụng cho tạm dừng và tắt chế độ) |
| v0.2.2 | 2026-09-20 | **Vá gap quy trình `PROPOSED → APPROVED`**: `architecture-writer` phát hiện `PAUSE-021` vẫn còn tag `(PROPOSED)` trong văn bản dù toàn file đã đóng dấu `Approved` từ v0.2.1 và mục 6 ghi "không còn câu hỏi mở" (2 tầng trạng thái file vs requirement không khớp nhau). Chủ dự án xác nhận trực tiếp 2026-09-20: DUYỆT `PAUSE-021`, giữ nguyên ngưỡng đã có sẵn trong spec **> 5 lần/ngày** làm con số chính thức (trước đó chỉ ghi là "ví dụ minh hoạ"). Đổi tag từ `(PROPOSED)` sang `(ĐÃ CHỐT v0.2.2, APPROVED)`. Rà soát toàn file: không còn requirement `PAUSE-0xx` nào khác sót tag `PROPOSED`. Archive: `Specification/Outdated/07-pause-resume-spec__v0.2.1__2026-09-20.md` |
| v0.2.1 | 2026-09-17 | Chủ dự án approve toàn bộ requirement trong file này — chuyển trạng thái file từ `Draft` sang `Approved` |
| v0.2.0 | 2026-09-17 | **Chốt 2 câu hỏi mở**: `PAUSE-002a` — mốc "Hết ngày hôm nay" dùng cứng 23:59 theo giờ hệ thống, không cấu hình "giờ đi ngủ" riêng ở Phase 1. `PAUSE-002b` — Phase 1 chỉ hỗ trợ tạm dừng toàn bộ giám sát, không phân biệt theo app/browser cụ thể (cùng lý do đã bỏ whitelist app ở `BE-072`/`BE-073`). Không còn câu hỏi mở |
| v0.1.0 | 2026-09-17 | Khởi tạo |

# 05 — Anti-Uninstall & Tamper Protection Spec

> Version: v0.2.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Mục tiêu

Ngăn người dùng không có quyền (trẻ em) tắt, gỡ cài đặt, hoặc vô hiệu hoá app mà không qua xác thực mật khẩu phụ huynh — trong khi vẫn giữ khả năng phụ huynh gỡ hợp lệ khi cần.

## 2. Các véc-tơ tấn công cần phòng thủ (attack vectors)

| # | Kịch bản trẻ có thể thử | Biện pháp phòng thủ tương ứng |
|---|---|---|
| A1 | Vào Task Manager, kill process `Service`/`Vision`/`Overlay` | `ANTI-010` Watchdog kép + Windows Service tự động restart |
| A2 | Vào Control Panel / Settings → Apps, gỡ cài đặt trực tiếp | `ANTI-020` Custom uninstaller yêu cầu mật khẩu trước khi cho phép chạy `msiexec /x` hoặc tương đương |
| A3 | Xoá thủ công file cài đặt trong `Program Files` | `ANTI-030` ACL chặn quyền Delete/Write của user thường lên thư mục cài đặt |
| A4 | Sửa registry để tắt service tự khởi động | `ANTI-031` ACL registry key + watchdog phát hiện thay đổi registry bất thường |
| A5 | Khởi động Windows vào **Safe Mode** để bypass service | `ANTI-040` Ghi nhận và cảnh báo (không thể ngăn hoàn toàn — giới hạn kỹ thuật cố hữu của Windows, cần nêu rõ trong tài liệu) |
| A6 | Dùng tài khoản admin khác (nếu trẻ biết mật khẩu Windows admin) | `ANTI-041` Ngoài phạm vi bảo vệ phần mềm — cần khuyến nghị phụ huynh quản lý tài khoản Windows riêng biệt (đã ghi ở `GEN-022`). Phân tích đầy đủ lý do kỹ thuật + roadmap Phase 2 (kernel driver/MDM) xem `04-security-spec.md` mục `SEC-005` đến `SEC-007` |
| A7 | Reset toàn bộ máy (factory reset / cài lại Windows) | Không thể ngăn ở tầng ứng dụng — ghi nhận là giới hạn cố hữu |
| A8 | Sửa file `hosts`/firewall để chặn ngược app tự update hoặc heartbeat nội bộ | `ANTI-050` Không phụ thuộc network cho core function nên không bị ảnh hưởng; theo dõi riêng nếu có update mechanism |
| A9 | Sửa/làm hỏng trực tiếp file `config.db` (vd. mở bằng SQLite browser, ghi đè byte ngẫu nhiên) để đổi ngưỡng risk score, xoá whitelist, hoặc phá cấu trúc khiến app không đọc được | `ANTI-070` Service không dừng hoạt động khi không đọc/giải mã được `config.db` — tự động fallback dùng bộ cấu hình mặc định hard-code trong code, ở trạng thái an toàn nhất (giám sát luôn BẬT) |

## 3. Cơ chế Watchdog kép (Dual Watchdog)

Nguyên tắc: **không có single point of failure** — nếu 1 thành phần bị kill, thành phần còn lại phát hiện và khôi phục.

```
┌─────────────────────┐      heartbeat       ┌──────────────────────┐
│ ParentalGuard.Service│ ◄──────────────────► │ ParentalGuard.Watchdog│
│ (Windows Service)    │                      │ (Windows Service #2)  │
└─────────┬────────────┘                      └───────────┬───────────┘
          │ giám sát                                       │ giám sát
          ▼                                                ▼
   Vision / Overlay                          Service chính (nếu Service
   process con                                bị kill hoàn toàn, Watchdog
                                               tự cài đặt lại / khởi động lại)
```

- `ANTI-010`: Triển khai **2 Windows Service độc lập** (`Service` và `Watchdog`), mỗi service giám sát heartbeat của service kia. Nếu 1 service bị dừng bất thường (không qua quy trình dừng hợp lệ có xác thực mật khẩu), service còn lại:
  1. Ghi audit log sự kiện.
  2. Cố gắng khởi động lại service bị dừng (`sc start` tương đương qua Service Controller API).
  3. Nếu registry/service definition bị xoá hoàn toàn → Watchdog có bản sao cấu hình service tối thiểu để tự đăng ký lại.
- `ANTI-011`: Cả 2 service đặt `Recovery Options` trong Service Control Manager: "Restart the Service" cho lần fail thứ 1, 2, 3 (built-in Windows feature, lớp bảo vệ bổ sung miễn phí).

## 4. Bảo vệ khỏi gỡ cài đặt qua Control Panel/Settings (ANTI-020)

- Không dùng uninstaller mặc định của MSI/MSIX cho phép gỡ trực tiếp.
- Đăng ký custom uninstall command trong registry (`UninstallString`) trỏ đến 1 executable riêng của ParentalGuard — executable này:
  1. Hiện modal yêu cầu nhập mật khẩu quản trị (xem `06-password-management-spec.md`).
  2. Xác thực đúng → mới thực sự gọi quy trình gỡ (dừng cả 2 service, xoá file, xoá registry, có thể giữ lại audit log để phụ huynh xem log cuối nếu muốn).
  3. Sai mật khẩu hoặc huỷ → không gỡ gì cả, ghi log lần thử gỡ thất bại.

## 5. Bảo vệ file & registry bằng ACL (ANTI-030, ANTI-031)

- Thư mục cài đặt (`%ProgramFiles%\ParentalGuard`) set ACL: user thường có quyền Read+Execute, **không có** quyền Write/Delete. Chỉ SYSTEM và Administrators (thực thi qua uninstaller đã xác thực) có quyền ghi/xoá.
- Registry key cấu hình service (`HKLM\SYSTEM\CurrentControlSet\Services\ParentalGuard*`) set ACL tương tự — chặn user thường sửa `Start` value (kiểu thường dùng để vô hiệu hoá auto-start).

## 6. Giới hạn kỹ thuật cần truyền thông minh bạch với phụ huynh (ANTI-040, ANTI-041)

Không thể giải quyết 100% bằng phần mềm — cần ghi rõ trong tài liệu người dùng để tránh kỳ vọng sai:

- Safe Mode boot có thể bypass service khởi động thông thường.
- Tài khoản Windows Admin khác (nếu trẻ biết mật khẩu) có toàn quyền trên máy.
- Cài lại hệ điều hành/factory reset xoá mọi phần mềm.
- Boot từ USB/hệ điều hành khác (dual boot, live USB) hoàn toàn nằm ngoài khả năng kiểm soát của bất kỳ app Windows nào.

→ Khuyến nghị đi kèm (không phải tính năng app, mà là hướng dẫn sử dụng): phụ huynh nên là admin duy nhất trên máy, đặt mật khẩu BIOS/UEFI để chặn boot từ USB khác nếu muốn mức bảo vệ cao hơn.

## 7. Rate-limit & cảnh báo khi bị tấn công liên tục (ANTI-060)

- Nếu Vision/Overlay bị kill và restart quá N lần trong khoảng thời gian T (ví dụ 5 lần/10 phút) → coi là dấu hiệu bị tấn công có chủ đích:
  - Hiện thông báo rõ ràng trên màn hình (không ẩn giấu) rằng có nỗ lực vô hiệu hoá giám sát.
  - Ghi log chi tiết hơn (nhưng vẫn không chứa ảnh) để phụ huynh xem trong Dashboard.

## 8. Fail-secure khi file cấu hình bị hỏng/không đọc được (ANTI-070, ĐÃ CHỐT v0.2.0)

- `ANTI-070`: Nếu `Service` không đọc được hoặc không giải mã được `config.db` (file bị hỏng, bị sửa đổi trái phép làm sai cấu trúc, hoặc DPAPI giải mã thất bại vì bất kỳ lý do gì), **app tuyệt đối không được dừng giám sát**. `Service` tự động chuyển sang dùng **bộ cấu hình mặc định hard-code sẵn trong code** (không phụ thuộc file ngoài) để tiếp tục hoạt động ngay lập tức.
- Bộ cấu hình mặc định bắt buộc ở trạng thái an toàn nhất theo nguyên tắc fail-secure:
  - Giám sát luôn ở trạng thái **BẬT** (không bao giờ mặc định là tắt giám sát — tránh việc trẻ cố tình làm hỏng file để vô hiệu hoá bảo vệ).
  - Ngưỡng risk score dùng giá trị mặc định đã benchmark (`BE-090`).
  - Whitelist rỗng (không có ngoại lệ nào được áp dụng khi chưa xác thực lại được cấu hình gốc).
- `Service` ghi audit log sự kiện này (timestamp, lý do không đọc được config) để phụ huynh thấy khi mở Dashboard.
- Chi tiết kỹ thuật lưu trữ/mã hoá `config.db` xem `BE-060`/`BE-061` ở `02-backend-spec.md` và `SEC-040` ở `04-security-spec.md` — nội dung requirement này được liên kết 2 chiều giữa 3 file để tránh trùng lặp.

## 9. Câu hỏi mở

- [ ] Có nên tự động gửi thông báo (không qua network, chỉ hiển thị khi phụ huynh mở Dashboard) khi phát hiện nỗ lực tamper nhiều lần không, hay chỉ ghi log thụ động?
- [ ] Mức độ nên đầu tư chống Safe Mode bypass — Windows có cơ chế "Safe Mode with Networking" cho phép 1 số service chạy nếu đăng ký đúng cách, cần nghiên cứu thêm tính khả thi trước khi quyết định đầu tư công sức.
- [ ] Sau khi fallback về cấu hình mặc định (`ANTI-070`), `Service` có nên tự động ghi đè `config.db` bằng bản mặc định (để lần khởi động sau không phải fallback lại), hay giữ nguyên file hỏng và chỉ giữ mặc định trong bộ nhớ runtime cho tới khi phụ huynh mở Dashboard xác nhận/đặt lại cấu hình? Quyết định cụ thể ở System Design.

## 10. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.0 | 2026-09-17 | **Chốt**: thêm `ANTI-070` — fail-secure khi `config.db` bị hỏng/không đọc được, Service tự động fallback dùng bộ cấu hình mặc định hard-code trong code (giám sát luôn BẬT), không dừng hoạt động. Thêm attack vector A9 vào bảng mục 2. Section 8 mới, renumber section Câu hỏi mở/Changelog |
| v0.1.1 | 2026-09-17 | Thêm liên kết tới `04-security-spec.md` (`SEC-005` đến `SEC-007`) ở dòng A6 — phân tích đầy đủ lý do kịch bản admin nằm ngoài phạm vi bảo vệ chuyển sang file security để tránh trùng lặp nội dung |
| v0.1.0 | 2026-09-17 | Khởi tạo |

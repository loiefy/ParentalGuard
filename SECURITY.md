# Security Policy

ParentalGuard là app giám sát/bảo vệ trẻ em, chạy hoàn toàn local (`GEN-034`). Chúng tôi coi trọng báo cáo lỗ hổng bảo mật có trách nhiệm (responsible disclosure) — xem `Specification/04-security-spec.md` (`SEC-031`) và `Specification/10-additional-mechanisms-spec.md` (`MISC-070`).

## Báo cáo lỗ hổng bảo mật

**Không tạo public GitHub Issue cho lỗ hổng chưa được vá.**

Dùng tính năng **Private vulnerability reporting** của GitHub (tab *Security* → *Report a vulnerability* trên repo này). Kênh này gửi báo cáo riêng tư trực tiếp tới chủ dự án, không public cho tới khi có bản vá.

## Phạm vi

- Bypass cơ chế anti-uninstall/tamper (`05-anti-uninstall-tamper-spec.md`).
- Rò rỉ dữ liệu ảnh chụp màn hình ra ngoài tiến trình `Vision`/`Overlay` (`IMG-001`, `IMG-040`).
- Bypass xác thực mật khẩu quản trị / Recovery Key (`06-password-management-spec.md`).
- Lỗ hổng trong pipeline capture→classify cho phép thực thi mã hoặc leo thang quyền.

## Ngoài phạm vi

- Báo cáo yêu cầu quyền Administrator trên máy (đã ghi nhận ngoài phạm vi Phase 1, xem `Architecture/01-tong-quan-kien-truc.md` mục 7).

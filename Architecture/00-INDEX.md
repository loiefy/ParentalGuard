# ParentalGuard — Tài liệu Thiết kế Hệ thống (System Design / Architecture)

> **Giai đoạn**: `[2] Thiết kế hệ thống` (đã chuyển từ `[1] Viết Spec` — xem `../Specification/00-INDEX.md` mục 1)
> **Trạng thái**: `DRAFT` — đang viết, chưa review/approve
> **Cập nhật lần cuối**: 2026-09-17

---

## 1. Mục đích bộ tài liệu này

Bộ tài liệu này trả lời câu hỏi **"làm thế nào" (HOW)** để hiện thực hoá **"cái gì" (WHAT)** đã chốt trong `../Specification/`. Mỗi tài liệu ở đây phải truy vết được ngược về Requirement ID tương ứng trong spec (`GEN-`, `BE-`, `SEC-`, ...) — không phát minh thêm yêu cầu sản phẩm mới ở bước này; nếu trong lúc thiết kế phát hiện thiếu/mâu thuẫn với spec, phải quay lại sửa spec trước (theo đúng quy trình archive ở `CLAUDE.md`), không tự ý quyết định ở tài liệu kiến trúc.

Cấu trúc tài liệu đi từ **high-level → low-level**, từ mục tiêu tổng thể xuống chi tiết implement:

```
[01] Tổng quan kiến trúc          ← bức tranh toàn cảnh: component, tech stack, nguyên tắc xuyên suốt
        ↓
[02-09] Kiến trúc từng mảng       ← chi tiết hoá từng component/luồng (process, data, IPC, security, pipeline...)
        ↓
[10+]  Kiến trúc triển khai/vận hành ← deployment, CI/CD, release, dev workflow
```

## 2. Cấu trúc bộ tài liệu

| # | File | Nội dung | Trạng thái |
|---|---|---|---|
| 00 | `00-INDEX.md` | File này — mục lục, quy tắc, trạng thái tổng | Draft |
| 01 | `01-tong-quan-kien-truc.md` | System context, component architecture, tech stack, nguyên tắc thiết kế xuyên suốt, bảng quyết định kiến trúc (ADR) | **Draft — sẵn sàng review** |
| 02 | `02-process-architecture.md` | Chi tiết từng process (Service/Vision/Overlay/UI/Watchdog): lifecycle, trách nhiệm, state machine | Chưa viết |
| 03 | `03-ipc-communication.md` | Named Pipe contract cụ thể: message schema, thứ tự gọi, xử lý lỗi/timeout, HMAC signing | Chưa viết |
| 04 | `04-data-architecture.md` | Schema `config.db` (SQLite), format audit log (hash-chain), layout file trên đĩa | Chưa viết |
| 05 | `05-image-pipeline-architecture.md` | Class/component design pipeline capture→crop→resize→inference→decision, threading model | Chưa viết |
| 06 | `06-security-architecture.md` | ACL cụ thể (thư mục/registry), token/session handling, AppContainer, WFP rule, DPAPI application | Chưa viết |
| 07 | `07-anti-tamper-architecture.md` | Dual watchdog implementation, Service Recovery Options, custom uninstaller flow | Chưa viết |
| 08 | `08-ui-architecture.md` | WinUI 3 app structure (MVVM), navigation map, resource/đa ngôn ngữ | Chưa viết |
| 09 | `09-deployment-release-architecture.md` | Installer (MSI/MSIX), SignPath CI signing pipeline, GitHub Release flow | Chưa viết |
| 10 | `10-dev-automation-architecture.md` | Claude Code Agent workflow (Dev/Test/Debug/Report), GitHub Actions CI pipeline | Chưa viết |

## 3. Nguyên tắc truy vết (Traceability)

- Tài liệu kiến trúc **không có Requirement ID riêng** — mọi quyết định thiết kế phải trích dẫn ngược ID gốc trong spec (ví dụ: "Module `Vision` implement `BE-023a`"). Điều này tránh 2 bộ ID song song gây rối.
- Với quyết định kiến trúc thuần kỹ thuật, không map trực tiếp về 1 requirement cụ thể (ví dụ: chọn ORM nào, đặt tên namespace ra sao) → ghi dưới dạng **ADR (Architecture Decision Record)** ngắn gọn trong bảng ở mỗi file, không cần ID.

## 4. Quy tắc bảo trì (khác với `Specification/`)

- Tài liệu kiến trúc **được kỳ vọng thay đổi thường xuyên hơn spec** trong quá trình code thực tế (khi implement mới lộ ra chi tiết chưa lường trước) — vì vậy **không bắt buộc archive vào `Outdated/`** trước mỗi lần sửa như bên `Specification/`. Mỗi file vẫn có version header + bảng Changelog ở cuối để theo dõi lịch sử, nhưng không tạo bản sao riêng.
- Nếu 1 thay đổi kiến trúc kéo theo phải **sửa lại chính spec** (WHAT thay đổi, không chỉ HOW) — bắt buộc quay lại `Specification/` và làm đúng quy trình archive ở đó trước, rồi mới cập nhật tài liệu kiến trúc theo sau.
- Version từng file theo semver như bên spec (`PATCH`/`MINOR`/`MAJOR`), nhưng không có "version bộ tài liệu tổng" bắt buộc — chỉ theo dõi trạng thái Draft/Review/Approved theo cột ở mục 2.

## 5. Quy trình review tài liệu này

Giống quy trình Feature Gate đã chốt ở `TEST-002`/`TEST-003` (`11-testing-qa-process.md`): mỗi file kiến trúc viết xong → bạn review → approve hoặc yêu cầu sửa → mới sang file tiếp theo. Không viết dồn toàn bộ 10 file cùng lúc để tránh sai lệch lớn phải làm lại nhiều.

## 6. Changelog

| Ngày | Thay đổi |
|---|---|
| 2026-09-17 | Khởi tạo bộ tài liệu Architecture, viết xong `01-tong-quan-kien-truc.md`, còn lại chờ review tuần tự |

# CLAUDE.md — Hướng dẫn cho Claude Code khi làm việc trong dự án ParentalGuard

Đây là instruction cố định cho mọi phiên Claude Code làm việc trong repo này. Đọc file này trước khi thực hiện bất kỳ thay đổi nào liên quan đến `/Specification/` hoặc `/Architecture/`.

## Bối cảnh dự án

ParentalGuard là app Windows giám sát màn hình local, phát hiện/chặn nội dung khiêu dâm để bảo vệ trẻ em (dự án cộng đồng). Toàn bộ quyết định kiến trúc/spec được thống nhất qua quá trình review kỹ lưỡng — **không tự ý thay đổi nội dung đã `APPROVED`/`ĐÃ CHỐT` trong spec nếu người dùng không yêu cầu rõ ràng.**

Trạng thái hiện tại: **đã hoàn thành review/approve bước Viết Spec (bước 1), đã chuyển sang bước [2] Thiết kế hệ thống (System Design)** kể từ 2026-09-17 — người dùng đã xác nhận rõ ràng việc chuyển giai đoạn. Tài liệu thiết kế hệ thống nằm ở thư mục `Architecture/` (xem cấu trúc bên dưới), viết tuần tự từng file, review/approve như đã làm với spec trước khi sang file tiếp theo. **Vẫn chưa code** — chỉ chuyển sang bước code khi người dùng nói rõ tiếp.

## Cấu trúc thư mục spec

```
Specification/
├── 00-INDEX.md                          ← mục lục, changelog tổng, quy tắc version
├── 01-tong-quan-va-pham-vi.md
├── 02-backend-spec.md
├── 03-frontend-ui-spec.md
├── 04-security-spec.md
├── 05-anti-uninstall-tamper-spec.md
├── 06-password-management-spec.md
├── 07-pause-resume-spec.md
├── 08-performance-cpu-spec.md
├── 09-image-processing-spec.md
├── 10-additional-mechanisms-spec.md
├── 11-testing-qa-process.md
├── 12-dev-process-standards.md          ← GitHub settings, quản lý secret/key, code styling
└── Outdated/                            ← LƯU CÁC PHIÊN BẢN CŨ (xem quy tắc bên dưới)
```

Luôn đọc `00-INDEX.md` trước để nắm quy tắc đánh Requirement ID (`GEN-`, `BE-`, `FE-`, `SEC-`, `ANTI-`, `PWD-`, `PAUSE-`, `PERF-`, `IMG-`, `MISC-`, `TEST-`, `DEV-`) và bảng changelog tổng hiện tại trước khi sửa bất kỳ file nào.

## Cấu trúc thư mục Architecture (System Design)

```
Architecture/
├── 00-INDEX.md                          ← mục lục, quy tắc riêng, trạng thái từng file
├── 01-tong-quan-kien-truc.md            ← high-level: system context, component, tech stack, ADR
├── 02-process-architecture.md ... 10-dev-automation-architecture.md  ← chi tiết hoá từng mảng (xem 00-INDEX.md)
```

Quy tắc riêng cho `Architecture/` (đọc `Architecture/00-INDEX.md` mục 3-5 để biết đầy đủ):
- Không có Requirement ID riêng — mọi quyết định thiết kế phải trích dẫn ngược ID gốc trong `Specification/`.
- **Không bắt buộc archive vào `Outdated/`** trước khi sửa (khác `Specification/`) vì tài liệu kiến trúc được kỳ vọng thay đổi thường xuyên hơn khi code thực tế lộ ra chi tiết mới — chỉ cần version header + Changelog cuối file.
- Nếu sửa kiến trúc kéo theo phải đổi chính spec (WHAT, không chỉ HOW) → quay lại `Specification/` làm đúng quy trình archive ở đó trước.
- Viết tuần tự từng file, chờ người dùng review/approve mới sang file tiếp theo — không viết dồn nhiều file cùng lúc.

## QUY TẮC BẮT BUỘC: Archive phiên bản cũ trước khi sửa spec

**Mỗi khi 1 file spec trong `Specification/` bị sửa nội dung (không tính sửa lỗi chính tả nhỏ)**, PHẢI làm theo đúng thứ tự sau, không được bỏ qua:

1. **Trước khi sửa**: copy nguyên trạng file hiện tại vào `Specification/Outdated/`, đặt tên theo format:
   `<tên-file-gốc>__v<version-cũ>__<YYYY-MM-DD>.md`
   Ví dụ: sửa `04-security-spec.md` đang ở `v0.4.1` → copy thành `Specification/Outdated/04-security-spec__v0.4.1__2026-09-17.md` trước khi động vào file gốc.
2. **Sau đó mới sửa** file gốc trong `Specification/`: cập nhật nội dung, bump version ở header (theo semver: PATCH = làm rõ câu chữ, MINOR = thêm requirement mới, MAJOR = đổi kiến trúc/phạm vi lớn), thêm dòng vào bảng "Changelog file này" ở cuối file đó.
3. **Cập nhật `00-INDEX.md`**: thêm dòng vào bảng changelog tổng, và cập nhật dòng "Trạng thái spec" ở đầu file nếu cần.
4. Không bao giờ sửa trực tiếp 1 requirement đã đánh dấu `APPROVED`/`ĐÃ CHỐT` mà không tạo requirement mới ghi "supersedes <ID cũ>" và đánh dấu bản cũ `DEPRECATED` — đúng quy tắc đã có ở `00-INDEX.md` mục 4.

Không tự động thực hiện bước archive này cho các sửa đổi ngoài `Specification/` (code, config...) — quy tắc này CHỈ áp dụng cho thư mục spec.

## Khi người dùng yêu cầu trả lời câu hỏi mở / bổ sung spec

- Tìm đúng câu hỏi mở trong mục "Câu hỏi mở" của file liên quan trước khi trả lời.
- Sau khi có quyết định, xoá câu hỏi khỏi mục "Câu hỏi mở" (hoặc đánh dấu đã trả lời), viết quyết định thành requirement mới hoặc cập nhật requirement liên quan, kèm ghi chú "ĐÃ CHỐT" + version.
- Nếu quyết định ảnh hưởng nhiều file (ví dụ đổi kiến trúc), phải rà và cập nhật đồng bộ tất cả file liên quan trong cùng lượt, không để 2 file mâu thuẫn nhau — đúng như cách đã làm xuyên suốt quá trình review.
- Luôn hỏi lại người dùng nếu 1 quyết định có vẻ mâu thuẫn với requirement đã `APPROVED` trước đó, thay vì tự ý ghi đè.

## Khi nào KHÔNG áp dụng quy tắc archive

- Sửa lỗi đánh máy/chính tả không đổi ý nghĩa.
- Các file ngoài `Specification/` (code, README dự án, v.v. — trừ khi người dùng chỉ định thêm).

# 01 — Tổng quan Kiến trúc Hệ thống

> Version: v0.3.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Mục tiêu hệ thống (nhắc lại từ spec, góc nhìn kiến trúc)

ParentalGuard là 1 hệ thống **hoàn toàn chạy local trên 1 máy Windows**, không có backend, không có server, không có tài khoản cloud. Toàn bộ "hệ thống" nằm gọn trong ranh giới 1 máy tính — đây là điểm khác biệt kiến trúc quan trọng nhất so với đa số app hiện đại (không có kiến trúc client-server, không có API, không có database trung tâm).

Mục tiêu kỹ thuật cốt lõi cần kiến trúc phục vụ đúng (nguồn: `01-tong-quan-va-pham-vi.md`, `04-security-spec.md`):
1. Phát hiện + chặn nội dung khiêu dâm hiển thị trên màn hình, độ trễ thấp.
2. Không thể bị trẻ em tắt/gỡ mà không qua xác thực.
3. Không rò rỉ dữ liệu nhạy cảm (ảnh chụp màn hình) dưới bất kỳ hình thức nào — kể cả ra ngoài máy lẫn tồn lại trên đĩa.
4. Tài nguyên máy tiêu tốn ở mức chấp nhận được cho chạy nền dài hạn.

## 2. System Context Diagram

```
┌──────────────────────────────────────────────────────────────────┐
│                         1 MÁY WINDOWS (duy nhất)                  │
│                                                                    │
│   ┌────────────┐        ┌─────────────────────────────────┐      │
│   │  Phụ huynh │───────▶│   ParentalGuard.UI (Dashboard)   │      │
│   └────────────┘        └─────────────────────────────────┘      │
│                                       │                            │
│   ┌────────────┐                     │ Named Pipe (local IPC)     │
│   │  Trẻ em    │◀───┐                ▼                            │
│   └────────────┘    │        ┌───────────────────┐                │
│        │             │        │ ParentalGuard.    │                │
│        │ dùng máy    │ overlay│ Service (LocalSys) │                │
│        ▼             │ chặn   └─────────┬─────────┘                │
│  [Ứng dụng bất kỳ]───┼────────┐         │ Named Pipe               │
│  (browser, video...)  │        │         ▼                          │
│        ▲               │  ┌─────────────────────┐  ┌─────────────┐│
│        │ capture pixel  └─▶│ ParentalGuard.Overlay│  │ ParentalGuard│
│        │ (Desktop           └─────────────────────┘  │ .Watchdog   ││
│        │  Duplication API)                            │ (Service #2)││
│        │                                               └──────┬──────┘│
│   ┌──────────────────┐          heartbeat (Named Pipe) ◀──────┘      │
│   │ ParentalGuard.    │◀────────────────────────────────────────────┘
│   │ Vision (AI infer)  │
│   └────────────────────┘
│                                                                    │
└──────────────────────────────────────────────────────────────────┘

KHÔNG có kết nối ra ngoài ranh giới này dưới bất kỳ hình thức nào (GEN-034).
```

- **Không có hệ thống bên ngoài nào** trong context diagram — không cloud API, không update server, không license server, không telemetry endpoint. Đây là điểm khác biệt lớn nhất so với kiến trúc "chuẩn" của đa số app thương mại hiện đại, xuất phát trực tiếp từ `GEN-034`/`SEC-001a`.
- 2 "actor" con người: **Phụ huynh** (tương tác qua `UI`, xác thực qua `Service`) và **Trẻ em** (bị giám sát thụ động, tương tác cưỡng bức với `Overlay` khi vi phạm).

## 3. Component Architecture (5 process)

| Process | Vai trò 1 câu | Chạy khi nào | Quyền |
|---|---|---|---|
| `ParentalGuard.Service` | Bộ não — sở hữu cấu hình, điều phối, xác thực, audit log | Từ lúc khởi động máy (trước đăng nhập) tới khi tắt máy | LocalSystem |
| `ParentalGuard.Watchdog` | Giám sát `Service`, tự khôi phục nếu `Service` bị kill | Song song `Service`, độc lập | LocalSystem |
| `ParentalGuard.Vision` | Capture + AI inference, single-purpose tuyệt đối | Chỉ khi có user đăng nhập session tương tác | User session, quyền hạn chế |
| `ParentalGuard.Overlay` | Vẽ overlay chặn, icon trạng thái, toast notification | Song song session người dùng | User session, quyền thấp |
| `ParentalGuard.UI` | Dashboard cho phụ huynh | Chỉ khi phụ huynh chủ động mở | User session |

Chi tiết lifecycle/trách nhiệm từng process → `02-process-architecture.md`. Chi tiết message contract giữa các process → `03-ipc-communication.md`.

## 4. Technology Stack

| Lớp | Lựa chọn | Nguồn quyết định |
|---|---|---|
| Ngôn ngữ/Runtime | C# / .NET 10 (đổi từ .NET 8 do gần hết hạn hỗ trợ, xem `GEN-003a`) | `GEN-003a` |
| UI Framework | WinUI 3 (Windows App SDK) | `GEN-003` |
| AI Inference | ONNX Runtime + DirectML (fallback CPU) | `GEN-003`, `PERF-030`/`031` |
| Model AI | `GantMan/nsfw_model` (MobileNetV2, MIT) convert sang `.onnx` | `IMG-014` |
| Screen Capture | Desktop Duplication API (DXGI) | `GEN-003` |
| IPC | Named Pipes (`System.IO.Pipes`) + Protobuf, ký HMAC | `BE-050`/`051` |
| Local storage | SQLite (`config.db`), file phẳng cho audit log/password hash | `BE-060` |
| Mã hoá at-rest | Windows DPAPI (machine-scope) | `SEC-040` |
| Hash password | Argon2id | `PWD-011` |
| Code signing | Chứng chỉ OV qua SignPath Foundation (CI) | `SEC-030` |
| CI/CD | GitHub Actions + Claude Code Agent | `TEST-002`, `DEV-030` |
| Test | xUnit/NUnit, test harness riêng cho integration | `11-testing-qa-process.md` mục 2 |

## 5. Nguyên tắc thiết kế xuyên suốt (Cross-cutting Principles)

Mọi tài liệu kiến trúc con (02-10) phải tuân thủ các nguyên tắc sau, kế thừa trực tiếp từ spec:

| Nguyên tắc | Ý nghĩa với kiến trúc | Nguồn |
|---|---|---|
| **Local-first tuyệt đối** | Không thiết kế bất kỳ interface/hook nào cho network, kể cả "để dành sau" | `GEN-034`, `SEC-001a` |
| **Least privilege** | Mỗi process chỉ xin đúng quyền OS cần thiết, không "xin dư cho chắc" | `SEC-002` |
| **Fail-secure** | Mọi lỗi/exception không rõ nguyên nhân → mặc định nghiêng về phía AN TOÀN HƠN (giám sát bật, không phải tắt) | `BE-061a`, `ANTI-070` |
| **Defense in depth** | Không thiết kế 1 điểm kiểm soát duy nhất cho bất kỳ cơ chế bảo vệ nào | `SEC-004` |
| **Không lưu trữ ảnh** | Mọi component chạm vào pixel/frame phải có đường zero-out rõ ràng trong thiết kế, không phụ thuộc GC | `IMG-001`, `IMG-003` |
| **Minh bạch** | Không thiết kế bất kỳ cơ chế ẩn giấu sự tồn tại của app | `SEC-003` |
| **Truy vết được** | Mọi quyết định code phải map ngược về Requirement ID | Mục 3 file này |

## 6. Bảng quyết định kiến trúc quan trọng (ADR tóm tắt)

| # | Quyết định | Lý do chính | Requirement liên quan |
|---|---|---|---|
| ADR-01 | `Vision` chạy trong session tương tác qua `CreateProcessAsUser`, không phải `CreateProcess` từ Session 0 | Desktop Duplication API chỉ hoạt động ở session tương tác; `Service` chạy Session 0 bị cô lập | `BE-023a` |
| ADR-02 | 5 process tách biệt thay vì 1 process monolithic | Least privilege — `Vision` (nhạy cảm nhất) bị cô lập network + quyền thấp, không ảnh hưởng nếu bị compromise | `GEN-004`, `SEC-002` |
| ADR-03 | Dùng exclude-list (không phải allow-list) để tối ưu capture | Whitelist ứng dụng là bài toán vô hạn, luôn đi sau; giám sát theo cửa sổ active giải quyết triệt để | `BE-071`/`BE-072` |
| ADR-04 | Crop GPU-side về đúng bounding rect cửa sổ trước khi resize | Vừa tăng performance vừa tăng độ chính xác model với cửa sổ nhỏ trên màn hình lớn | `IMG-012` |
| ADR-05 | Dual Windows Service (Service + Watchdog) thay vì 1 service + restart tự động của SCM | Không có single point of failure — SCM restart chỉ xử lý crash của chính service đó | `ANTI-010` |
| ADR-06 | DPAPI machine-scope thay vì tự implement mã hoá | Không tự quản lý key — giảm bề mặt tấn công, tận dụng cơ chế đã được kiểm chứng của OS | `SEC-040` |
| ADR-07 | Không có cơ chế auto-update | Ưu tiên tuyệt đối "zero network" hơn tiện lợi cập nhật tự động | `GEN-034`, `MISC-020` (REJECTED) |
| ADR-08 | Model AI: trọng số `GantMan/nsfw_model` convert ONNX, không dùng Python runtime | License MIT không ràng buộc, nhẹ (MobileNetV2), khớp stack C#/.NET đã chọn | `IMG-014` |
| ADR-09 | Ký code bằng chứng chỉ OV qua SignPath Foundation, không tự mua EV | Dự án free/open-source, không có ngân sách tự trả phí EV cert + HSM hàng năm | `SEC-030`, `DEV-012` |
| ADR-10 | Quy trình Dev/Test/Debug/Report bằng Claude Code Agent, Approve vẫn là người | Tự động hoá tối đa nhưng giữ nguyên tắc không tự test tự duyệt | `TEST-002`/`TEST-003` |
| ADR-11 (ĐÃ CHỐT v0.2.0) | **1 repo GitHub duy nhất** cho cả code lẫn tài liệu spec/kiến trúc (`Specification/`, `Architecture/`), không tách 2 repo riêng | Dự án nhỏ, 1 người maintain — giữ chung giúp lịch sử commit/PR liên kết trực tiếp code ↔ requirement ID mà không cần đồng bộ 2 repo | `12-dev-process-standards.md` |

## 7. Ngoài phạm vi kiến trúc Phase 1 (nhắc lại, không thiết kế chi tiết)

- Kernel driver / MDM (chống trẻ có quyền Administrator) — `SEC-005` đến `SEC-007`.
- Đa hồ sơ/đa người dùng máy — `MISC-040`.
- Phân tích temporal video (nhiều frame liên tiếp) — `IMG-031`.
- Câu hỏi bảo mật bổ sung cho Recovery — `PWD-034`.

## 8. Câu hỏi mở

_Hiện không còn câu hỏi mở nào trong file này._

## 9. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.3.0 | 2026-09-17 | Cập nhật tech stack: .NET 8 → **.NET 10** (khớp `GEN-003a` mới ở `Specification/00-INDEX.md`, supersedes `GEN-003`) — lý do .NET 8 hết hạn hỗ trợ 10/11/2026 |
| v0.2.0 | 2026-09-17 | Chốt câu hỏi mở: giữ chung 1 repo GitHub cho cả code lẫn tài liệu spec/kiến trúc — `ADR-11` mới. File này không còn câu hỏi mở |
| v0.1.0 | 2026-09-17 | Khởi tạo — system context, component architecture, tech stack, nguyên tắc xuyên suốt, ADR tóm tắt 10 quyết định kiến trúc chính |

# ParentalGuard.Overlay

Đợt 0 (`ROADMAP.md`): khung xương console app — nhận bootstrap (pipe handle kế thừa) từ `Service`
qua tham số dòng lệnh, đọc khoá HMAC + tên pipe (`ParentalGuard.Ipc.Client.ChildIpcBootstrap`),
connect vào `ParentalGuard.Svc.Overlay` (retry backoff, `Architecture/03` mục 4.1), handshake
`Hello`/`HelloAck`, giữ heartbeat sống (`ParentalGuard.Ipc.Client.IpcChildClient`, dùng chung với
`ParentalGuard.Vision`).

Process này được `Service` spawn qua `CreateProcessAsUser` với Restricted Token + Low Integrity
Level vào đúng session tương tác, giống hệt pipeline của `Vision` nhưng phạm vi tài nguyên hẹp
hơn (không có quyền Read `models\*.onnx`, `Architecture/06` mục 2.4).

## Đợt 1 (`ROADMAP.md`, `BE-030`-`032`) — overlay blur tối giản

`Program.cs` dùng `Main` truyền thống + `[STAThread]` (không phải top-level statements như
`Vision`/Service) vì WinForms cần thread STA cho message loop (`Application.Run`). IPC chạy trên
1 `Task` nền riêng; `Rendering/OverlayCoordinator.cs` (1 `Form` ẩn giữ message loop) nhận
`OverlayRectListCommand` từ Thread IPC (marshal qua `Control.Invoke`) và dựng/xoá
`Rendering/ContentBlurOverlayForm.cs` — mỗi cửa sổ vi phạm 1 overlay riêng che đúng rect, có nút
"Tắt nội dung". Overlay **không tự biết lý do** 1 rect nằm trong danh sách (`OverlayRect.Reason`
chỉ đọc bởi Service — Architecture/02 mục 5.1).

**`BE-032` (Session 0 Isolation, force-close)**: Session 0 Isolation khiến Service không thể gọi
API `user32` lên HWND của session tương tác — Overlay tự đóng cửa sổ cục bộ
(`Windows/OverlayWindowInterop.cs`, best-effort `WM_CLOSE`) và vẫn báo `ForceCloseRequest` lên
Service để cập nhật state/audit log. Đã đồng bộ đầy đủ ở `Architecture/02-process-architecture.md`
mục 2.4/5 (v0.1.1) và `Specification/02-backend-spec.md` BE-032 (v0.11.3, ĐÃ CHỐT) — xem
`docs/dependency-map.md` mục "Ghi chú khoảng trống đã biết" nếu cần chi tiết.

**Chưa có ở Đợt 1** (Đợt 2, `FE-016`/`BE-084`-`089`): vùng loại trừ nút đóng 3 lớp, đa cửa sổ vi
phạm z-index khớp cửa sổ gốc, giới hạn 10 overlay + chế độ gộp, multi-monitor. Icon trạng thái
(`BE-033`) và Toast UI thật (`BE-061b`) vẫn là Đợt 6.

**Chỉ verify được ở mức build trong môi trường dev/CI không có session tương tác thật** — hành vi
runtime (vẽ đúng vị trí, click nút đóng đúng cửa sổ) cần chủ dự án tự kiểm tra trên máy Windows
thật.

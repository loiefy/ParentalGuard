# src/

Khung thư mục cho 5 process của ParentalGuard, theo đúng bảng Component Architecture đã chốt ở `Architecture/01-tong-quan-kien-truc.md` mục 3 (`GEN-004`, `ADR-02`).

Đây chỉ là **khung thư mục** — chưa có `.csproj`/`.sln` hay logic nghiệp vụ nào. Từng project sẽ được scaffold code thật (bằng agent `feature-dev`) khi:
1. Requirement ID liên quan đã `APPROVED` trong `Specification/`.
2. Thiết kế HOW tương ứng đã viết & approve trong `Architecture/` (`02-process-architecture.md`, `03-ipc-communication.md`, `05-image-pipeline-architecture.md`, `06-security-architecture.md`, `07-anti-tamper-architecture.md`, `08-ui-architecture.md` — tất cả hiện "Chưa viết").

| Thư mục | Process | Chi tiết HOW ở |
|---|---|---|
| `ParentalGuard.Service/` | Bộ não — cấu hình, điều phối, xác thực, audit log (LocalSystem) | `02`, `03`, `04`, `06` |
| `ParentalGuard.Watchdog/` | Giám sát & tự khôi phục `Service` (LocalSystem) | `02`, `07` |
| `ParentalGuard.Vision/` | Capture + AI inference, single-purpose (user session, quyền hạn chế) | `02`, `05`, `06` |
| `ParentalGuard.Overlay/` | Vẽ overlay chặn, icon trạng thái, toast (user session, quyền thấp) | `02`, `03`, `08` |
| `ParentalGuard.UI/` | Dashboard cho phụ huynh (user session) | `02`, `08` |

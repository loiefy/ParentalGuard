# src/

5 process của ParentalGuard, theo đúng bảng Component Architecture đã chốt ở
`Architecture/01-tong-quan-kien-truc.md` mục 3 (`GEN-004`, `ADR-02`). Đợt 0 (`ROADMAP.md`) đã
scaffold code thật cho `Service`/`Vision`/`Overlay` + 1 thư viện dùng chung `ParentalGuard.Ipc`
(envelope Protobuf + framing/HMAC + client handshake, dùng chung bởi `Service` lẫn `Vision`/`Overlay`
— không phải 1 trong 5 process, chỉ là thư viện chia sẻ contract IPC).

`Watchdog`/`UI` vẫn là khung thư mục placeholder — chờ `Architecture/07-anti-tamper-architecture.md`
(Đợt 4) và `08-ui-architecture.md` (Đợt 6).

| Thư mục | Process | Trạng thái Đợt 0 | Chi tiết HOW ở |
|---|---|---|---|
| `ParentalGuard.Ipc/` | *(thư viện dùng chung, không phải process riêng)* | Có code thật | `03` |
| `ParentalGuard.Service/` | Bộ não — cấu hình, điều phối, xác thực, audit log (LocalSystem) | Có code thật (skeleton Đợt 0) | `02`, `03`, `04`, `06` |
| `ParentalGuard.Watchdog/` | Giám sát & tự khôi phục `Service` (LocalSystem) | Placeholder — chưa tới lượt | `02`, `07` (chưa viết) |
| `ParentalGuard.Vision/` | Capture + AI inference, single-purpose (user session, quyền hạn chế) | Có code thật (khung xương — heartbeat, chưa capture) | `02`, `05` (chưa viết), `06` |
| `ParentalGuard.Overlay/` | Vẽ overlay chặn, icon trạng thái, toast (user session, quyền thấp) | Có code thật (khung xương — heartbeat, chưa vẽ) | `02`, `03`, `08` (chưa viết) |
| `ParentalGuard.UI/` | Dashboard cho phụ huynh (user session) | Placeholder — chưa tới lượt | `02`, `08` (chưa viết) |

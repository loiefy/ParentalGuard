# ParentalGuard.Service

Windows Service (LocalSystem, `Start type = Automatic`), bộ não hệ thống — Đợt 0 (`ROADMAP.md`) đã implement:

- **Fail-secure config loading** (`BE-060`/`061`/`061a`/`061b`, `Data/`): SQLite `config.db` (`Microsoft.Data.Sqlite`, WAL mode) + DPAPI machine-scope (`System.Security.Cryptography.ProtectedData`). Lần đầu cài đặt tạo mới với default (`BE-073a`); mọi lỗi đọc/giải mã (`ConfigLoadException`) đi qua `FailSecureConfigLoader` — ghi audit log, default hard-code (giám sát luôn BẬT, exclude-list rỗng), sinh khoá HMAC mới, ghi đè `config.db` mới hoàn toàn (`Architecture/04` mục 6).
- **Audit log hash-chain** (`MISC-010`/`SEC-041`, `Audit/AuditLogWriter.cs`): JSONL append-only, canonical sorted-key JSON + SHA-256 chain, verify N=50 record cuối lúc khởi động (ADR-28), tự mở đoạn chain mới + ghi `AuditChainBrokenDetected` khi phát hiện tamper.
- **ACL** (`Architecture/06` mục 4, `Security/AclProvisioner.cs`): tự áp ACL idempotent cho `%ProgramFiles%\ParentalGuard\` và `%ProgramData%\ParentalGuard\` mỗi lần Starting (chưa có installer ở Đợt 0 — ADR-37).
- **WFP network block cho Vision** (`SEC-010`/`016`-`018`, `Security/WfpVisionBlocker.cs`, `WfpInterop.cs`): Provider/Sublayer/Filter riêng qua `Fwpuclnt.dll`, chặn `ALE_AUTH_CONNECT`+`ALE_AUTH_RECV_ACCEPT` (IPv4/IPv6), idempotent, áp dụng lúc Starting. `Security/VisionNetworkWatcher.cs` theo dõi Security Event 5157, xử lý như crash (ADR-33/34).
- **Token/process pipeline** (`BE-023a`, `Architecture/06` mục 2, `Security/RestrictedTokenFactory.cs` + `ChildProcessLauncher.cs`): `WTSQueryUserToken` → `DuplicateTokenEx` → `CreateRestrictedToken` (`DISABLE_MAX_PRIVILEGE` + disable SID Administrators) → Low Integrity Level → `CreateProcessAsUser`, bootstrap khoá HMAC qua `AnonymousPipeServerStream` kế thừa handle.
- **Named Pipe server + supervision** (`Architecture/03`, `Ipc/ChildProcessSupervisor.cs`, `Security/PipeAclFactory.cs`): ACL SID + Mandatory Label Low no-write-up (ADR-31), xác thực danh tính theo đường dẫn cài đặt lúc connect (mục 4.2 điều kiện 3a — điều kiện 3b chữ ký code-signing để Đợt 9, xem `docs/dependency-map.md`), handshake Hello/HelloAck, heartbeat, tự phục hồi khi crash/pipe vỡ (`BE-040`/`BE-023`).
- **Session tương tác** (`Session/SessionWatcher.cs`): message-only window nhận `WM_WTSSESSION_CHANGE`, tự respawn Vision/Overlay đúng session khi đổi (khoá màn hình, Fast User Switching, RDP).

`Worker.cs` orchestrate toàn bộ trình tự Starting theo `Architecture/02-process-architecture.md` mục 6.

Chưa có ở Đợt 0 (đúng phạm vi `ROADMAP.md`): capture/inference (Đợt 1), overlay rendering thật (Đợt 2), password/auth (Đợt 3), Watchdog/anti-tamper (Đợt 4), pause/resume logic (Đợt 5), Dashboard UI (Đợt 6).

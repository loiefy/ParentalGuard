using Google.Protobuf;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Config;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Audit;

/// <summary>
/// Orchestrator domain Lịch sử (`10-ui-architecture.md` mục 6.3, `PWD-020`/`MISC-030`) — nhận
/// <c>AuditLogQuery</c>/<c>MarkFalsePositiveRequest</c> từ pipe <c>UI</c>. Việc ghi
/// <c>user_whitelisted_process_names</c> (`MISC-030a`: không còn thêm whitelist từ đây)
/// (1 nguồn ghi duy nhất, tránh trùng lặp logic persist/push `ControlVisionCommand`).
/// </summary>
/// <remarks>
/// <b>2026-09-28 audit fix (2 vòng security-privacy-auditor độc lập xác nhận FAIL cứng)</b>: gate
/// "đã qua <c>view_audit_log</c>" trước đây lưu ở field cấp INSTANCE của lớp này — mà
/// <see cref="AuditLogCoordinator"/> chỉ được tạo 1 lần cho toàn vòng đời <c>Service</c>
/// (<c>Worker.cs</c>), dùng CHUNG cho mọi kết nối UI kế tiếp. Hệ quả: phụ huynh xác thực + xem trang 0
/// xong đóng app, trong 10 phút sau đó BẤT KỲ ai tự mở lại <c>ParentalGuard.UI.exe</c> (kể cả đứa trẻ
/// bị giám sát — <c>VerifyClientIdentity</c> chỉ kiểm tra đường dẫn exe, chưa có code-signing) gửi
/// <c>AuditLogQuery</c> với token RỖNG đều đọc được toàn bộ audit log KHÔNG CẦN mật khẩu — vi phạm
/// trực tiếp `PWD-020` (PoC thực nghiệm xác nhận bypass thành công, chạy 2 lần độc lập). Lý do biện
/// minh cũ ("UI single-instance qua Mutex, ADR-117a") KHÔNG hợp lệ — `ADR-117a` tự ghi rõ đây là "UX
/// polish thuần, KHÔNG PHẢI yêu cầu bảo mật". Đúng nguyên văn `10-ui-architecture.md` mục 6.3 ("nhớ đã
/// qua gate theo đúng SESSION PIPE HIỆN TẠI"), gate nay sống trong <see cref="AuditLogViewSession"/> —
/// 1 instance MỚI tạo mỗi lần <c>UiSessionServer.RunConnectionAsync</c> bắt đầu (1 kết nối pipe), tự
/// giải phóng khi kết nối đóng — không còn field service-wide nào để rò rỉ qua kết nối khác.
/// </remarks>
public sealed class AuditLogCoordinator(AuthCoordinator authCoordinator, AuditLogWriter auditLog, MonotonicClock clock, string configDbPath)
{
    private const string ViewAuditLogActionContext = "view_audit_log";

    /// <summary>
    /// `10-ui-architecture.md` mục 6.3: <c>action_token</c> chỉ bắt buộc hợp lệ ở request ĐẦU TIÊN của
    /// 1 phiên xem — các trang kế tiếp trong CÙNG 1 kết nối pipe gửi token rỗng. Cửa sổ tái sử dụng cụ
    /// thể (10 phút) là quyết định implement, không phải giá trị bảo mật — đủ dài cho 1 phiên cuộn/tải
    /// thêm trang bình thường. Phạm vi áp dụng giới hạn đúng 1 <see cref="AuditLogViewSession"/> (1 kết
    /// nối pipe) — xem ghi chú audit fix 2026-09-28 ở class doc phía trên.
    /// </summary>
    private static readonly TimeSpan _viewSessionWindow = TimeSpan.FromMinutes(10);

    private const uint DefaultPageSize = 50;
    private const uint MaxPageSize = 200;

    private readonly IpcMessageIdGenerator _messageIds = new();

    public Task<IpcPayload> HandleAsync(IpcPayload request, AuditLogViewSession session, CancellationToken cancellationToken) => request.BodyCase switch
    {
        IpcPayload.BodyOneofCase.AuditLogQuery => HandleAuditLogQueryAsync(request, session, cancellationToken),
        IpcPayload.BodyOneofCase.MarkFalsePositiveReq => HandleMarkFalsePositiveRefused(request),
        IpcPayload.BodyOneofCase.VerifyAuditChainReq => HandleVerifyAuditChainAsync(request, session, cancellationToken),
        _ => throw new InvalidOperationException($"AuditLogCoordinator received unexpected message: {request.BodyCase}."),
    };

    private async Task<IpcPayload> HandleAuditLogQueryAsync(IpcPayload request, AuditLogViewSession session, CancellationToken cancellationToken)
    {
        AuditLogQuery req = request.AuditLogQuery;
        IpcPayload response = NewResponse(request);
        long trustedNow = clock.UtcNowUnixMs;

        if (req.ActionToken.Length > 0)
        {
            if (!await ConsumeTokenAsync(req.ActionToken, ViewAuditLogActionContext, cancellationToken).ConfigureAwait(false))
            {
                response.AuditLogResp = new AuditLogResponse { Result = AuditLogQueryResult.InvalidToken };
                return response;
            }

            session.GateOpenUntilUnixMs = trustedNow + (long)_viewSessionWindow.TotalMilliseconds;
        }
        else if (!IsGateOpen(session, trustedNow))
        {
            response.AuditLogResp = new AuditLogResponse { Result = AuditLogQueryResult.InvalidToken };
            return response;
        }

        uint pageSize = req.PageSize == 0 ? DefaultPageSize : Math.Min(req.PageSize, MaxPageSize);
        (IReadOnlyList<AuditLogEntryRaw> entries, bool hasMore) = await auditLog.ReadPageAsync((int)req.Page, (int)pageSize, cancellationToken).ConfigureAwait(false);

        var resp = new AuditLogResponse { Result = AuditLogQueryResult.Success, HasMore = hasMore };
        resp.Entries.AddRange(entries.Select(e => new AuditLogEntry
        {
            Seq = (ulong)e.Seq,
            TsUnixMs = e.TsUnixMs,
            EventType = e.EventType,
            DetailJson = e.DetailJson,
            ProcessName = e.ProcessName,
            RiskScore = e.RiskScore,
        }));
        response.AuditLogResp = resp;
        return response;
    }

    /// <summary>
    /// `MISC-030a` (Specification/10 v0.3.0, ĐÃ CHỐT 2026-10-01, supersedes `MISC-030`): bỏ "Đánh dấu sai" —
    /// thao tác này loại CẢ ứng dụng khỏi giám sát chỉ vì 1 lần chặn nhầm. Luôn từ chối, không tiêu thụ
    /// action_token, không đụng tới whitelist (UI chính thức không còn gửi; chặn cả client tự chế).
    /// </summary>
    private Task<IpcPayload> HandleMarkFalsePositiveRefused(IpcPayload request)
    {
        IpcPayload response = NewResponse(request);
        response.MarkFalsePositiveResp = new MarkFalsePositiveResponse { Result = MarkFalsePositiveResult.Unspecified };
        return Task.FromResult(response);
    }

    /// <summary>
    /// `10-ui-architecture.md` mục 6.3 (ADR-141), `04-data-architecture.md` mục 5.3a (ADR-138) — thao
    /// tác chỉ-đọc từ góc nhìn UI (không mang <c>action_token</c> riêng, đã ở trong `S3` qua gate
    /// <c>view_audit_log</c> lúc vào trang). Việc ghi <c>AuditChainBrokenDetected</c> mới (nếu có) và
    /// <c>audit_meta.last_full_verify_at_unix_ms</c> nằm bên trong <see cref="AuditLogWriter.VerifyFullChainAsync"/>/
    /// <see cref="TryPersistLastFullVerifyAt"/> — không phải hành vi rẽ nhánh của coordinator này.
    /// </summary>
    /// <remarks>
    /// <b>2026-09-29 audit fix (ADR-142, FAIL cứng do security-privacy-auditor phát hiện)</b>: request
    /// không mang <c>action_token</c> nhưng VẪN PHẢI enforce gate <c>view_audit_log</c> qua
    /// <paramref name="session"/> — bản gốc drop mất tham số này khi định tuyến từ <see cref="HandleAsync"/>,
    /// khiến endpoint chạy thẳng không xác thực (bypass hoàn toàn `PWD-020`). Dùng lại ĐÚNG state
    /// <see cref="AuditLogViewSession.GateOpenUntilUnixMs"/> mà <see cref="HandleAuditLogQueryAsync"/> đã
    /// dùng cho trang 2+ token rỗng — cùng 1 nguồn sự thật, session-scoped per kết nối pipe (đúng cơ chế
    /// đã sửa lỗ hổng tương tự ở `AuditLogQuery`, Đợt 7).
    /// </remarks>
    private async Task<IpcPayload> HandleVerifyAuditChainAsync(IpcPayload request, AuditLogViewSession session, CancellationToken cancellationToken)
    {
        IpcPayload response = NewResponse(request);
        long trustedNow = clock.UtcNowUnixMs;
        if (!IsGateOpen(session, trustedNow))
        {
            response.VerifyAuditChainResp = new VerifyAuditChainResponse { Result = VerifyAuditChainResult.InvalidToken };
            return response;
        }

        AuditChainVerifyResult result = await auditLog.VerifyFullChainAsync(cancellationToken).ConfigureAwait(false);
        TryPersistLastFullVerifyAt(result.VerifiedAtUnixMs);

        response.VerifyAuditChainResp = new VerifyAuditChainResponse
        {
            Result = VerifyAuditChainResult.Success,
            IsIntact = result.IsIntact,
            TotalRecordsScanned = result.TotalRecordsScanned,
            BrokenAtSeq = result.BrokenAtSeq,
            VerifiedAtUnixMs = result.VerifiedAtUnixMs,
        };
        return response;
    }

    /// <summary>Best-effort — mốc thời gian hiển thị chỉ là UX phụ trợ (mục 3.6), không chặn kết quả verify trả về UI nếu ghi <c>config.db</c> lỗi.</summary>
    private void TryPersistLastFullVerifyAt(long verifiedAtUnixMs)
    {
        try
        {
            using ConfigDb db = ConfigDb.Open(configDbPath);
            db.UpdateLastFullVerifyAt(verifiedAtUnixMs);
        }
        catch (ConfigLoadException)
        {
        }
    }

    /// <summary>
    /// Gate mở nếu phiên đăng nhập phụ huynh của kết nối này còn hiệu lực (gia hạn idle — `PWD-024`, ADR-150)
    /// HOẶC cửa sổ "đã qua <c>view_audit_log</c>" 1-lần còn hạn (cơ chế cũ, giữ tương thích).
    /// </summary>
    private static bool IsGateOpen(AuditLogViewSession session, long trustedNow) =>
        session.ParentSession?.TryTouch() == true
        || (session.GateOpenUntilUnixMs is long gateOpenUntil && trustedNow < gateOpenUntil);

    private async Task<bool> ConsumeTokenAsync(ByteString token, string actionContext, CancellationToken cancellationToken)
    {
        byte[] tokenBytes = CredentialBytes.UnsafeGetBuffer(token);
        try
        {
            return await authCoordinator.TryConsumeActionTokenAsync(tokenBytes, actionContext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CredentialBytes.Zero(tokenBytes);
        }
    }

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);
}

/// <summary>
/// State "đã qua gate <c>view_audit_log</c>" cho ĐÚNG 1 kết nối pipe — audit fix 2026-09-28 (xem class
/// doc <see cref="AuditLogCoordinator"/>). <c>UiSessionServer.RunConnectionAsync</c> tạo 1 instance MỚI
/// mỗi lần 1 kết nối UI bắt đầu, không share/persist qua kết nối khác, tự bị garbage-collect khi kết
/// nối đóng — cố ý KHÔNG đặt state này trong <see cref="AuditLogCoordinator"/> (singleton service-wide)
/// để không lặp lại đúng lớp lỗi vừa sửa.
/// </summary>
public sealed class AuditLogViewSession
{
    public long? GateOpenUntilUnixMs { get; set; }

    /// <summary>Phiên đăng nhập phụ huynh của CÙNG kết nối pipe (`PWD-024`) — null ở test cũ/kết nối không dùng phiên.</summary>
    public UiParentSession? ParentSession { get; init; }
}

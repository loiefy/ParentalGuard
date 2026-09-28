using Google.Protobuf;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Config;

namespace ParentalGuard.Service.Audit;

/// <summary>
/// Orchestrator domain Lịch sử (`10-ui-architecture.md` mục 6.3, `PWD-020`/`MISC-030`) — nhận
/// <c>AuditLogQuery</c>/<c>MarkFalsePositiveRequest</c> từ pipe <c>UI</c>. Việc ghi
/// <c>user_whitelisted_process_names</c> uỷ quyền cho <see cref="ConfigCoordinator.TryAddUserWhitelistEntryAsync"/>
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
public sealed class AuditLogCoordinator(AuthCoordinator authCoordinator, AuditLogWriter auditLog, ConfigCoordinator configCoordinator, MonotonicClock clock)
{
    private const string ViewAuditLogActionContext = "view_audit_log";
    private const string ManageWhitelistActionContext = "manage_whitelist";

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
        IpcPayload.BodyOneofCase.MarkFalsePositiveReq => HandleMarkFalsePositiveAsync(request, cancellationToken),
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
        else if (session.GateOpenUntilUnixMs is not long gateOpenUntil || trustedNow >= gateOpenUntil)
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

    /// <summary>Mục 6.3 — gate `manage_whitelist` (cùng ADR-122 đã áp dụng cho `RemoveWhitelistEntryRequest`).</summary>
    private async Task<IpcPayload> HandleMarkFalsePositiveAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        MarkFalsePositiveRequest req = request.MarkFalsePositiveReq;
        IpcPayload response = NewResponse(request);

        if (!await ConsumeTokenAsync(req.ActionToken, ManageWhitelistActionContext, cancellationToken).ConfigureAwait(false))
        {
            response.MarkFalsePositiveResp = new MarkFalsePositiveResponse { Result = MarkFalsePositiveResult.InvalidToken };
            return response;
        }

        WhitelistAddResult result = await configCoordinator.TryAddUserWhitelistEntryAsync(req.ProcessName, cancellationToken).ConfigureAwait(false);
        switch (result)
        {
            case WhitelistAddResult.AlreadyListed:
                response.MarkFalsePositiveResp = new MarkFalsePositiveResponse { Result = MarkFalsePositiveResult.AlreadyListed };
                return response;
            case WhitelistAddResult.PersistFailed:
                response.MarkFalsePositiveResp = new MarkFalsePositiveResponse { Result = MarkFalsePositiveResult.Unspecified };
                return response;
        }

        await auditLog.AppendAsync(
            "ConfigChanged",
            new { field = "user_whitelisted_process_names", action = "add", process_name = req.ProcessName },
            CancellationToken.None).ConfigureAwait(false);

        response.MarkFalsePositiveResp = new MarkFalsePositiveResponse { Result = MarkFalsePositiveResult.Success };
        return response;
    }

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
}

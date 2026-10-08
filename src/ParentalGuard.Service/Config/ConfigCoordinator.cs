using Google.Protobuf;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Config;

/// <summary>
/// Orchestrator domain Cài đặt (`10-ui-architecture.md` mục 6.4, `FE-012`/`FE-012a`/`MISC-030`/
/// `PERF-050b`) — nhận <c>ConfigQuery</c>/<c>ConfigUpdateRequest</c>/<c>RemoveWhitelistEntryRequest</c>
/// từ pipe <c>UI</c> — nguồn ghi duy nhất cho <c>user_whitelisted_process_names</c>. `MISC-030a` (2026-10-01):
/// không còn đường THÊM whitelist ("Đánh dấu sai" đã bỏ) — chỉ còn xoá qua <c>RemoveWhitelistEntryRequest</c>.
/// </summary>
public sealed class ConfigCoordinator(
    MonitoringStateHolder monitoringStateHolder,
    string configDbPath,
    AuthCoordinator authCoordinator,
    AuditLogWriter auditLog,
    Action pushControlVisionCommand,
    Action<string>? pushOverlayMessage = null,
    Action<string>? pushLanguage = null)
{
    /// <summary>`PAUSE-041`: chế độ "Bảo vệ cả phụ huynh" đang bật — tạm dừng cần vượt thử thách.</summary>
    public bool ParentProtectionEnabled => monitoringStateHolder.Current.ParentProtectionEnabled;

    private const string ManageWhitelistActionContext = "manage_whitelist";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IpcMessageIdGenerator _messageIds = new();

    /// <summary>
    /// Định tuyến theo <see cref="IpcPayload.BodyOneofCase"/> — pipe UI gọi đúng hàm này cho domain Cài đặt (field 148-157).
    /// <paramref name="parentSession"/> là phiên đăng nhập phụ huynh của ĐÚNG kết nối pipe hiện tại (`PWD-024`, ADR-149/150).
    /// </summary>
    public Task<IpcPayload> HandleAsync(IpcPayload request, UiParentSession parentSession, CancellationToken cancellationToken) => request.BodyCase switch
    {
        IpcPayload.BodyOneofCase.ConfigQuery => HandleConfigQueryAsync(request),
        IpcPayload.BodyOneofCase.ConfigUpdateReq => HandleConfigUpdateAsync(request, parentSession, cancellationToken),
        IpcPayload.BodyOneofCase.RemoveWhitelistReq => HandleRemoveWhitelistAsync(request, parentSession, cancellationToken),
        IpcPayload.BodyOneofCase.ResetWhitelistReq => HandleResetWhitelistAsync(request, parentSession, cancellationToken),
        IpcPayload.BodyOneofCase.SetParentProtectionReq => HandleSetParentProtectionAsync(request, parentSession, cancellationToken),
        IpcPayload.BodyOneofCase.SetLanguageReq => HandleSetLanguageAsync(request, cancellationToken),
        _ => throw new InvalidOperationException($"ConfigCoordinator received unexpected message: {request.BodyCase}."),
    };

    /// <summary>Mục 6.4 — không gate, đọc thẳng từ RAM (<see cref="MonitoringStateHolder"/> luôn đồng bộ với lần ghi <c>config.db</c> gần nhất).</summary>
    private Task<IpcPayload> HandleConfigQueryAsync(IpcPayload request)
    {
        MonitoringStateData state = monitoringStateHolder.Current;
        IpcPayload response = NewResponse(request);
        var resp = new ConfigResponse { OverlayMessage = state.OverlayMessage, PerformanceMode = state.PerformanceMode };
        // MISC-030b (ĐÃ CHỐT 2026-10-01): whitelist hiển thị ở S4 = danh sách chủ dự án cấp sẵn trong spec
        // (BE-073a, exclude_process_names) + mục cũ do người dùng thêm trước khi MISC-030a bỏ đường thêm.
        resp.UserWhitelistedProcessNames.AddRange(EffectiveWhitelist(state));
        resp.ParentProtectionEnabled = state.ParentProtectionEnabled;
        resp.Language = state.Language;
        resp.ParentGameMeters = state.ParentGameMeters;
        response.ConfigResp = resp;
        return Task.FromResult(response);
    }

    /// <summary>
    /// Mục 6.4 — "full update": UI luôn gửi đủ cả 2 field hiện hành mỗi lần Save (<c>03-ipc-communication.md</c>
    /// mục 3.7) — Service ghi đè nguyên vẹn, không tự đoán field nào "thực sự đổi". 2026-10-05 (`PWD-024`, ADR-150,
    /// supersedes ADR-125 "không gate"): BẮT BUỘC phiên phụ huynh đang hoạt động trên kết nối này.
    /// </summary>
    private async Task<IpcPayload> HandleConfigUpdateAsync(IpcPayload request, UiParentSession parentSession, CancellationToken cancellationToken)
    {
        ConfigUpdateRequest req = request.ConfigUpdateReq;
        IpcPayload response = NewResponse(request);

        if (!parentSession.TryTouch())
        {
            response.ConfigUpdateResp = new ConfigUpdateResponse { Result = ConfigUpdateResult.NotAuthenticated };
            return response;
        }

        OverlayMessageValidationResult validation = OverlayMessageValidator.Validate(req.OverlayMessage);
        if (validation == OverlayMessageValidationResult.TooLong)
        {
            response.ConfigUpdateResp = new ConfigUpdateResponse { Result = ConfigUpdateResult.TooLong };
            return response;
        }

        if (validation == OverlayMessageValidationResult.InvalidCharacters)
        {
            response.ConfigUpdateResp = new ConfigUpdateResponse { Result = ConfigUpdateResult.InvalidCharacters };
            return response;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MonitoringStateData current = monitoringStateHolder.Current;

            // `03-ipc-communication.md` mục 3.7: PERFORMANCE_MODE_UNSPECIFIED (client lỗi/cũ) → giữ
            // nguyên giá trị hiện có, KHÔNG ghi đè, KHÔNG coi là lỗi cần trả về UI.
            PerformanceMode newPerformanceMode = req.PerformanceMode == PerformanceMode.Unspecified
                ? current.PerformanceMode
                : req.PerformanceMode;

            MonitoringStateData updated = current with { OverlayMessage = req.OverlayMessage, PerformanceMode = newPerformanceMode };
            if (!TryPersist(updated))
            {
                // Cùng mẫu hình PauseCoordinator.HandlePauseAsync — enum đã Approved không có giá trị
                // lỗi nội bộ riêng, dùng Unspecified (giá trị mặc định proto3) thay vì phát minh thêm.
                response.ConfigUpdateResp = new ConfigUpdateResponse { Result = ConfigUpdateResult.Unspecified };
                return response;
            }

            monitoringStateHolder.Update(updated);

            if (current.OverlayMessage != updated.OverlayMessage)
            {
                await auditLog.AppendAsync("ConfigChanged", new { field = "overlay_message" }, CancellationToken.None).ConfigureAwait(false);
                pushOverlayMessage?.Invoke(updated.OverlayMessage); // ADR-110 — Overlay áp dụng cho overlay dựng sau đó
            }

            if (current.PerformanceMode != updated.PerformanceMode)
            {
                await auditLog.AppendAsync("ConfigChanged", new { field = "performance_mode" }, CancellationToken.None).ConfigureAwait(false);
                pushControlVisionCommand();
            }

            response.ConfigUpdateResp = new ConfigUpdateResponse { Result = ConfigUpdateResult.Success };
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mục 6.4 — gate `manage_whitelist` (ADR-122): xoá làm YẾU giám sát, khác các field cosmetic khác ở `S4`.</summary>
    private async Task<IpcPayload> HandleRemoveWhitelistAsync(IpcPayload request, UiParentSession parentSession, CancellationToken cancellationToken)
    {
        RemoveWhitelistEntryRequest req = request.RemoveWhitelistReq;
        IpcPayload response = NewResponse(request);

        if (!await IsAuthorizedAsync(req.ActionToken, parentSession, cancellationToken).ConfigureAwait(false))
        {
            response.RemoveWhitelistResp = new RemoveWhitelistEntryResponse { Result = RemoveWhitelistEntryResult.InvalidToken };
            return response;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // MISC-030b: xoá được cả mục thuộc danh sách cấp sẵn (BE-073a) lẫn mục cũ người dùng thêm — xoá xong
            // ứng dụng đó quay lại bị giám sát. Không có đường THÊM nào (MISC-030a).
            MonitoringStateData current = monitoringStateHolder.Current;
            List<string> remainingUser = current.UserWhitelistedProcessNames
                .Where(name => !string.Equals(name, req.ProcessName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            List<string> remainingExclude = current.ExcludeProcessNames
                .Where(name => !string.Equals(name, req.ProcessName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (remainingUser.Count == current.UserWhitelistedProcessNames.Count && remainingExclude.Count == current.ExcludeProcessNames.Count)
            {
                response.RemoveWhitelistResp = new RemoveWhitelistEntryResponse { Result = RemoveWhitelistEntryResult.NotFound };
                return response;
            }

            MonitoringStateData updated = current with { UserWhitelistedProcessNames = remainingUser, ExcludeProcessNames = remainingExclude };
            if (!TryPersist(updated))
            {
                response.RemoveWhitelistResp = new RemoveWhitelistEntryResponse { Result = RemoveWhitelistEntryResult.Unspecified };
                return response;
            }

            monitoringStateHolder.Update(updated);
            pushControlVisionCommand();

            await auditLog.AppendAsync(
                "ConfigChanged",
                new { field = "whitelist", action = "remove", process_name = req.ProcessName },
                CancellationToken.None).ConfigureAwait(false);

            response.RemoveWhitelistResp = new RemoveWhitelistEntryResponse { Result = RemoveWhitelistEntryResult.Success };
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// `MISC-030c` (ĐÃ CHỐT 2026-10-01): "Khôi phục cài đặt gốc" — whitelist về ĐÚNG danh sách cấp sẵn trong spec
    /// (`BE-073a`, <see cref="MonitoringStateData.InitialExcludeProcessNames"/>), bỏ mọi mục cũ người dùng thêm.
    /// Gate `manage_whitelist`: thao tác này có thể THÊM lại ứng dụng không bị giám sát.
    /// </summary>
    private async Task<IpcPayload> HandleResetWhitelistAsync(IpcPayload request, UiParentSession parentSession, CancellationToken cancellationToken)
    {
        IpcPayload response = NewResponse(request);
        if (!await IsAuthorizedAsync(request.ResetWhitelistReq.ActionToken, parentSession, cancellationToken).ConfigureAwait(false))
        {
            response.ResetWhitelistResp = new ResetWhitelistResponse { Result = ResetWhitelistResult.InvalidToken };
            return response;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MonitoringStateData current = monitoringStateHolder.Current;
            MonitoringStateData updated = current with
            {
                ExcludeProcessNames = [.. MonitoringStateData.InitialExcludeProcessNames],
                UserWhitelistedProcessNames = [],
            };
            if (!TryPersist(updated))
            {
                response.ResetWhitelistResp = new ResetWhitelistResponse { Result = ResetWhitelistResult.Unspecified };
                return response;
            }

            monitoringStateHolder.Update(updated);
            pushControlVisionCommand();
            await auditLog.AppendAsync("ConfigChanged", new { field = "whitelist", action = "reset" }, CancellationToken.None).ConfigureAwait(false);

            var resp = new ResetWhitelistResponse { Result = ResetWhitelistResult.Success };
            resp.Whitelist.AddRange(EffectiveWhitelist(updated));
            response.ResetWhitelistResp = resp;
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// `PAUSE-040`/`PAUSE-047` (2026-10-08): cần phiên phụ huynh. Chỉ BẬT chế độ / TĂNG quãng đường được đổi trực tiếp; TẮT hoặc GIẢM
    /// quãng đường làm giảm bảo vệ → <c>CHALLENGE_REQUIRED</c>, phải về đích trò chơi nhảy rào (<see cref="Auth.ParentGameCoordinator"/>).
    /// </summary>
    private async Task<IpcPayload> HandleSetParentProtectionAsync(IpcPayload request, UiParentSession parentSession, CancellationToken cancellationToken)
    {
        SetParentProtectionRequest req = request.SetParentProtectionReq;
        IpcPayload response = NewResponse(request);
        if (!parentSession.TryTouch())
        {
            response.SetParentProtectionResp = new SetParentProtectionResponse { Result = SetParentProtectionResult.NotAuthenticated };
            return response;
        }

        if (req.ParentGameMeters != 0 && !Auth.ParentGameCoordinator.AllowedMeters.Contains(req.ParentGameMeters))
        {
            response.SetParentProtectionResp = new SetParentProtectionResponse { Result = SetParentProtectionResult.Unspecified };
            return response;
        }

        MonitoringStateData current = monitoringStateHolder.Current;
        if (current.ParentProtectionEnabled && Auth.ParentGameCoordinator.NeedsGame(req.Enabled, req.ParentGameMeters, current.ParentGameMeters))
        {
            response.SetParentProtectionResp = new SetParentProtectionResponse { Result = SetParentProtectionResult.ChallengeRequired };
            return response;
        }

        bool saved = await ApplyParentProtectionAsync(req.Enabled, req.ParentGameMeters, cancellationToken).ConfigureAwait(false);
        response.SetParentProtectionResp = new SetParentProtectionResponse { Result = saved ? SetParentProtectionResult.Success : SetParentProtectionResult.Unspecified };
        return response;
    }

    /// <summary>`PAUSE-045`: quãng đường hiện hành của trò chơi nhảy rào.</summary>
    public uint ParentGameMeters => monitoringStateHolder.Current.ParentGameMeters;

    /// <summary>
    /// Ghi chế độ + quãng đường (0 = giữ nguyên) — gọi sau khi đã kiểm tra quyền (trực tiếp ở trên, hoặc về đích trò chơi).
    /// </summary>
    public async Task<bool> ApplyParentProtectionAsync(bool enabled, uint gameMeters, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MonitoringStateData current = monitoringStateHolder.Current;
            uint meters = gameMeters == 0 ? current.ParentGameMeters : gameMeters;
            if (current.ParentProtectionEnabled == enabled && current.ParentGameMeters == meters)
            {
                return true;
            }

            MonitoringStateData updated = current with { ParentProtectionEnabled = enabled, ParentGameMeters = meters };
            if (!TryPersist(updated))
            {
                return false;
            }

            monitoringStateHolder.Update(updated);
            await auditLog.AppendAsync("ConfigChanged", new { field = "parent_protection", enabled, game_meters = meters }, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>`FE-063a`/`FE-083` (2026-10-07): đổi ngôn ngữ KHÔNG cần đăng nhập — chỉ chấp nhận 6 mã hỗ trợ, đẩy xuống Overlay.</summary>
    private async Task<IpcPayload> HandleSetLanguageAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        string language = request.SetLanguageReq.Language;
        IpcPayload response = NewResponse(request);
        if (!MonitoringStateData.SupportedLanguages.Contains(language))
        {
            response.SetLanguageResp = new SetLanguageResponse { Accepted = false };
            return response;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MonitoringStateData current = monitoringStateHolder.Current;
            if (current.Language != language)
            {
                MonitoringStateData updated = current with { Language = language };
                if (!TryPersist(updated))
                {
                    response.SetLanguageResp = new SetLanguageResponse { Accepted = false };
                    return response;
                }

                monitoringStateHolder.Update(updated);
                pushLanguage?.Invoke(language);
                await auditLog.AppendAsync("ConfigChanged", new { field = "language", value = language }, CancellationToken.None).ConfigureAwait(false);
            }

            response.SetLanguageResp = new SetLanguageResponse { Accepted = true };
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Hợp (không trùng, không phân biệt hoa thường) của danh sách cấp sẵn BE-073a và mục cũ người dùng thêm.</summary>
    internal static IReadOnlyList<string> EffectiveWhitelist(MonitoringStateData state) =>
        [.. state.ExcludeProcessNames.Concat(state.UserWhitelistedProcessNames).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// ADR-150: phiên phụ huynh đang hoạt động (gia hạn idle) HOẶC <c>action_token</c> 1-lần "manage_whitelist" (giữ
    /// tương thích). Token luôn bị zero dù đi nhánh nào.
    /// </summary>
    private async Task<bool> IsAuthorizedAsync(ByteString token, UiParentSession parentSession, CancellationToken cancellationToken)
    {
        byte[] tokenBytes = CredentialBytes.UnsafeGetBuffer(token);
        try
        {
            if (parentSession.TryTouch())
            {
                return true;
            }

            return tokenBytes.Length > 0
                && await authCoordinator.TryConsumeActionTokenAsync(tokenBytes, ManageWhitelistActionContext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CredentialBytes.Zero(tokenBytes);
        }
    }

    private bool TryPersist(MonitoringStateData state)
    {
        try
        {
            using ConfigDb db = ConfigDb.Open(configDbPath);
            db.UpdateMonitoringState(state);
            return true;
        }
        catch (ConfigLoadException)
        {
            return false;
        }
    }

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);
}

using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Nguồn sự thật duy nhất cho danh sách overlay đang active (Architecture/02-process-architecture.md
/// mục 5 điểm 1, ADR-12): nhận <c>VisionInferenceResult</c> từ kênh <c>Vision</c>, so ngưỡng
/// (`IMG-013`/`BE-090`), rồi đẩy <c>OverlayRectListCommand</c> xuống kênh <c>Overlay</c> — Overlay
/// chỉ vẽ theo danh sách này, không tự quyết định gì (`BE-021`, `BE-030`+).
///
/// Thiết kế đã CHỐT (`BE-032`, Architecture/02-process-architecture.md mục 2.4, v0.1.1 — không còn
/// là khoảng trống đang treo): vì Session 0 Isolation (`BE-023a`) khiến `Service` không thể gọi API
/// `user32` lên 1 HWND thuộc window station của session tương tác, `Overlay` (đã ở đúng session đó)
/// tự `PostMessage(WM_CLOSE)` lên window handle vi phạm cục bộ khi user bấm nút "Tắt nội dung", đồng
/// thời gửi <c>ForceCloseRequest</c> lên đây chỉ để `Service` cập nhật lại danh sách overlay đang
/// active + ghi audit log — `Service` giữ đúng vai trò "nguồn sự thật duy nhất" cho *state*, không
/// tự thực thi hành động OS đóng cửa sổ.
/// </summary>
/// <remarks>
/// `BE-034` (ĐÃ CHỐT 2026-09-30): overlay KHOÁ CỨNG kể từ lúc hiển thị — kết quả nhận diện sau đó cho
/// cùng cửa sổ bị bỏ qua hoàn toàn (không gỡ vì điểm thấp: Desktop Duplication chụp cả overlay của
/// chính hệ thống, gỡ theo điểm thấp gây nhấp nháy vô hạn). Chỉ gỡ qua <see cref="HandleForceCloseAsync"/>
/// (bấm nút / auto-timeout / cửa sổ biến mất) hoặc <see cref="ClearForPause"/>. Mỗi lần danh sách cửa
/// sổ đang bị che đổi, gọi <paramref name="onCoveredWindowsChanged"/> để đẩy lại <c>ControlVisionCommand</c>
/// (`BE-034b` — Vision bỏ qua các cửa sổ này).
/// </remarks>
public sealed class OverlayDecisionCoordinator(
    ChildProcessSupervisor overlaySupervisor,
    AuditLogWriter auditLog,
    Func<float> currentRiskThreshold,
    Action? onCoveredWindowsChanged = null,
    Func<int, ProcessFacts?>? lookupProcess = null,
    Action<int>? killProcess = null,
    Func<DateTimeOffset>? utcNow = null)
{
    private readonly object _sync = new();
    private readonly Dictionary<ulong, OverlayRect> _active = [];

    // BE-034d: tên tiến trình Vision ghi nhận cho cửa sổ đang bị che, và các lần bấm "Tắt nội dung" gần đây.
    private readonly Dictionary<ulong, string> _processNameByWindow = [];
    private readonly Dictionary<ulong, (string ProcessName, DateTimeOffset At)> _recentManualClose = [];
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<uint, uint> _mergedOverlayIdByMonitor = [];
    private uint _nextOverlayId = 1;
    private uint _nextMergedOverlayId = 1;
    private bool _mergedModeActive;

    /// <summary>Dùng làm <c>configureInitialPush</c> khi Overlay (re)connect — gửi lại state hiện hành (fail-secure).</summary>
    public void ConfigureInitialPush(IpcPayload payload) => payload.OverlayRects = BuildCommand();

    /// <summary>`BE-034b`: snapshot cửa sổ đang bị overlay che — gửi xuống Vision trong <c>ControlVisionCommand.covered_window_handles</c>.</summary>
    public IReadOnlyList<ulong> CoveredWindowHandles
    {
        get
        {
            lock (_sync)
            {
                return [.. _active.Keys];
            }
        }
    }

    public async Task HandleVisionResultAsync(IpcPayload message, CancellationToken cancellationToken)
    {
        VisionInferenceResult result = message.VisionResult;
        bool violates = OverlayThresholdDecision.Violates(result.RiskScore, currentRiskThreshold());
        lock (_sync)
        {
            // BE-034: đã che → bỏ qua MỌI kết quả tới sau (kể cả kết quả đang bay trên IPC trước khi
            // Vision nhận danh sách covered mới, BE-034b). Vị trí/kích thước do Overlay tự bám theo
            // cửa sổ qua WinEventHook (BE-031), không cần Vision cập nhật rect nữa.
            if (!violates || _active.ContainsKey(result.WindowHandle))
            {
                return;
            }

            // ADR-137 (Architecture/04 mục 5.1): edge-triggered — mỗi lượt block đúng 1 record ContentBlocked.
            _processNameByWindow[result.WindowHandle] = result.ProcessName;
            _active[result.WindowHandle] = new OverlayRect
            {
                WindowHandle = result.WindowHandle,
                Rect = result.Bbox,
                MonitorId = result.MonitorId,
                OverlayId = _nextOverlayId++,
                Reason = OverlayReason.ContentViolation,
            };
        }

        PushCurrentList();
        onCoveredWindowsChanged?.Invoke();

        await auditLog.AppendAsync(
            "ContentBlocked",
            new
            {
                windowHandle = result.WindowHandle,
                processName = result.ProcessName,
                riskScore = result.RiskScore,
                bbox = new { x = result.Bbox?.X ?? 0, y = result.Bbox?.Y ?? 0, width = result.Bbox?.Width ?? 0, height = result.Bbox?.Height ?? 0 },
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task HandleForceCloseAsync(IpcPayload message, CancellationToken cancellationToken)
    {
        ForceCloseRequest request = message.ForceClose;
        bool removed;
        lock (_sync)
        {
            removed = _active.Remove(request.WindowHandle);
            if (_processNameByWindow.Remove(request.WindowHandle, out string? processName) && request.Source == CloseSource.Manual && !string.IsNullOrEmpty(processName))
            {
                _recentManualClose[request.WindowHandle] = (processName, _utcNow());
            }
        }

        if (!removed)
        {
            return;
        }

        PushCurrentList();
        onCoveredWindowsChanged?.Invoke();

        if (request.Source == CloseSource.WindowGone)
        {
            // BE-034 điều kiện (3): cửa sổ đã tự biến mất (vd đóng bằng nút X gốc, FE-016) — chỉ giải
            // phóng state, KHÔNG phải force-close nên không ghi ForceCloseRequested.
            return;
        }

        // BE-089b: Overlay LUÔN set Source tường minh (MANUAL/AUTO_TIMEOUT) — không tự suy luận
        // nguồn từ dữ liệu khác (Architecture/04 mục 5.1).
        string source = CloseSourceMapper.ToAuditLogValue(request.Source);
        await auditLog.AppendAsync(
            "ForceCloseRequested",
            new { windowHandle = request.WindowHandle, overlayId = request.OverlayId, source },
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// `BE-034d` (2026-10-07): Overlay báo cửa sổ vẫn còn 3 giây sau "Tắt nội dung" → kết thúc tiến trình sở hữu nó, chỉ khi
    /// <see cref="ForceKillPolicy.Decide"/> cho phép (đúng tiến trình Vision đã thấy, phiên người dùng, không phải tiến trình hệ
    /// thống/ParentalGuard, trong 30 giây sau lần bấm). Mọi kết quả đều ghi audit log.
    /// </summary>
    public async Task HandleForceKillAsync(IpcPayload message, CancellationToken cancellationToken)
    {
        ForceKillRequest request = message.ForceKill;
        (string ProcessName, DateTimeOffset At)? recent;
        lock (_sync)
        {
            recent = _recentManualClose.Remove(request.WindowHandle, out var entry) ? entry : null;
            foreach (ulong stale in _recentManualClose.Where(kv => _utcNow() - kv.Value.At > ForceKillPolicy.RequestWindow).Select(kv => kv.Key).ToList())
            {
                _recentManualClose.Remove(stale);
            }
        }

        ProcessFacts? facts = (lookupProcess ?? LookupProcess)((int)request.ProcessId);
        ForceKillDecision decision = ForceKillPolicy.Decide(recent?.ProcessName, recent is null ? TimeSpan.MaxValue : _utcNow() - recent.Value.At, facts);
        bool killed = false;
        if (decision == ForceKillDecision.Allowed)
        {
            try
            {
                (killProcess ?? KillProcess)((int)request.ProcessId);
                killed = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException)
            {
                // Tiến trình đã tự thoát giữa chừng hoặc không đủ quyền — ghi nhận, không làm gì thêm.
            }
        }

        await auditLog.AppendAsync(
            killed ? "ForceKillExecuted" : "ForceKillRefused",
            new { windowHandle = request.WindowHandle, processName = facts?.ExecutableName ?? recent?.ProcessName ?? "", decision = decision.ToString() },
            CancellationToken.None).ConfigureAwait(false);
    }

    private static ProcessFacts? LookupProcess(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return new ProcessFacts(pid, process.SessionId, process.ProcessName + ".exe");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void KillProcess(int pid)
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        process.Kill(entireProcessTree: false);
    }

    /// <summary>
    /// ADR-106 (Architecture/02 mục 3a.1 bước 7, `PAUSE-002b`): giải phóng TOÀN BỘ overlay đang che
    /// ngay lúc chuyển <c>Running·Paused</c> — xoá cả state nội bộ (không chỉ gửi 1 lần danh sách
    /// rỗng), để nếu Overlay reconnect trong lúc đang Pause cũng nhận đúng danh sách rỗng thay vì
    /// state cũ trước lúc Pause (khác nguyên tắc "giữ nguyên state khi mất kết nối" áp dụng cho
    /// crash-restart — Pause hợp lệ là quyết định nghiệp vụ chủ động, không phải gián đoạn kênh
    /// IPC, nên không thuộc phạm vi fail-secure "gián đoạn kênh điều khiển không được hiểu là đã
    /// hết vi phạm" ở `03-ipc-communication.md` mục 6).
    /// </summary>
    public void ClearForPause()
    {
        lock (_sync)
        {
            _active.Clear();
            _mergedModeActive = false;
            _mergedOverlayIdByMonitor.Clear();
        }

        PushCurrentList();
        onCoveredWindowsChanged?.Invoke();
    }

    private void PushCurrentList() => overlaySupervisor.TryEnqueueBusinessMessage(payload => payload.OverlayRects = BuildCommand());

    /// <summary>
    /// `BE-088`/`089` (Architecture/07-overlay-architecture.md mục 3.2, ADR-57): ngưỡng hysteresis
    /// vào/ra chế độ gộp áp dụng lại ở MỌI lần build — không có nhánh code riêng cho việc "thoát
    /// chế độ gộp", nó là hệ quả tự nhiên của ngưỡng áp dụng lại mỗi lần.
    /// </summary>
    private OverlayRectListCommand BuildCommand()
    {
        var command = new OverlayRectListCommand { GeneratedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        lock (_sync)
        {
            _mergedModeActive = OverlayMergeThreshold.ShouldBeMerged(_mergedModeActive, _active.Count);
            if (!_mergedModeActive)
            {
                command.Rects.AddRange(_active.Values);
                return command;
            }

            var allHandles = _active.Keys.ToList();
            foreach (IGrouping<uint, OverlayRect> group in _active.Values.GroupBy(r => r.MonitorId))
            {
                OverlayRect representative = group.OrderBy(r => r.OverlayId).First();
                var merged = new OverlayRect
                {
                    WindowHandle = representative.WindowHandle,
                    MonitorId = group.Key,
                    OverlayId = StableMergedOverlayId(group.Key),
                    Reason = OverlayReason.ContentViolation,
                    IsMerged = true,
                };
                merged.MergedWindowHandles.AddRange(allHandles);
                command.Rects.Add(merged);
            }

            return command;
        }
    }

    /// <summary>Namespace riêng khỏi <see cref="_nextOverlayId"/> của overlay đơn (mục 3.2) — phải gọi trong lock <see cref="_sync"/>.</summary>
    private uint StableMergedOverlayId(uint monitorId)
    {
        if (_mergedOverlayIdByMonitor.TryGetValue(monitorId, out uint existing))
        {
            return existing;
        }

        uint id = _nextMergedOverlayId++;
        _mergedOverlayIdByMonitor[monitorId] = id;
        return id;
    }
}

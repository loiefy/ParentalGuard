using System.Windows.Forms;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Overlay.Icons;
using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Rendering;

/// <summary>
/// Form ẩn giữ đúng 1 handle Windows message loop cho toàn bộ Overlay (`BE-030`) — mọi thao tác
/// với các <see cref="ContentBlurOverlayForm"/> phải chạy trên thread UI này qua <see cref="Control.Invoke(Delegate)"/>,
/// vì message nghiệp vụ tới từ Thread IPC (khác thread). Đợt 2 (Architecture/07-overlay-architecture.md
/// mục 3/4): thêm chế độ gộp (`BE-088`/`089`), z-index cục bộ (`BE-087`), và điều phối icon trạng
/// thái multi-monitor (`FE-020`-`022`) qua <see cref="StatusIconManager"/>.
/// </summary>
public sealed class OverlayCoordinator : Form
{
    private const int _wmDisplayChange = 0x007E;
    private const int _debounceMs = 50;
    private const int _windowGoneCheckMs = 1000;

    private readonly Dictionary<ulong, ContentBlurOverlayForm> _overlays = [];
    private readonly Action<ForceCloseRequest> _sendForceClose;
    private readonly Action<IconPositionUpdate> _sendIconPosition;
    private readonly HashSet<IntPtr> _pendingLocationChangeHandles = [];
    private readonly StatusIconManager _iconManager;
    private readonly Action _requestOpenDashboard;

    private IDisposable? _winEventRegistration;
    private System.Windows.Forms.Timer? _debounceTimer;
    private readonly System.Windows.Forms.Timer _windowGoneTimer;
    private readonly HashSet<ulong> _reportedGoneHandles = [];
    private bool _pendingZOrderResync;
    private string _blockedMessage = string.Empty; // ADR-110: OverlayMessageUpdate gần nhất (RAM)

    /// <summary>`BE-034d`: chờ ngần này sau "Tắt nội dung" rồi mới xin Service buộc đóng nếu cửa sổ vẫn còn.</summary>
    internal static readonly TimeSpan ForceKillDelay = TimeSpan.FromSeconds(3);

    private readonly Action<ForceKillRequest>? _sendForceKill;

    public OverlayCoordinator(Action<ForceCloseRequest> sendForceClose, Action<IconPositionUpdate> sendIconPosition, Action requestOpenDashboard, Action<ForceKillRequest>? sendForceKill = null)
    {
        _sendForceClose = sendForceClose;
        _sendForceKill = sendForceKill;
        _sendIconPosition = sendIconPosition;
        _requestOpenDashboard = requestOpenDashboard;
        _iconManager = new StatusIconManager(sendIconPosition, requestOpenDashboard);

        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        Size = new System.Drawing.Size(1, 1);

        // Mục 2.6/3.5: 1 WinEventHook dùng chung cho rect real-time (FE-016b) và z-order (BE-087).
        _winEventRegistration = WinEventHookInterop.Register(
            [WinEventHookInterop.EventObjectLocationChange, WinEventHookInterop.EventSystemForeground, WinEventHookInterop.EventObjectReorder],
            OnWinEvent);

        // BE-034 điều kiện (3): overlay khoá cứng, Service không còn gỡ theo điểm thấp — Overlay phải tự
        // phát hiện cửa sổ vi phạm đã biến mất (vd đóng bằng nút X gốc chừa ra theo FE-016), nếu không
        // overlay sẽ treo che 1 vùng trống tới hết timeout.
        _windowGoneTimer = new System.Windows.Forms.Timer { Interval = _windowGoneCheckMs };
        _windowGoneTimer.Tick += (_, _) => CheckForGoneWindows();
        _windowGoneTimer.Start();
    }

    /// <summary>`Architecture/02` mục 5 điểm 1: Overlay chỉ vẽ đúng danh sách này, không tự quyết định gì thêm.</summary>
    public void ApplyOverlayList(OverlayRectListCommand command)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyOverlayList(command));
            return;
        }

        IReadOnlyList<OverlayRect> desired = RegroupMergedByRealMonitor(command.Rects);
        var incomingHandles = desired.Select(r => r.WindowHandle).ToHashSet();
        foreach (ulong staleHandle in _overlays.Keys.Where(h => !incomingHandles.Contains(h)).ToList())
        {
            RemoveOverlay(staleHandle);
        }

        foreach (OverlayRect rect in desired)
        {
            if (_overlays.TryGetValue(rect.WindowHandle, out ContentBlurOverlayForm? existing))
            {
                bool mergedSetChanged = rect.IsMerged && !existing.CoveredWindowHandles.ToHashSet().SetEquals(rect.MergedWindowHandles);
                if (existing.IsMerged != rect.IsMerged || mergedSetChanged)
                {
                    // Mục 3.3: cấu trúc control khác nhau giữa 2 chế độ (có/không vùng loại trừ) — xoá + tạo lại, không tái dùng ApplyRect tại chỗ.
                    RemoveOverlay(rect.WindowHandle);
                    CreateAndShow(rect);
                }
                else
                {
                    existing.ApplyRect(rect);
                }
            }
            else
            {
                CreateAndShow(rect);
            }
        }

        ResyncZOrder();
    }

    /// <summary>
    /// Bug real-hardware (2026-10-01): form điều phối này KHÔNG bao giờ Show() (Program chỉ ép tạo
    /// Handle), nên sự kiện <c>Load</c> — nơi duy nhất từng gọi <c>_iconManager.Start()</c> — không bao giờ
    /// chạy → icon trạng thái FE-020 không xuất hiện. Khởi động icon ngay khi có handle; BeginInvoke để
    /// chạy khi message loop (<c>Application.Run</c>) đã bơm.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(_iconManager.Start);
    }

#if PARENTALGUARD_DEVELOPER_MODE
    private DeveloperOverlay? _developerOverlay;

    /// <summary>DEV-051: chế độ developer — viền + % rủi ro quanh cửa sổ Vision vừa phân tích (chỉ có ở bản build dev).</summary>
    public void ApplyDebugWindowScore(DebugWindowScore score)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyDebugWindowScore(score));
            return;
        }

        (_developerOverlay ??= new DeveloperOverlay()).Apply(score);
    }
#endif

    /// <summary>ADR-110 (07 mục 4.4): áp dụng cho overlay dựng SAU đó, không dựng lại overlay đang hiển thị.</summary>
    public void ApplyOverlayMessage(OverlayMessageUpdate update)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyOverlayMessage(update));
            return;
        }

        _blockedMessage = update.Text;
    }

    /// <summary>
    /// `BE-089c` (ĐÃ CHỐT 2026-10-01): mỗi màn hình đúng 1 overlay full-screen riêng, chỉ chứa cửa sổ vi phạm của
    /// CHÍNH màn hình đó. Bug real-hardware: Service nhóm theo <c>monitor_id</c> = chỉ số output DXGI THEO TỪNG
    /// card đồ hoạ — máy laptop + màn hình ngoài khác adapter có thể cùng chỉ số 0 → cửa sổ của 2 màn hình bị gộp
    /// chung 1 overlay. Overlay (đã ở đúng session, ADR-58) tự nhóm lại theo màn hình thật qua MonitorFromWindow.
    /// Rect thường (không gộp) giữ nguyên.
    /// </summary>
    internal static IReadOnlyList<OverlayRect> RegroupMergedByRealMonitor(IEnumerable<OverlayRect> rects, Func<ulong, string>? monitorKeyOf = null)
    {
        monitorKeyOf ??= h => MonitorInterop.GetMonitorInfoForWindow(new IntPtr(unchecked((long)h)))?.DeviceName ?? string.Empty;
        var result = new List<OverlayRect>();
        var merged = new List<OverlayRect>();
        foreach (OverlayRect rect in rects)
        {
            (rect.IsMerged ? merged : result).Add(rect);
        }

        if (merged.Count == 0)
        {
            return result;
        }

        uint overlayId = merged.Min(r => r.OverlayId);
        IEnumerable<ulong> allHandles = merged.SelectMany(r => r.MergedWindowHandles).Concat(merged.Select(r => r.WindowHandle)).Distinct();
        foreach (IGrouping<string, ulong> group in allHandles.GroupBy(monitorKeyOf).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            List<ulong> handles = [.. group.OrderBy(h => h)];
            var perMonitor = new OverlayRect
            {
                WindowHandle = handles[0], // đại diện: Overlay tự resolve bounds màn hình từ handle này
                OverlayId = overlayId,
                Reason = OverlayReason.ContentViolation,
                IsMerged = true,
            };
            perMonitor.MergedWindowHandles.AddRange(handles);
            result.Add(perMonitor);
        }

        return result;
    }

    public void ApplyMonitoringStatus(MonitoringStatusUpdate status)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyMonitoringStatus(status));
            return;
        }

        _iconManager.ApplyStatus(status);
    }

    public void ApplyIconLayoutSync(IconLayoutSync sync)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyIconLayoutSync(sync));
            return;
        }

        _iconManager.ApplyLayoutSync(sync);
    }

    /// <summary>ADR-66: pipe Overlay↔Service đứt — không chờ push, tự chuyển local toàn bộ icon sang ERROR.</summary>
    public void ApplyDisconnected()
    {
        if (InvokeRequired)
        {
            Invoke(ApplyDisconnected);
            return;
        }

        _iconManager.ApplyDisconnected();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == _wmDisplayChange)
        {
            // Form đã nhận WM_DISPLAYCHANGE qua WndProc chuẩn (không cần đăng ký thêm, mục 4.1.1) — hot-plug icon.
            _iconManager.RefreshMonitors();
        }
    }

    private void CreateAndShow(OverlayRect rect)
    {
        var form = new ContentBlurOverlayForm(rect, HandleCloseButtonClicked, HandleMergedCloseTriggered, _blockedMessage, _requestOpenDashboard);
        _overlays[rect.WindowHandle] = form;
        form.Show();
    }

    private void HandleCloseButtonClicked(ulong windowHandle, uint overlayId, CloseSource source)
    {
        if (source == CloseSource.Manual)
        {
            ScheduleForceKill([windowHandle]);
        }

        _sendForceClose(new ForceCloseRequest
        {
            WindowHandle = windowHandle,
            OverlayId = overlayId,
            ClickedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Source = source,
        });
        RemoveOverlay(windowHandle);
    }

    /// <summary>
    /// Báo <c>WINDOW_GONE</c> đúng 1 lần cho mỗi handle đã biến mất. Overlay thường: gỡ ngay tại chỗ.
    /// Overlay gộp: chờ Service gửi danh sách mới (các handle còn lại vẫn đang vi phạm).
    /// </summary>
    private void CheckForGoneWindows()
    {
        foreach ((ulong key, ContentBlurOverlayForm form) in _overlays.ToList())
        {
            foreach (ulong handle in form.CoveredWindowHandles)
            {
                if (OverlayWindowInterop.WindowExists(handle) || !_reportedGoneHandles.Add(handle))
                {
                    continue;
                }

                _sendForceClose(new ForceCloseRequest
                {
                    WindowHandle = handle,
                    OverlayId = form.OverlayId,
                    ClickedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Source = CloseSource.WindowGone,
                });
            }

            if (!form.IsMerged && _reportedGoneHandles.Contains(key))
            {
                RemoveOverlay(key);
            }
        }

        // HWND có thể được OS tái sử dụng — chỉ giữ dấu "đã báo" cho handle còn đang có overlay.
        var live = _overlays.Values.SelectMany(f => f.CoveredWindowHandles).ToHashSet();
        _reportedGoneHandles.RemoveWhere(h => !live.Contains(h));
    }

    /// <summary>`BE-089`/`BE-089b`: 1 <c>ForceCloseRequest</c> riêng cho mỗi handle trong batch, cùng <paramref name="overlayId"/> và <paramref name="source"/> (mục 3.3).</summary>
    private void HandleMergedCloseTriggered(uint overlayId, IReadOnlyList<ulong> mergedWindowHandles, CloseSource source)
    {
        if (source == CloseSource.Manual)
        {
            ScheduleForceKill(mergedWindowHandles);
        }

        long clickedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (ulong handle in mergedWindowHandles)
        {
            _sendForceClose(new ForceCloseRequest
            {
                WindowHandle = handle,
                OverlayId = overlayId,
                ClickedAtUnixMs = clickedAt,
                Source = source,
            });
        }

        RemoveMergedOverlay(mergedWindowHandles);
    }

    /// <summary>
    /// `BE-034d` (2026-10-07): ghi lại PID chủ cửa sổ NGAY lúc bấm (cửa sổ còn sống), 3 giây sau cửa sổ vẫn còn (ứng dụng bỏ
    /// qua <c>WM_CLOSE</c>/đang hỏi "Lưu thay đổi?") và vẫn cùng PID → xin Service buộc đóng. Auto-timeout không đi đường này.
    /// </summary>
    private void ScheduleForceKill(IReadOnlyList<ulong> windowHandles)
    {
        if (_sendForceKill is null)
        {
            return;
        }

        var targets = windowHandles.Select(h => (Handle: h, Pid: OverlayWindowInterop.GetOwningProcessId(h))).Where(t => t.Pid != 0).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var timer = new System.Windows.Forms.Timer { Interval = (int)ForceKillDelay.TotalMilliseconds };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            foreach ((ulong handle, uint pid) in targets)
            {
                if (OverlayWindowInterop.WindowExists(handle) && OverlayWindowInterop.GetOwningProcessId(handle) == pid)
                {
                    _sendForceKill(new ForceKillRequest { WindowHandle = handle, ProcessId = pid });
                }
            }
        };
        timer.Start();
    }

    private void RemoveOverlay(ulong windowHandle)
    {
        if (_overlays.Remove(windowHandle, out ContentBlurOverlayForm? form))
        {
            form.Close();
            form.Dispose();
        }
    }

    /// <summary>Overlay gộp keyed theo handle đại diện — nhiều màn hình có thể cùng overlay_id (`BE-089c`), nên tìm đúng form theo chính danh sách handle của nó.</summary>
    private void RemoveMergedOverlay(IReadOnlyList<ulong> mergedWindowHandles)
    {
        ulong? key = _overlays.Where(kv => kv.Value.IsMerged && ReferenceEquals(kv.Value.CoveredWindowHandles, mergedWindowHandles)).Select(kv => (ulong?)kv.Key).FirstOrDefault();
        if (key is { } handle)
        {
            RemoveOverlay(handle);
        }
    }

    private void OnWinEvent(IntPtr hwnd, uint eventType)
    {
        ulong handle = unchecked((ulong)hwnd.ToInt64());
        if (!_overlays.ContainsKey(handle))
        {
            // Bug 2026-10-06: cửa sổ KHÁC (vd Dashboard) được đưa lên trên/kéo đè lên cửa sổ vi phạm → tính lại xem overlay
            // có phải hạ xuống không (ZOrderSync.ComputeDemoted). Đổi foreground xử lý NGAY (không debounce) để overlay
            // không kịp đè lên cửa sổ mới hiện lên; mọi sự kiện khác của cửa sổ không theo dõi vẫn bỏ qua (mục 2.6/3.5).
            if (_overlays.Count == 0)
            {
                return;
            }

            if (eventType == WinEventHookInterop.EventSystemForeground)
            {
                _pendingZOrderResync = true;
                FlushWinEvents();
            }
            else if (eventType == WinEventHookInterop.EventObjectLocationChange && hwnd == GetForegroundWindow())
            {
                _pendingZOrderResync = true;
                RestartDebounceTimer();
            }

            return;
        }

        if (eventType == WinEventHookInterop.EventObjectLocationChange)
        {
            _pendingLocationChangeHandles.Add(hwnd);
        }

        _pendingZOrderResync = true;
        RestartDebounceTimer();
    }

    private void RestartDebounceTimer()
    {
        _debounceTimer ??= CreateDebounceTimer();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private System.Windows.Forms.Timer CreateDebounceTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = _debounceMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FlushWinEvents();
        };
        return timer;
    }

    private void FlushWinEvents()
    {
        foreach (IntPtr hwnd in _pendingLocationChangeHandles)
        {
            ulong handle = unchecked((ulong)hwnd.ToInt64());
            if (_overlays.TryGetValue(handle, out ContentBlurOverlayForm? form))
            {
                form.OnTrackedWindowLocationChanged();
            }
        }

        _pendingLocationChangeHandles.Clear();

        if (_pendingZOrderResync)
        {
            _pendingZOrderResync = false;
            ResyncZOrder();
        }
    }

    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>`BE-087`: z-index cục bộ — ADR-56.</summary>
    private void ResyncZOrder()
    {
        // Overlay full-screen (gộp) luôn trên cùng — không xếp theo Z-order cửa sổ đại diện.
        var tracked = new HashSet<IntPtr>(_overlays.Where(kv => !kv.Value.IsMerged).Select(kv => new IntPtr(unchecked((long)kv.Key))));
        if (tracked.Count == 0)
        {
            return;
        }

        ZOrderSync.Resync(tracked, trackedHandle =>
        {
            ulong key = unchecked((ulong)trackedHandle.ToInt64());
            return _overlays.TryGetValue(key, out ContentBlurOverlayForm? form) ? form.Handle : IntPtr.Zero;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _winEventRegistration?.Dispose();
            _debounceTimer?.Dispose();
            _windowGoneTimer.Dispose();
            _iconManager.Dispose();
#if PARENTALGUARD_DEVELOPER_MODE
            _developerOverlay?.Dispose();
#endif
        }

        base.Dispose(disposing);
    }
}

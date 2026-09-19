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

    private readonly Dictionary<ulong, ContentBlurOverlayForm> _overlays = [];
    private readonly Action<ForceCloseRequest> _sendForceClose;
    private readonly Action<IconPositionUpdate> _sendIconPosition;
    private readonly HashSet<IntPtr> _pendingLocationChangeHandles = [];
    private readonly StatusIconManager _iconManager;

    private IDisposable? _winEventRegistration;
    private System.Windows.Forms.Timer? _debounceTimer;
    private bool _pendingZOrderResync;

    public OverlayCoordinator(Action<ForceCloseRequest> sendForceClose, Action<IconPositionUpdate> sendIconPosition)
    {
        _sendForceClose = sendForceClose;
        _sendIconPosition = sendIconPosition;
        _iconManager = new StatusIconManager(sendIconPosition);

        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        Size = new System.Drawing.Size(1, 1);
        Load += (_, _) =>
        {
            Hide();
            _iconManager.Start();
        };

        // Mục 2.6/3.5: 1 WinEventHook dùng chung cho rect real-time (FE-016b) và z-order (BE-087).
        _winEventRegistration = WinEventHookInterop.Register(
            [WinEventHookInterop.EventObjectLocationChange, WinEventHookInterop.EventSystemForeground, WinEventHookInterop.EventObjectReorder],
            OnWinEvent);
    }

    /// <summary>`Architecture/02` mục 5 điểm 1: Overlay chỉ vẽ đúng danh sách này, không tự quyết định gì thêm.</summary>
    public void ApplyOverlayList(OverlayRectListCommand command)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyOverlayList(command));
            return;
        }

        var incomingHandles = command.Rects.Select(r => r.WindowHandle).ToHashSet();
        foreach (ulong staleHandle in _overlays.Keys.Where(h => !incomingHandles.Contains(h)).ToList())
        {
            RemoveOverlay(staleHandle);
        }

        foreach (OverlayRect rect in command.Rects)
        {
            if (_overlays.TryGetValue(rect.WindowHandle, out ContentBlurOverlayForm? existing))
            {
                if (existing.IsMerged != rect.IsMerged)
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
        var form = new ContentBlurOverlayForm(rect, HandleCloseButtonClicked, HandleMergedCloseTriggered);
        _overlays[rect.WindowHandle] = form;
        form.Show();
    }

    private void HandleCloseButtonClicked(ulong windowHandle, uint overlayId)
    {
        _sendForceClose(new ForceCloseRequest
        {
            WindowHandle = windowHandle,
            OverlayId = overlayId,
            ClickedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Source = CloseSource.Manual,
        });
        RemoveOverlay(windowHandle);
    }

    /// <summary>`BE-089`/`BE-089b`: 1 <c>ForceCloseRequest</c> riêng cho mỗi handle trong batch, cùng <paramref name="overlayId"/> và <paramref name="source"/> (mục 3.3).</summary>
    private void HandleMergedCloseTriggered(uint overlayId, IReadOnlyList<ulong> mergedWindowHandles, CloseSource source)
    {
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

        RemoveOverlayByOverlayId(overlayId);
    }

    private void RemoveOverlay(ulong windowHandle)
    {
        if (_overlays.Remove(windowHandle, out ContentBlurOverlayForm? form))
        {
            form.Close();
            form.Dispose();
        }
    }

    /// <summary>Overlay gộp được keyed trong <see cref="_overlays"/> theo window_handle đại diện — tìm đúng entry giữ <paramref name="overlayId"/> này rồi xoá.</summary>
    private void RemoveOverlayByOverlayId(uint overlayId)
    {
        ulong? key = _overlays.Where(kv => kv.Value.IsMerged && kv.Value.OverlayId == overlayId).Select(kv => (ulong?)kv.Key).FirstOrDefault();
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
            // Mục 2.6/3.5: lọc theo _overlays.Keys, tránh xử lý sự kiện của toàn bộ hệ thống.
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

    /// <summary>`BE-087`: z-index cục bộ — ADR-56.</summary>
    private void ResyncZOrder()
    {
        var tracked = new HashSet<IntPtr>(_overlays.Keys.Select(h => new IntPtr(unchecked((long)h))));
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
            _iconManager.Dispose();
        }

        base.Dispose(disposing);
    }
}

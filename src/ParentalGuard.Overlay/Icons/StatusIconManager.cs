using System.Drawing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Overlay.Resources;
using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Icons;

/// <summary>
/// 1 instance/màn hình, hot-plug qua <c>WM_DISPLAYCHANGE</c> (`BE-083`, Architecture/07-overlay-architecture.md
/// mục 4.1) — nguồn sự thật trạng thái là <c>MonitoringStatusUpdate</c> do Service đẩy xuống (ADR-65),
/// KHÔNG tự suy luận. Phải gọi từ UI thread của <see cref="Rendering.OverlayCoordinator"/>.
/// </summary>
public sealed class StatusIconManager : IDisposable
{
    private static readonly Color _activeColor = Color.FromArgb(46, 160, 67);
    private static readonly Color _pausedColor = Color.FromArgb(255, 193, 7);
    private static readonly Color _errorColor = Color.FromArgb(220, 53, 69);

    private readonly Dictionary<string, StatusIconForm> _icons = [];
    private readonly Dictionary<string, Point> _savedPositions = [];
    private readonly Action<IconPositionUpdate> _sendIconPosition;
    private readonly Action _requestOpenDashboard;
    private readonly System.Windows.Forms.Timer _pauseCountdownTimer;

    private IconState _state = IconState.Error;
    private long _pauseExpiresAtUnixMs;

    public StatusIconManager(Action<IconPositionUpdate> sendIconPosition, Action requestOpenDashboard)
    {
        _sendIconPosition = sendIconPosition;
        _requestOpenDashboard = requestOpenDashboard;
        _pauseCountdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pauseCountdownTimer.Tick += (_, _) => RefreshAllAppearance();
    }

    public void Start() => RefreshMonitors();

    /// <summary>Re-enumerate + diff theo <c>device_name</c> (cùng pattern diff-theo-key đã dùng cho <c>_overlays</c>).</summary>
    public void RefreshMonitors()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorInterop.EnumerateMonitors();
        var currentDeviceNames = monitors.Select(m => m.DeviceName).ToHashSet();

        foreach (string staleDeviceName in _icons.Keys.Where(d => !currentDeviceNames.Contains(d)).ToList())
        {
            _icons.Remove(staleDeviceName, out StatusIconForm? form);
            form?.Close();
            form?.Dispose();
        }

        foreach (MonitorInfo monitor in monitors)
        {
            if (_icons.ContainsKey(monitor.DeviceName))
            {
                continue;
            }

            Point? saved = _savedPositions.TryGetValue(monitor.DeviceName, out Point s) ? s : null;
            var form = new StatusIconForm(monitor.DeviceName, monitor.WorkArea, saved, OnPositionCommitted, _requestOpenDashboard);
            _icons[monitor.DeviceName] = form;
            form.Show();
            ApplyAppearance(form);
        }
    }

    public void ApplyStatus(MonitoringStatusUpdate status)
    {
        _state = status.State;
        _pauseExpiresAtUnixMs = status.PauseExpiresAtUnixMs;
        if (_state == IconState.Paused)
        {
            _pauseCountdownTimer.Start();
        }
        else
        {
            _pauseCountdownTimer.Stop();
        }

        RefreshAllAppearance();
    }

    /// <summary>`FE-020a`/ADR-67: áp dụng cho icon đang tồn tại khớp <c>device_name</c>; màn hình chưa từng kéo giữ nguyên vị trí mặc định.</summary>
    public void ApplyLayoutSync(IconLayoutSync sync)
    {
        _savedPositions.Clear();
        foreach (IconPositionUpdate position in sync.Positions)
        {
            _savedPositions[position.DeviceName] = new Point(position.X, position.Y);
            if (_icons.TryGetValue(position.DeviceName, out StatusIconForm? form))
            {
                form.Location = new Point(position.X, position.Y);
            }
        }
    }

    /// <summary>ADR-66: pipe đứt — không chờ push, tự chuyển local toàn bộ icon sang ERROR ngay.</summary>
    public void ApplyDisconnected()
    {
        _state = IconState.Error;
        _pauseCountdownTimer.Stop();
        RefreshAllAppearance();
    }

    private void OnPositionCommitted(string deviceName, Point location)
    {
        _savedPositions[deviceName] = location;
        _sendIconPosition(new IconPositionUpdate { DeviceName = deviceName, X = location.X, Y = location.Y });
    }

    private void RefreshAllAppearance()
    {
        foreach (StatusIconForm form in _icons.Values)
        {
            ApplyAppearance(form);
        }
    }

    private void ApplyAppearance(StatusIconForm form)
    {
        (Color color, string tooltip, StatusGlyph glyph) = _state switch
        {
            IconState.Active => (_activeColor, OverlayStrings.IconTooltipActive, StatusGlyph.Check),
            IconState.Paused => (_pausedColor, OverlayStrings.IconTooltipPaused(FormatCountdown()), StatusGlyph.Pause),
            _ => (_errorColor, OverlayStrings.IconTooltipError, StatusGlyph.Alert),
        };
        form.ApplyAppearance(color, tooltip, glyph);
    }

    private string FormatCountdown()
    {
        long remainingMs = _pauseExpiresAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        TimeSpan remaining = remainingMs > 0 ? TimeSpan.FromMilliseconds(remainingMs) : TimeSpan.Zero;
        return $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }

    public void Dispose()
    {
        _pauseCountdownTimer.Dispose();
        foreach (StatusIconForm form in _icons.Values)
        {
            form.Dispose();
        }

        _icons.Clear();
    }
}

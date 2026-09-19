using System.Drawing;
using System.Windows.Forms;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Overlay.Resources;
using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Rendering;

/// <summary>
/// `BE-030`-`032`, `BE-088a`/`089a`/`089b`, `FE-016`/`016c`/`016f`/`016g`
/// (Architecture/07-overlay-architecture.md mục 2/3.3/3.4): 1 overlay che đúng rect cửa sổ vi phạm
/// (hoặc toàn màn hình khi <see cref="IsMerged"/>) + nút "Tắt nội dung". Overlay không tự biết LÝ
/// DO che (<c>OverlayRect.Reason</c> chỉ phục vụ log phía Service — Architecture/02 mục 5.1).
/// </summary>
public sealed class ContentBlurOverlayForm : Form
{
    private readonly ulong _windowHandle;
    private readonly uint _overlayId;
    private readonly bool _isMerged;
    private readonly IReadOnlyList<ulong> _mergedWindowHandles;
    private readonly Action<ulong, uint> _onCloseButtonClicked;
    private readonly Action<uint, IReadOnlyList<ulong>, CloseSource> _onMergedCloseTriggered;

    // Mục 2.6: huỷ kết quả lớp 1 (UI Automation) tới muộn sau khi đã có 1 chu trình tính lại mới hơn
    // (move/resize kế tiếp) — tránh giật hình do nâng cấp trễ vô nghĩa.
    private int _exclusionGeneration;

    private System.Windows.Forms.Timer? _autoTimeoutTimer;
    private int _remainingSeconds;

    /// <summary>`FE-016g` (ĐÃ CHỐT v0.9.1) mục 3.4.3 — hook cho đếm ngược trực quan bắt buộc; listener gắn ở <see cref="AddCountdownLabel"/>.</summary>
    public event Action<int>? CountdownTick;

    public ContentBlurOverlayForm(
        OverlayRect rect,
        Action<ulong, uint> onCloseButtonClicked,
        Action<uint, IReadOnlyList<ulong>, CloseSource> onMergedCloseTriggered)
    {
        _windowHandle = rect.WindowHandle;
        _overlayId = rect.OverlayId;
        _isMerged = rect.IsMerged;
        _mergedWindowHandles = [.. rect.MergedWindowHandles];
        _onCloseButtonClicked = onCloseButtonClicked;
        _onMergedCloseTriggered = onMergedCloseTriggered;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = 0.92;

        var label = new Label
        {
            Text = "Nội dung không phù hợp đã được che",
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(FontFamily.GenericSansSerif, 14f, FontStyle.Bold),
        };

        var closeButton = new Button
        {
            Text = "Tắt nội dung",
            AutoSize = true,
        };
        closeButton.Click += (_, _) => HandleCloseButtonClicked();

        Controls.Add(label);
        Controls.Add(closeButton);
        closeButton.Location = new Point((ClientSize.Width - closeButton.Width) / 2, ClientSize.Height - closeButton.Height - 16);
        closeButton.BringToFront();

        if (_isMerged)
        {
            ApplyMergedBounds();

            // Bounds vừa đổi từ ClientSize mặc định của Form sang kích thước toàn màn hình thật (`ApplyMergedBounds`)
            // — tính lại vị trí nút theo Bounds thật, nếu không nút sẽ lệch khỏi màn hình.
            closeButton.Location = new Point((ClientSize.Width - closeButton.Width) / 2, ClientSize.Height - closeButton.Height - 16);

            StartAutoTimeout();
            AddCountdownLabel(closeButton);
        }
        else
        {
            ApplyRect(rect);
        }
    }

    public bool IsMerged => _isMerged;

    public ulong WindowHandle => _windowHandle;

    public uint OverlayId => _overlayId;

    private IntPtr TrackedHwnd => new(unchecked((long)_windowHandle));

    /// <summary>Bỏ qua nếu <see cref="IsMerged"/> — mục 3.3 "bỏ qua field rect".</summary>
    public void ApplyRect(OverlayRect rect)
    {
        if (_isMerged)
        {
            return;
        }

        Bounds = new Rectangle(rect.Rect.X, rect.Rect.Y, rect.Rect.Width, rect.Rect.Height);
        RecalculateExclusion();
    }

    /// <summary>`FE-016b`/`BE-031`: gọi bởi <see cref="OverlayCoordinator"/> khi WinEventHook (debounced) báo cửa sổ theo dõi đổi vị trí/kích thước.</summary>
    public void OnTrackedWindowLocationChanged()
    {
        if (_isMerged)
        {
            return;
        }

        Rectangle? windowRect = DwmInterop.GetExtendedFrameBounds(TrackedHwnd);
        if (windowRect is { } rect)
        {
            Bounds = rect;
        }

        RecalculateExclusion();
    }

    /// <summary>ADR-58: overlay gộp tự resolve bounds toàn màn hình (<c>rcMonitor</c>) — bỏ qua field <c>rect</c> nhận từ Service.</summary>
    private void ApplyMergedBounds()
    {
        MonitorInfo? monitor = MonitorInterop.GetMonitorInfoForWindow(TrackedHwnd);
        Bounds = monitor?.Bounds ?? Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
    }

    /// <summary>Mục 2.1: lớp 3 (fallback) đồng bộ ngay, lớp 1+2 nâng cấp bất đồng bộ trong ngân sách 150ms.</summary>
    private void RecalculateExclusion()
    {
        IntPtr hwnd = TrackedHwnd;
        double dpiScale = MonitorInterop.GetDpiScale(hwnd);
        Rectangle windowRect = DwmInterop.GetExtendedFrameBounds(hwnd) ?? Bounds;
        int generation = ++_exclusionGeneration;

        ApplyExclusionRegion(ExclusionRegionCalculator.FallbackRect(windowRect, dpiScale));

        _ = UpgradeExclusionAsync(hwnd, windowRect, dpiScale, generation);
    }

    private async Task UpgradeExclusionAsync(IntPtr hwnd, Rectangle windowRect, double dpiScale, int generation)
    {
        Rectangle? uiaRect = await CloseButtonLocator.LookupAsync(hwnd, windowRect).ConfigureAwait(true);
        if (uiaRect is null || IsDisposed || generation != _exclusionGeneration)
        {
            // Timeout/không tìm thấy, form đã đóng, hoặc đã có chu trình mới hơn (move/resize kế tiếp) — giữ nguyên lớp 3.
            return;
        }

        ApplyExclusionRegion(ExclusionRegionCalculator.PaddedRect(uiaRect.Value, windowRect, dpiScale));
    }

    private void ApplyExclusionRegion(Rectangle absoluteRect)
    {
        var relative = new Rectangle(absoluteRect.X - Location.X, absoluteRect.Y - Location.Y, absoluteRect.Width, absoluteRect.Height);
        var newRegion = new Region(ClientRectangle);
        newRegion.Exclude(relative);
        Region? previous = Region;
        Region = newRegion;
        previous?.Dispose();
    }

    private const int _wmSyscommand = 0x0112;
    private const int _scCloseMask = 0xFFF0;
    private const int _scClose = 0xF060;

    /// <summary>Chặn Alt+F4/Alt+Space→Đóng (WM_SYSCOMMAND/SC_CLOSE) — chống bypass lớp bảo vệ trẻ em; chỉ được đóng qua nút "Tắt nội dung" hoặc code nội bộ khi rect không còn.</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _wmSyscommand && (m.WParam.ToInt32() & _scCloseMask) == _scClose)
        {
            return;
        }

        base.WndProc(ref m);
    }

    private void HandleCloseButtonClicked()
    {
        if (_isMerged)
        {
            TriggerForceCloseAll(CloseSource.Manual);
            return;
        }

        OverlayWindowInterop.RequestClose(_windowHandle);
        _onCloseButtonClicked(_windowHandle, _overlayId);
    }

    /// <summary>`BE-089a`: nút "Tắt nội dung" duy nhất đóng TOÀN BỘ cửa sổ vi phạm bị gộp — dùng chung với auto-timeout (`BE-089b`).</summary>
    private void TriggerForceCloseAll(CloseSource source)
    {
        _autoTimeoutTimer?.Stop();
        foreach (ulong handle in _mergedWindowHandles)
        {
            OverlayWindowInterop.RequestClose(handle);
        }

        _onMergedCloseTriggered(_overlayId, _mergedWindowHandles, source);
    }

    /// <summary>
    /// `BE-089b`/`GEN-007b`: lối thoát dự phòng thứ 2, đếm hoàn toàn cục bộ — KHÔNG round-trip IPC
    /// tới Service (ADR-63: <see cref="System.Windows.Forms.Timer"/> chạy trên UI thread, tick 1s).
    /// </summary>
    private void StartAutoTimeout()
    {
        _remainingSeconds = 30;
        _autoTimeoutTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _autoTimeoutTimer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            CountdownTick?.Invoke(_remainingSeconds);
            if (_remainingSeconds <= 0)
            {
                TriggerForceCloseAll(CloseSource.AutoTimeout);
            }
        };
        _autoTimeoutTimer.Start();
    }

    /// <summary>
    /// `FE-016g` (v0.9.1)/`BE-089b`: đếm ngược trực quan bắt buộc, đặt cạnh nút "Tắt nội dung" duy nhất
    /// — CHỈ ở overlay full-screen lock (<see cref="_isMerged"/>), không hiện ở overlay chế độ thường.
    /// Subscribe <see cref="CountdownTick"/> (mục 3.4.3 — hook đã tồn tại sẵn cho đúng mục đích này).
    /// </summary>
    private void AddCountdownLabel(Button closeButton)
    {
        var countdownLabel = new Label
        {
            Text = OverlayStrings.AutoTimeoutCountdown(_remainingSeconds),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 11f, FontStyle.Bold),
        };

        Controls.Add(countdownLabel);
        countdownLabel.Location = new Point(closeButton.Left + ((closeButton.Width - countdownLabel.Width) / 2), closeButton.Bottom + 8);
        countdownLabel.BringToFront();

        CountdownTick += remainingSeconds => countdownLabel.Text = OverlayStrings.AutoTimeoutCountdown(remainingSeconds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoTimeoutTimer?.Stop();
            _autoTimeoutTimer?.Dispose();
            Region?.Dispose();
        }

        base.Dispose(disposing);
    }
}

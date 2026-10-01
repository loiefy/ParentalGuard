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
    private readonly Action<ulong, uint, CloseSource> _onCloseButtonClicked;
    private readonly Action<uint, IReadOnlyList<ulong>, CloseSource> _onMergedCloseTriggered;

    // Mục 2.6: huỷ kết quả lớp 1 (UI Automation) tới muộn sau khi đã có 1 chu trình tính lại mới hơn
    // (move/resize kế tiếp) — tránh giật hình do nâng cấp trễ vô nghĩa.
    private int _exclusionGeneration;

    private System.Windows.Forms.Timer? _autoTimeoutTimer;
    private int _remainingSeconds;

    private readonly Button _closeButton;
    private Label? _countdownLabel;

    /// <summary>`FE-016g` (ĐÃ CHỐT v0.9.1) mục 3.4.3 — hook cho đếm ngược trực quan bắt buộc; listener gắn ở <see cref="AddCountdownLabel"/>.</summary>
    public event Action<int>? CountdownTick;

    public ContentBlurOverlayForm(
        OverlayRect rect,
        Action<ulong, uint, CloseSource> onCloseButtonClicked,
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
        // FE-011 (bug real-hardware 2026-09-30): bản cũ nền đen Opacity=0.92 — vẫn nhìn xuyên ~8%,
        // nội dung sáng/tương phản cao lộ rõ. Che ĐỤC hoàn toàn (fail-secure, mức "blur" tối đa):
        // không pixel nào của cửa sổ vi phạm đi qua được overlay, trừ vùng loại trừ nút đóng FE-016a.
        BackColor = Color.FromArgb(24, 24, 28);
        Opacity = 1.0;

        var label = new Label
        {
            Text = "Nội dung không phù hợp đã được che",
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(FontFamily.GenericSansSerif, 14f, FontStyle.Bold),
        };

        // FE-012: primary button, màu nhấn rõ ràng (bản cũ là nút xám mặc định WinForms, lẫn vào nền).
        _closeButton = new Button
        {
            Text = "Tắt nội dung",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            Font = new Font(FontFamily.GenericSansSerif, 13f, FontStyle.Bold),
            Padding = new Padding(16, 6, 16, 6),
            Cursor = Cursors.Hand,
        };
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.Click += (_, _) => HandleCloseButtonClicked();

        Controls.Add(label);
        Controls.Add(_closeButton);
        _closeButton.BringToFront();

        // Bug đã sửa 2026-09-30: vị trí nút trước đây tính 1 lần trong constructor theo ClientSize MẶC ĐỊNH
        // của Form (trước khi ApplyRect gán Bounds thật) và không bao giờ tính lại khi overlay đổi kích
        // thước theo cửa sổ vi phạm → nút nằm lạc góc/khuất. Giờ đặt lại mỗi lần Resize.
        Resize += (_, _) => LayoutControls();

        if (_isMerged)
        {
            ApplyMergedBounds();
            StartAutoTimeout(MergedAutoTimeoutSeconds);
        }
        else
        {
            ApplyRect(rect);
            StartAutoTimeout(SingleAutoTimeoutSeconds);
        }

        AddCountdownLabel();
    }

    /// <summary>`BE-089b`/`FE-016g`: overlay full-screen lock (chế độ gộp).</summary>
    internal const int MergedAutoTimeoutSeconds = 30;

    /// <summary>`BE-034a`/`FE-016h` (ĐÃ CHỐT 2026-09-30): overlay thường (1 cửa sổ).</summary>
    internal const int SingleAutoTimeoutSeconds = 60;

    public bool IsMerged => _isMerged;

    /// <summary>Chế độ gộp: toàn bộ handle bị gộp; chế độ thường: đúng 1 handle đang che.</summary>
    public IReadOnlyList<ulong> CoveredWindowHandles => _isMerged ? _mergedWindowHandles : [_windowHandle];

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

    private void HandleCloseButtonClicked() => TriggerForceClose(CloseSource.Manual);

    /// <summary>Nút "Tắt nội dung" (`BE-032`/`BE-089a`) và auto-timeout (`BE-034a`/`BE-089b`) dùng chung 1 luồng force-close.</summary>
    private void TriggerForceClose(CloseSource source)
    {
        if (_isMerged)
        {
            TriggerForceCloseAll(source);
            return;
        }

        _autoTimeoutTimer?.Stop();
        OverlayWindowInterop.RequestClose(_windowHandle);
        _onCloseButtonClicked(_windowHandle, _overlayId, source);
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
    /// `BE-089b`/`GEN-007b` (gộp, 30s) và `BE-034a` (thường, 60s): lối thoát dự phòng, đếm hoàn toàn
    /// cục bộ — KHÔNG round-trip IPC tới Service (ADR-63: <see cref="System.Windows.Forms.Timer"/>
    /// chạy trên UI thread, tick 1s). Hết giờ → đúng luồng force-close như bấm tay, nguồn AUTO_TIMEOUT.
    /// </summary>
    private void StartAutoTimeout(int seconds)
    {
        _remainingSeconds = seconds;
        _autoTimeoutTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _autoTimeoutTimer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            CountdownTick?.Invoke(_remainingSeconds);
            if (_remainingSeconds <= 0)
            {
                TriggerForceClose(CloseSource.AutoTimeout);
            }
        };
        _autoTimeoutTimer.Start();
    }

    /// <summary>
    /// `FE-016g` (gộp)/`FE-016h` (thường, 2026-09-30): đếm ngược trực quan bắt buộc ngay dưới nút
    /// "Tắt nội dung" — không đếm ngầm rồi tự đóng bất ngờ. Subscribe <see cref="CountdownTick"/> (mục 3.4.3 — hook đã tồn tại sẵn cho đúng mục đích này).
    /// </summary>
    private void AddCountdownLabel()
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
        countdownLabel.BringToFront();
        _countdownLabel = countdownLabel;
        LayoutControls();

        CountdownTick += remainingSeconds =>
        {
            countdownLabel.Text = OverlayStrings.AutoTimeoutCountdown(remainingSeconds);
            LayoutControls(); // AutoSize đổi bề rộng khi số giây đổi số chữ số — căn giữa lại.
        };
    }

    /// <summary>
    /// Nút "Tắt nội dung" ngay dưới thông điệp (giữa overlay, luôn trong vùng nhìn thấy), đếm ngược
    /// (chỉ chế độ gộp) ngay dưới nút. Bản cũ đặt nút sát đáy và đếm ngược BÊN DƯỚI nút → đếm ngược
    /// rơi ra ngoài màn hình ở chế độ gộp, vi phạm FE-016g (đếm ngược trực quan bắt buộc).
    /// </summary>
    private void LayoutControls()
    {
        Size client = ClientSize;
        int buttonTop = Math.Min((client.Height / 2) + 40, client.Height - _closeButton.Height - 16);
        _closeButton.Location = new Point((client.Width - _closeButton.Width) / 2, Math.Max(0, buttonTop));
        if (_countdownLabel is not null)
        {
            _countdownLabel.Location = new Point((client.Width - _countdownLabel.Width) / 2, _closeButton.Bottom + 12);
        }
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

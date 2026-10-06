#if PARENTALGUARD_DEVELOPER_MODE
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Rendering;

/// <summary>
/// Chế độ developer (`DEV-050`–`DEV-052`, Specification/12 mục 6a) — chỉ biên dịch khi build bật
/// <c>ParentalGuardDeveloperMode</c> (mặc định bản dev; bản production TẮT). Mỗi cửa sổ Vision vừa phân tích có 1
/// khung viền đỏ cam 2px + ô tròn % rủi ro ở góc dưới-trái, tự bám vị trí thật của cửa sổ (kể cả trải nhiều màn hình).
/// Không đổi hành vi bảo vệ; khung không nhận chuột và bị loại khỏi ảnh chụp màn hình để không làm sai kết quả Vision.
/// </summary>
internal sealed class DeveloperOverlay : IDisposable
{
    /// <summary>Không có điểm mới trong khoảng này (cửa sổ không còn được quét) → gỡ khung.</summary>
    private static readonly TimeSpan _staleAfter = TimeSpan.FromSeconds(15);

    private readonly Dictionary<ulong, DeveloperFrameForm> _frames = [];
    private readonly System.Windows.Forms.Timer _followTimer = new() { Interval = 200 };

    public DeveloperOverlay()
    {
        _followTimer.Tick += (_, _) => FollowWindows();
        _followTimer.Start();
    }

    /// <summary>Gọi trên UI thread.</summary>
    public void Apply(DebugWindowScore score)
    {
        if (!_frames.TryGetValue(score.WindowHandle, out DeveloperFrameForm? frame))
        {
            frame = new DeveloperFrameForm();
            _frames[score.WindowHandle] = frame;
        }

        frame.RiskScore = score.RiskScore;
        frame.LastUpdatedUtc = DateTime.UtcNow;
        Rectangle? bounds = DwmInterop.GetExtendedFrameBounds(new IntPtr(unchecked((long)score.WindowHandle)))
            ?? (score.Bbox is { } b ? new Rectangle(b.X, b.Y, b.Width, b.Height) : null);
        if (bounds is { } r)
        {
            frame.ShowAt(r);
        }
    }

    private void FollowWindows()
    {
        List<ulong>? gone = null;
        foreach ((ulong hwnd, DeveloperFrameForm frame) in _frames)
        {
            IntPtr handle = new(unchecked((long)hwnd));
            bool stale = DateTime.UtcNow - frame.LastUpdatedUtc > _staleAfter;
            if (stale || !OverlayWindowInterop.WindowExists(hwnd) || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle))
            {
                (gone ??= []).Add(hwnd);
                continue;
            }

            if (DwmInterop.GetExtendedFrameBounds(handle) is { } bounds)
            {
                frame.ShowAt(bounds);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (ulong hwnd in gone)
        {
            _frames[hwnd].Dispose();
            _frames.Remove(hwnd);
        }
    }

    public void Dispose()
    {
        _followTimer.Dispose();
        foreach (DeveloperFrameForm frame in _frames.Values)
        {
            frame.Dispose();
        }

        _frames.Clear();
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    }

    /// <summary>Khung trong suốt, click-through, topmost, không hiện ở taskbar — chỉ vẽ viền + ô tròn %.</summary>
    private sealed class DeveloperFrameForm : Form
    {
        private const int WsExLayered = 0x00080000;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExTopmost = 0x00000008;
        private const int WmDpiChanged = 0x02E0;
        private const int WmNcHitTest = 0x0084;
        private const uint WdaExcludeFromCapture = 0x00000011;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTopmost = new(-1);
        private static readonly Color KeyColor = Color.FromArgb(1, 0, 1);
        private static readonly Color FrameColor = Color.OrangeRed;

        private Rectangle _bounds;

        public DeveloperFrameForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float RiskScore { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DateTime LastUpdatedUtc { get; set; }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost;
                return cp;
            }
        }

        /// <summary>Toạ độ pixel vật lý (Overlay PerMonitorV2) — SetWindowPos trực tiếp, không qua scale của WinForms.</summary>
        public void ShowAt(Rectangle bounds)
        {
            if (!IsHandleCreated)
            {
                CreateHandle();
                // Loại khỏi Desktop Duplication: khung debug không được lọt vào ảnh Vision phân tích.
                NativeMethods.SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
            }

            bool moved = bounds != _bounds;
            _bounds = bounds;
            NativeMethods.SetWindowPos(Handle, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
            if (!Visible)
            {
                Visible = true;
            }

            if (moved)
            {
                Invalidate();
            }
            else
            {
                Invalidate(CircleBounds());
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WmDpiChanged:
                    // Khung trải qua màn hình khác DPI: giữ nguyên kích thước pixel vật lý đã đặt, không để WinForms tự co giãn.
                    m.Result = IntPtr.Zero;
                    return;
                case WmNcHitTest:
                    m.Result = new IntPtr(-1); // HTTRANSPARENT
                    return;
            }

            base.WndProc(ref m);
        }

        private int CircleDiameter => Math.Max(36, (int)Math.Round(44 * DeviceDpi / 96.0));

        private Rectangle CircleBounds()
        {
            int d = CircleDiameter;
            int margin = 6;
            return new Rectangle(margin, ClientSize.Height - d - margin, d, d);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(KeyColor);
            using (var pen = new Pen(FrameColor, 2) { Alignment = PenAlignment.Inset })
            {
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }

            // TransparencyKey + khử răng cưa sẽ để lại viền màu key — vẽ hình tròn không khử răng cưa.
            Rectangle circle = CircleBounds();
            using (var fill = new SolidBrush(FrameColor))
            {
                g.FillEllipse(fill, circle);
            }

            string text = $"{Math.Round(Math.Clamp(RiskScore, 0f, 1f) * 100):0}%";
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            TextRenderer.DrawText(g, text, font, circle, Color.White, FrameColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
#endif

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Icons;

/// <summary>
/// 1 icon trạng thái/màn hình (`FE-020`-`022`, Architecture/07-overlay-architecture.md mục 4.1) —
/// layered window ~40×40px (ADR-64), kéo-thả (`FE-020a`), tooltip (`FE-022`). Click-through ngoài
/// icon đạt được tự nhiên (form nhỏ, không cần <see cref="Region"/> loại trừ như overlay blur).
/// </summary>
public sealed class StatusIconForm : Form
{
    // Yêu cầu chủ dự án 2026-10-01: giảm còn 70% (40px → 28px ở 100% DPI).
    private const int _sizeAt100Dpi = 28;

    private readonly string _deviceName;
    private readonly Action<string, Point> _onPositionCommitted;
    private readonly Action _onDoubleClick;
    private readonly StatusHoverLabelForm _hoverLabel = new();

    private Point _dragStartOffset;
    private Point _dragStartLocation;
    private bool _dragging;
    private bool _hovering;
    private Color _currentColor = Color.Gray;
    private string _statusText = string.Empty;

    private const int _wmDpiChanged = 0x02E0;
    private const int _marginAt100Dpi = 8;

    /// <param name="monitorWorkArea">Work area của màn hình icon thuộc về.</param>
    /// <param name="savedLocation">Vị trí đã kéo-thả lưu lại (FE-020a); null = góc dưới-phải mặc định (FE-020).</param>
    public StatusIconForm(string deviceName, Rectangle monitorWorkArea, Point? savedLocation, Action<string, Point> onPositionCommitted, Action onDoubleClick)
    {
        _deviceName = deviceName;
        _onPositionCommitted = onPositionCommitted;
        _onDoubleClick = onDoubleClick;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;

        // Bug real-hardware 2026-10-01 (máy 2 màn hình khác DPI): bản cũ tạo Handle (đo DPI) TRƯỚC khi đặt
        // Location → cửa sổ sinh ra trên màn hình chính, đo sai DPI, rồi WinForms tự scale lần nữa khi dời sang
        // màn hình phụ (icon 160px thay vì đúng cỡ) và vị trí mặc định tính theo cỡ cố định 40px nên tràn ra
        // ngoài mép màn hình. Nay: đặt Location vào đúng màn hình TRƯỚC khi tạo Handle, đo DPI thật, rồi mới
        // tính cỡ + vị trí mặc định theo cỡ thật; tự xử lý WM_DPICHANGED.
        Size = new Size(1, 1);
        Location = savedLocation ?? monitorWorkArea.Location;
        int size = SizeForDpiScale(MonitorInterop.GetDpiScale(Handle)); // ADR-61: DPI của chính màn hình icon nằm trên
        Size = new Size(size, size);
        Location = savedLocation is { } saved
            ? ClampInto(saved, Size, monitorWorkArea)
            : DefaultLocation(monitorWorkArea, size, MonitorInterop.GetDpiScale(Handle));

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        // FE-022a: dòng chữ trạng thái cạnh icon khi hover. Ẩn bằng timer kiểm tra vị trí con trỏ THẬT thay vì
        // MouseLeave — thử nghiệm thực tế cho thấy WM_MOUSELEAVE tới ngay sau khi cửa sổ chữ hiện ra dù con trỏ
        // vẫn trên icon (chữ chớp tắt/không hiện).
        _hoverPollTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _hoverPollTimer.Tick += (_, _) =>
        {
            if (!Bounds.Contains(Cursor.Position))
            {
                _hovering = false;
                _hoverPollTimer.Stop();
                _hoverLabel.Hide();
            }
        };
        MouseEnter += (_, _) => BeginHover();
    }

    private readonly System.Windows.Forms.Timer _hoverPollTimer;
    private long _lastClickUpTicks;
    private Point _lastClickUpCursor;

    private void BeginHover()
    {
        _hovering = true;
        if (!_dragging)
        {
            _hoverLabel.ShowNextTo(Bounds, _statusText);
        }

        _hoverPollTimer.Start();
    }

    /// <summary>
    /// `FE-023`: tự nhận double-click (2 lần nhả chuột trong DoubleClickTime, gần cùng vị trí, không kéo) — không
    /// phụ thuộc WM_LBUTTONDBLCLK của cửa sổ layered (thử nghiệm thực tế: lần click thứ 2 bị mất).
    /// </summary>
    private void RegisterClickForDoubleClick()
    {
        long now = Environment.TickCount64;
        Point cursor = Cursor.Position;
        Size slop = SystemInformation.DoubleClickSize;
        bool isDouble = _lastClickUpTicks != 0
            && now - _lastClickUpTicks <= SystemInformation.DoubleClickTime
            && Math.Abs(cursor.X - _lastClickUpCursor.X) <= slop.Width
            && Math.Abs(cursor.Y - _lastClickUpCursor.Y) <= slop.Height;
        if (isDouble)
        {
            _lastClickUpTicks = 0;
            _hoverLabel.Hide();
            _onDoubleClick();
            return;
        }

        _lastClickUpTicks = now;
        _lastClickUpCursor = cursor;
    }

    public string DeviceName => _deviceName;

    private static int SizeForDpiScale(double dpiScale) => (int)Math.Round(_sizeAt100Dpi * dpiScale, MidpointRounding.AwayFromZero);

    /// <summary>`FE-020`: góc dưới-phải work area (không đè taskbar), theo cỡ icon THẬT ở DPI màn hình đó.</summary>
    internal static Point DefaultLocation(Rectangle workArea, int iconSize, double dpiScale)
    {
        int margin = (int)Math.Round(_marginAt100Dpi * dpiScale);
        return new Point(workArea.Right - iconSize - margin, workArea.Bottom - iconSize - margin);
    }

    /// <summary>Vị trí đã lưu có thể nằm ngoài màn hình (đổi độ phân giải/đổi màn hình) — kéo vào trong work area.</summary>
    internal static Point ClampInto(Point location, Size size, Rectangle workArea) => new(
        Math.Clamp(location.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
        Math.Clamp(location.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));

    /// <summary>Tự đổi cỡ theo DPI mới, giữ nguyên góc trên-trái — không để WinForms áp "suggested rect" (gây icon phình to).</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _wmDpiChanged)
        {
            int dpi = (int)((long)m.WParam & 0xFFFF);
            int size = SizeForDpiScale(dpi / 96.0);
            Size = new Size(size, size);
            Render();
            return;
        }

        base.WndProc(ref m);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // Bug real-hardware 2026-10-01: bản cũ là cửa sổ layered per-pixel alpha (UpdateLayeredWindow) — thử
            // nghiệm cho thấy hit-test chuột chập chờn (MouseEnter/Leave nhấp nháy, mất MouseDown) nên hover
            // FE-022a/double-click FE-023 không hoạt động. Nay là cửa sổ thường cắt hình tròn bằng Region.
            cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */;
            return cp;
        }
    }

    /// <summary>`FE-021`/`022`: ánh xạ trạng thái → màu + tooltip (mã hex/glyph cụ thể là chi tiết asset, mục 4.1.1 — không chặn thiết kế).</summary>
    public void ApplyAppearance(Color fillColor, string statusText)
    {
        _currentColor = fillColor;
        _statusText = statusText;
        if (_hovering && !_dragging)
        {
            _hoverLabel.UpdateText(Bounds, statusText);
        }

        Render();
    }

    private void Render()
    {
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, Width, Height);
        Region? old = Region;
        Region = new Region(path); // vùng nhận chuột/hiển thị = đúng hình tròn
        old?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(_currentColor);
        using var brush = new SolidBrush(_currentColor);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Vẽ toàn bộ ở OnPaint — tránh nhấp nháy nền mặc định.
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        _dragging = true;
        _dragStartOffset = e.Location;
        _dragStartLocation = Location;
    }

    /// <summary>`FE-020a`: pattern chuẩn WinForms — MouseMove cập nhật <see cref="Control.Location"/> theo con trỏ (layered window tự di chuyển theo, không cần render lại).</summary>
    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Location = new Point(Location.X + e.X - _dragStartOffset.X, Location.Y + e.Y - _dragStartOffset.Y);
        if (Location != _dragStartLocation)
        {
            _hoverLabel.Hide();
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (Location != _dragStartLocation)
        {
            _onPositionCommitted(_deviceName, Location); // click/double-click không di chuyển thì không ghi lại vị trí
            _lastClickUpTicks = 0;
        }
        else
        {
            RegisterClickForDoubleClick();
        }
    }

    // Icon trạng thái không được chiếm focus khi click (không cướp bàn phím khỏi ứng dụng đang dùng, không hiện
    // trong Alt+Tab) — kích hoạt cửa sổ lúc click đầu cũng là 1 nghi vấn làm mất click thứ 2.
    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hoverPollTimer.Dispose();
            _hoverLabel.Dispose();
        }

        base.Dispose(disposing);
    }
}

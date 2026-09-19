using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    private const int _wsExLayered = 0x00080000;
    private const int _sizeAt100Dpi = 40;

    private readonly string _deviceName;
    private readonly Action<string, Point> _onPositionCommitted;
    private readonly ToolTip _toolTip = new();

    private Point _dragStartOffset;
    private bool _dragging;
    private Color _currentColor = Color.Gray;

    public StatusIconForm(string deviceName, Point initialScreenLocation, Action<string, Point> onPositionCommitted)
    {
        _deviceName = deviceName;
        _onPositionCommitted = onPositionCommitted;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;

        double dpiScale = MonitorInterop.GetDpiScale(Handle); // ADR-61: DPI của chính màn hình icon nằm trên
        int size = (int)Math.Round(_sizeAt100Dpi * dpiScale, MidpointRounding.AwayFromZero);
        Size = new Size(size, size);
        Location = initialScreenLocation;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
    }

    public string DeviceName => _deviceName;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= _wsExLayered;
            return cp;
        }
    }

    /// <summary>`FE-021`/`022`: ánh xạ trạng thái → màu + tooltip (mã hex/glyph cụ thể là chi tiết asset, mục 4.1.1 — không chặn thiết kế).</summary>
    public void ApplyAppearance(Color fillColor, string tooltipText)
    {
        _currentColor = fillColor;
        _toolTip.SetToolTip(this, tooltipText);
        Render();
    }

    private void Render()
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_currentColor);
            g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }

        LayeredIconRenderer.Render(Handle, bitmap, Location);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        _dragging = true;
        _dragStartOffset = e.Location;
    }

    /// <summary>`FE-020a`: pattern chuẩn WinForms — MouseMove cập nhật <see cref="Control.Location"/> theo con trỏ (layered window tự di chuyển theo, không cần render lại).</summary>
    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Location = new Point(Location.X + e.X - _dragStartOffset.X, Location.Y + e.Y - _dragStartOffset.Y);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _onPositionCommitted(_deviceName, Location);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}

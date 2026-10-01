using System.Drawing;
using System.Windows.Forms;

namespace ParentalGuard.Overlay.Icons;

/// <summary>
/// `FE-022a` (Specification/03 v0.11.0, ĐÃ CHỐT 2026-10-01): 1 dòng chữ trạng thái hiện ngay cạnh icon khi
/// hover. Thay <c>ToolTip</c> WinForms (hiện trễ, vị trí do hệ thống quyết định). Không chiếm focus
/// (<c>WS_EX_NOACTIVATE</c>), không chặn click (<c>WS_EX_TRANSPARENT</c>), không lên taskbar/Alt+Tab.
/// </summary>
public sealed class StatusHoverLabelForm : Form
{
    private const int _wsExTransparent = 0x00000020;
    private const int _wsExToolWindow = 0x00000080;
    private const int _wsExNoActivate = 0x08000000;
    private const int _gapPx = 8;

    private readonly Label _label;

    public StatusHoverLabelForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 36);
        Opacity = 0.95;

        _label = new Label
        {
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            Padding = new Padding(10, 6, 10, 6),
        };
        Controls.Add(_label);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= _wsExTransparent | _wsExToolWindow | _wsExNoActivate;
            return cp;
        }
    }

    /// <summary>Hiện chữ ở phía còn trống của màn hình: icon nằm nửa phải → chữ bên trái, và ngược lại.</summary>
    public void ShowNextTo(Rectangle iconBounds, string text)
    {
        // Đo chữ tường minh (không dựa AutoSize của Form — trước khi Show, PreferredSize chưa đúng nên cửa sổ có
        // thể sinh ra to/lệch và đè lên chính icon).
        _label.Text = text;
        Size textSize = TextRenderer.MeasureText(text, _label.Font);
        var size = new Size(textSize.Width + _label.Padding.Horizontal + 2, textSize.Height + _label.Padding.Vertical + 2);
        Size = size;
        _label.Location = Point.Empty;

        Rectangle workArea = Screen.FromRectangle(iconBounds).WorkingArea;
        bool iconOnRightHalf = iconBounds.Left + (iconBounds.Width / 2) > workArea.Left + (workArea.Width / 2);
        int x = iconOnRightHalf ? iconBounds.Left - size.Width - _gapPx : iconBounds.Right + _gapPx;
        int y = iconBounds.Top + ((iconBounds.Height - size.Height) / 2);
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height));

        Location = new Point(x, y);
        if (!Visible)
        {
            Show();
        }
    }

    /// <summary>Cập nhật chữ khi đang hiện (vd đếm ngược Tạm dừng) mà không đổi vị trí neo.</summary>
    public void UpdateText(Rectangle iconBounds, string text)
    {
        if (Visible && _label.Text != text)
        {
            ShowNextTo(iconBounds, text);
        }
    }
}

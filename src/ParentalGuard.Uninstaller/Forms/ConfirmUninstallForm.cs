using System.Drawing;
using System.Windows.Forms;

namespace ParentalGuard.Uninstaller.Forms;

/// <summary>Màn hình xác nhận cuối (mục 5.3) — checkbox mặc định KHÔNG tick (mục 10, câu hỏi phụ không-blocking, giữ nguyên như đã ghi).</summary>
public sealed class ConfirmUninstallForm : Form
{
    private readonly CheckBox _keepAuditLogCheckBox;

    public ConfirmUninstallForm()
    {
        Text = "Gỡ cài đặt ParentalGuard — Xác nhận";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 160);

        var messageLabel = new Label
        {
            Left = 12,
            Top = 12,
            Width = 396,
            Height = 60,
            Text = "Bạn có chắc chắn muốn gỡ cài đặt ParentalGuard? Toàn bộ cấu hình giám sát và mật khẩu sẽ bị xoá.",
        };
        _keepAuditLogCheckBox = new CheckBox { Left = 12, Top = 76, Width = 396, Text = "Giữ lại nhật ký hoạt động cuối cùng (lưu ra Desktop)", Checked = false };

        var proceedButton = new Button { Text = "Gỡ cài đặt", Left = 252, Top = 116, Width = 90, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Huỷ", Left = 344, Top = 116, Width = 64, DialogResult = DialogResult.Cancel };

        Controls.Add(messageLabel);
        Controls.Add(_keepAuditLogCheckBox);
        Controls.Add(proceedButton);
        Controls.Add(cancelButton);
        AcceptButton = proceedButton;
        CancelButton = cancelButton;
    }

    public bool KeepAuditLog => _keepAuditLogCheckBox.Checked;
}

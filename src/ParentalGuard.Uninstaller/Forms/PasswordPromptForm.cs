using System.Drawing;
using System.Text;
using System.Windows.Forms;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Uninstaller.Forms;

/// <summary>Lớp gate #2 (mục 5.1/5.3) — modal mật khẩu ParentalGuard (KHÔNG phải mật khẩu Windows).</summary>
public sealed class PasswordPromptForm : Form
{
    private readonly Label _messageLabel;
    private readonly TextBox _passwordTextBox;

    public PasswordPromptForm(AuthVerifyResponse? previousAttempt)
    {
        Text = "Gỡ cài đặt ParentalGuard — Xác thực";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 150);

        _messageLabel = new Label { Left = 12, Top = 12, Width = 356, Height = 40, Text = "Nhập mật khẩu ParentalGuard để tiếp tục gỡ cài đặt." };
        _passwordTextBox = new TextBox { Left = 12, Top = 56, Width = 356, PasswordChar = '●' };

        var okButton = new Button { Text = "Xác nhận", Left = 212, Top = 90, Width = 75, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Huỷ", Left = 293, Top = 90, Width = 75, DialogResult = DialogResult.Cancel };

        Controls.Add(_messageLabel);
        Controls.Add(_passwordTextBox);
        Controls.Add(okButton);
        Controls.Add(cancelButton);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        ApplyPreviousAttempt(previousAttempt);
    }

    private void ApplyPreviousAttempt(AuthVerifyResponse? previousAttempt)
    {
        if (previousAttempt is null)
        {
            return;
        }

        _messageLabel.Text = previousAttempt.Result == AuthResult.LockedOut
            ? $"Đã thử sai quá nhiều lần. Vui lòng thử lại sau (khoá đến {DateTimeOffset.FromUnixTimeMilliseconds(previousAttempt.LockoutUntilUnixMs).ToLocalTime():HH:mm:ss})."
            : "Sai mật khẩu. Vui lòng thử lại.";
    }

    /// <summary>UTF-8, caller chịu trách nhiệm zero sau khi dùng xong (đúng nguyên tắc <c>CredentialBytes</c> đã dùng ở Service).</summary>
    public byte[] GetPasswordUtf8Bytes() => Encoding.UTF8.GetBytes(_passwordTextBox.Text);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Không giữ plaintext lâu hơn cần thiết trong control WinForms (best-effort — TextBox nội
            // bộ không cho zero trực tiếp managed string, nhưng ít nhất không để lộ qua control nữa).
            _passwordTextBox.Text = string.Empty;
        }

        base.Dispose(disposing);
    }
}

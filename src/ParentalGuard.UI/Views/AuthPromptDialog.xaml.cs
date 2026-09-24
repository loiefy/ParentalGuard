using System.Text;
using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `S5` Auth Modal (Architecture/10-ui-architecture.md mục 6.5) — component tái dùng cho mọi
/// <c>action_context</c>. <see cref="RequestActionTokenAsync"/> trả <c>action_token</c> (null nếu
/// huỷ) cho caller (không tự điều hướng — caller quyết định làm gì với token).
/// </summary>
public sealed partial class AuthPromptDialog : ContentDialog
{
    public AuthPromptDialog(IAuthFacade authFacade, string actionContext)
    {
        ViewModel = new AuthPromptViewModel(authFacade, actionContext);
        InitializeComponent();
    }

    public AuthPromptViewModel ViewModel { get; }

    /// <summary><c>true</c> nếu người dùng bấm "Quên mật khẩu?" — caller điều hướng <c>S6</c> (mục 6.5).</summary>
    public bool ForgotPasswordRequested { get; private set; }

    /// <summary>Hiện dialog, trả <c>action_token</c> nếu xác thực thành công, null nếu huỷ/đóng dialog.</summary>
    public async Task<byte[]?> RequestActionTokenAsync()
    {
        await ShowAsync();
        return ViewModel.ActionToken;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            // Luôn tự quyết định đóng dialog (Hide()) khi SUCCESS — không để ContentDialog tự đóng
            // ngầm định để còn giữ dialog mở lại khi sai mật khẩu/bị khoá (mục 6.5).
            args.Cancel = true;

            // Architecture/08 mục 5.3 — đọc PasswordBox.Password (string bất biến) → byte[] pinned NGAY LẬP TỨC.
            string password = PasswordInput.Password;
            byte[] passwordUtf8Pinned = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(password), pinned: true);
            Encoding.UTF8.GetBytes(password, passwordUtf8Pinned);
            PasswordInput.Password = string.Empty; // best-effort dọn UI — không xoá được bản gốc trong heap managed (residual risk).

            bool success = await ViewModel.SubmitAsync(passwordUtf8Pinned, CancellationToken.None);
            if (success)
            {
                Hide();
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnForgotPasswordClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ForgotPasswordRequested = true;
        Hide();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.Views;

namespace ParentalGuard.UI.Services;

/// <summary>
/// Điều hướng cấp cao giữa các Views (Architecture/10-ui-architecture.md mục 2.1/4) + quản lý hiển
/// thị <see cref="AuthPromptDialog"/> (`S5`) theo <c>action_context</c>.
/// </summary>
public sealed class NavigationService(IAuthFacade authFacade)
{
    private Frame? _rootFrame;

    public void Initialize(Frame rootFrame) => _rootFrame = rootFrame;

    public bool NavigateToConnectionError() => Navigate(typeof(ConnectionErrorPage));

    public bool NavigateToOnboarding() => Navigate(typeof(OnboardingPage));

    public bool NavigateToMainShell() => Navigate(typeof(MainShellPage));

    /// <summary>`S6` Recovery — CHƯA implement (giai đoạn sau Đợt 6, mục "Không làm ở giai đoạn này").</summary>
    public void NavigateToRecovery() => throw new NotImplementedException("S6 Recovery chưa implement ở giai đoạn này.");

    /// <summary>Mục 6.5 — hiện <see cref="AuthPromptDialog"/>, trả <c>action_token</c> (null nếu huỷ). Điều hướng <c>S6</c> nếu người dùng bấm "Quên mật khẩu?".</summary>
    public async Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot)
    {
        var dialog = new AuthPromptDialog(authFacade, actionContext) { XamlRoot = xamlRoot };
        byte[]? token = await dialog.RequestActionTokenAsync().ConfigureAwait(true);
        if (token is null && dialog.ForgotPasswordRequested)
        {
            NavigateToRecovery();
        }

        return token;
    }

    private bool Navigate(Type pageType) => _rootFrame?.Navigate(pageType) ?? false;
}

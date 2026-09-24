using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;
using ParentalGuard.UI.Views;

namespace ParentalGuard.UI;

/// <summary>
/// Entry point (Architecture/10-ui-architecture.md mục 2.3/4/9): single-instance guard (ADR-117a) →
/// khởi tạo DI container tối giản (ADR-116/mục 2.2) → <see cref="UiIpcClient.ConnectAsync"/> →
/// <c>AuthStatusQuery</c> → điều hướng <c>S1</c> Onboarding hoặc Main Shell.
/// </summary>
public partial class App : Application
{
    private SingleInstanceGuard? _singleInstanceGuard;
    private Window? _window;
    private OnboardingViewModel? _activeOnboardingViewModel;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Composition root tối giản (mục 2.2 — "container đơn giản khởi tạo 1 lần"), truy cập qua <c>(App)Application.Current</c>.</summary>
    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.IsFirstInstance)
        {
            // ADR-117a — đưa cửa sổ đang chạy lên foreground rồi tự thoát ngay, không thử connect pipe.
            SingleInstanceGuard.ActivateExistingInstance();
            _singleInstanceGuard.Dispose();
            Environment.Exit(0);
            return;
        }

        Services = BuildServiceProvider();

        var mainWindow = new MainWindow();
        _window = mainWindow;
        mainWindow.Closed += OnWindowClosed;

        var navigationService = Services.GetRequiredService<NavigationService>();
        navigationService.Initialize(mainWindow.RootFrameControl);

        mainWindow.Activate();

        _ = ConnectAndRouteAsync();
    }

    /// <summary>Gọi bởi <see cref="Views.ConnectionErrorPage"/> khi bấm "Thử lại" (mục 9).</summary>
    public Task RetryConnectAsync() => ConnectAndRouteAsync();

    /// <summary>
    /// Gọi bởi <see cref="OnboardingPage"/> khi tạo <see cref="OnboardingViewModel"/> — safety net để
    /// <see cref="OnWindowClosed"/> có thể zero Recovery Key buffer nếu user đóng app giữa chừng ở
    /// màn Recovery Key (trước khi Confirm).
    /// </summary>
    public void RegisterActiveOnboardingViewModel(OnboardingViewModel viewModel) => _activeOnboardingViewModel = viewModel;

    /// <summary>Gọi bởi <see cref="OnboardingPage"/> khi rời hẳn luồng Onboarding (đã Confirm hoặc phát hiện AlreadyConfigured).</summary>
    public void UnregisterActiveOnboardingViewModel(OnboardingViewModel viewModel)
    {
        if (ReferenceEquals(_activeOnboardingViewModel, viewModel))
        {
            _activeOnboardingViewModel = null;
        }
    }

    private async Task ConnectAndRouteAsync()
    {
        var uiIpcClient = Services.GetRequiredService<UiIpcClient>();
        var navigationService = Services.GetRequiredService<NavigationService>();
        try
        {
            await uiIpcClient.ConnectAsync(CancellationToken.None).ConfigureAwait(true);
            var authFacade = Services.GetRequiredService<IAuthFacade>();
            bool passwordConfigured = await authFacade.GetAuthStatusAsync(CancellationToken.None).ConfigureAwait(true);
            if (passwordConfigured)
            {
                navigationService.NavigateToMainShell();
            }
            else
            {
                navigationService.NavigateToOnboarding();
            }
        }
        catch (UiIpcConnectionException)
        {
            navigationService.NavigateToConnectionError();
        }
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<UiIpcClient>();
        services.AddSingleton<IAuthFacade, AuthFacade>();
        services.AddSingleton<IPauseFacade, PauseFacade>();
        services.AddSingleton<IDashboardFacade, DashboardFacade>();
        services.AddSingleton<IAuditFacade, AuditFacade>();
        services.AddSingleton<IConfigFacade, ConfigFacade>();
        services.AddSingleton<NavigationService>();
        return services.BuildServiceProvider();
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // BUG B fix (security audit Đợt 6): đóng app giữa chừng ở màn Recovery Key (trước Confirm)
        // phải vẫn zero plaintext buffer — ConfirmRecoveryKeySavedAsync bình thường không được gọi.
        _activeOnboardingViewModel?.Dispose();
        _activeOnboardingViewModel = null;

        var uiIpcClient = Services.GetRequiredService<UiIpcClient>();
        await uiIpcClient.DisconnectAsync().ConfigureAwait(false);
        _singleInstanceGuard?.Dispose();
    }
}

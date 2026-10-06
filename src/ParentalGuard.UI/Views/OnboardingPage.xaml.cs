using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `S1` Onboarding (Architecture/10-ui-architecture.md mục 6.1) — host <see cref="StepFrame"/> điều
/// hướng qua 3 bước con, dùng chung 1 <see cref="OnboardingViewModel"/> truyền qua tham số navigate.
/// </summary>
public sealed partial class OnboardingPage : Page
{
    private OnboardingViewModel? _viewModel;

    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => DrawFaintShapes();
        ActualThemeChanged += (_, _) => DrawFaintShapes();
    }

    /// <summary>
    /// FE-032a: vài hình tròn/vuông/tam giác mờ nhạt rải "ngẫu nhiên" phía sau nội dung — seed cố định nên bố cục ổn
    /// định giữa các lần mở; kích thước cố định, cửa sổ to/nhỏ chỉ hiện thêm/bớt hình (cùng nguyên tắc FE-005b).
    /// </summary>
    private void DrawFaintShapes()
    {
        ShapesCanvas.Children.Clear();
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var brush = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["PgFaintShapeColor"]);
        int seq = 1000;
        double Next() => MainShellPage.DecorativeNoise(seq++);
        const double cell = 260;
        for (double y = 0; y < height; y += cell)
        {
            for (double x = 0; x < width; x += cell)
            {
                if (Next() < 0.45)
                {
                    continue;
                }

                double size = 40 + (Next() * 90);
                double left = x + (Next() * (cell - size));
                double top = y + (Next() * (cell - size));
                Shape shape = (int)(Next() * 3) switch
                {
                    0 => new Ellipse { Width = size, Height = size },
                    1 => new Rectangle { Width = size, Height = size, RadiusX = 6, RadiusY = 6 },
                    _ => new Polygon { Points = { new(size / 2, 0), new(size, size * 0.87), new(0, size * 0.87) } },
                };
                shape.Fill = brush;
                shape.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                shape.RenderTransform = new RotateTransform { Angle = Next() * 360 };
                Canvas.SetLeft(shape, left);
                Canvas.SetTop(shape, top);
                ShapesCanvas.Children.Add(shape);
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            return;
        }

        IServiceProvider services = ((App)Application.Current).Services;
        _viewModel = new OnboardingViewModel(services.GetRequiredService<IAuthFacade>());
        // BUG B fix — đăng ký làm safety net cho App.OnWindowClosed (zero Recovery Key buffer nếu
        // user đóng app giữa chừng, trước khi Confirm).
        ((App)Application.Current).RegisterActiveOnboardingViewModel(_viewModel);
        _viewModel.NavigateToSetPasswordRequested += (_, _) => StepFrame.Navigate(typeof(OnboardingSetPasswordPage), _viewModel);
        _viewModel.NavigateBackToSetPasswordRequested += (_, _) => StepFrame.Navigate(typeof(OnboardingSetPasswordPage), _viewModel);
        _viewModel.NavigateToRecoveryKeyRequested += (_, _) => StepFrame.Navigate(typeof(OnboardingRecoveryKeyPage), _viewModel);
        _viewModel.AlreadyConfiguredDetected += OnAlreadyConfiguredAsync;
        _viewModel.OnboardingCompleted += (_, _) => GoToMainShell();

        StepFrame.Navigate(typeof(OnboardingWelcomePage), _viewModel);
    }

    /// <summary>Mục 6.1 bước 2 — race hiếm: auth.dat đã tồn tại khi Onboarding vẫn đang chạy.</summary>
    private async void OnAlreadyConfiguredAsync(object? sender, EventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("OnboardingAlreadyConfigured"),
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
        GoToMainShell();
    }

    private void GoToMainShell()
    {
        if (_viewModel is not null)
        {
            ((App)Application.Current).UnregisterActiveOnboardingViewModel(_viewModel);
        }

        ((App)Application.Current).Services.GetRequiredService<NavigationService>().NavigateToMainShell();
    }
}

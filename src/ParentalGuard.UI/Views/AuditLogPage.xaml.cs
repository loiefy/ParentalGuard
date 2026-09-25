using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `S3` Audit log (Architecture/10-ui-architecture.md mục 6.3) — vào tab luôn hiện `S5` TRƯỚC khi
/// render nội dung (<see cref="OnNavigatedTo"/> gọi <see cref="AuditLogViewModel.InitializeAsync"/>);
/// huỷ dialog → <see cref="AuditLogViewModel.GateCancelled"/>=true → điều hướng lại `S2`.
/// </summary>
public sealed partial class AuditLogPage : Page
{
    public AuditLogPage()
    {
        InitializeComponent();

        IServiceProvider services = ((App)Application.Current).Services;
        ViewModel = new AuditLogViewModel(
            services.GetRequiredService<IAuditFacade>(),
            services.GetRequiredService<NavigationService>());
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        EmptyText.Text = LocalizationService.Get("AuditLogEmptyText");
        LoadMoreButton.Content = LocalizationService.Get("AuditLoadMoreButton");
    }

    public AuditLogViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.InitializeAsync(XamlRoot);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuditLogViewModel.GateCancelled) && ViewModel.GateCancelled)
        {
            Frame.Navigate(typeof(DashboardPage));
        }
    }

    private async void OnLoadMoreClick(object sender, RoutedEventArgs e) => await ViewModel.LoadMoreAsync();

    private async void OnMarkFalsePositiveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string processName })
        {
            await ViewModel.MarkFalsePositiveAsync(processName, XamlRoot);
        }
    }

    /// <summary>Mục 7/ADR-124 — nút "Đánh dấu sai" nằm trong <c>DataTemplate</c> (lặp lại theo dòng), set text qua resource lúc mỗi instance tải xong (không có nơi "static label" chung như các control đơn lẻ khác).</summary>
    private void OnMarkFalsePositiveButtonLoaded(object sender, RoutedEventArgs e) => ((Button)sender).Content = LocalizationService.Get("AuditMarkFalsePositiveButton");
}

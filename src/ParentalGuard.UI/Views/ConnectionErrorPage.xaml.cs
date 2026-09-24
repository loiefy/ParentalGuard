using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>Mục 9 — màn hình lỗi kết nối toàn cửa sổ lúc khởi động (`ConnectAsync` fail/timeout).</summary>
public sealed partial class ConnectionErrorPage : Page
{
    public ConnectionErrorPage()
    {
        InitializeComponent();
        TitleText.Text = LocalizationService.Get("ConnectionErrorTitle");
        MessageText.Text = LocalizationService.Get("ConnectionErrorMessage");
        RetryButton.Content = LocalizationService.Get("ConnectionErrorRetryButton");
    }

    private async void OnRetryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        RetryButton.IsEnabled = false;
        try
        {
            await ((App)Microsoft.UI.Xaml.Application.Current).RetryConnectAsync();
        }
        finally
        {
            RetryButton.IsEnabled = true;
        }
    }
}

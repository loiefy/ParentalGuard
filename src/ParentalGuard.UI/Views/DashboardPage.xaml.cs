using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `S2` Dashboard (Architecture/10-ui-architecture.md mục 6.2) — poll 5 giây khi hiển thị (ADR-120,
/// <see cref="OnNavigatedTo"/>/<see cref="OnNavigatedFrom"/> gọi <see cref="DashboardViewModel.Start"/>/
/// <see cref="DashboardViewModel.Stop"/>).
/// </summary>
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();

        IServiceProvider services = ((App)Application.Current).Services;
        ViewModel = new DashboardViewModel(
            services.GetRequiredService<IDashboardFacade>(),
            services.GetRequiredService<IPauseFacade>(),
            services.GetRequiredService<NavigationService>());
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyStaticLabels();
    }

    public DashboardViewModel ViewModel { get; }

    private void ApplyStaticLabels()
    {
        HealthTitleText.Text = LocalizationService.Get("DashboardHealthTitle");
        HealthDetailsExpander.Header = LocalizationService.Get("DashboardHealthDetailsHeader");
        PauseDurationLabel.Text = LocalizationService.Get("DashboardPauseDurationLabel");
        PauseButton.Content = LocalizationService.Get("DashboardPauseButton");
        ResumeButton.Content = LocalizationService.Get("DashboardResumeButton");
        AnomalyInfoBar.Title = LocalizationService.Get("DashboardAnomalyBannerTitle");
        AnomalyInfoBar.Message = LocalizationService.Get("DashboardAnomalyBannerMessage");
        AnomalyAckButton.Content = LocalizationService.Get("DashboardAnomalyAckButton");
        ConnectionLostInfoBar.Message = LocalizationService.Get("DashboardConnectionLostMessage");
        DiskSpaceLowInfoBar.Message = LocalizationService.Get("DashboardHealthDiskSpaceLow");
        FallbackConfigInfoBar.Message = LocalizationService.Get("DashboardHealthFallbackConfig");
        ChartTitleText.Text = LocalizationService.Get("DashboardChartTitle");
        Chart7Button.Content = LocalizationService.Get("DashboardChart7DaysButton");
        Chart30Button.Content = LocalizationService.Get("DashboardChart30DaysButton");
        ChartEmptyText.Text = LocalizationService.Get("DashboardChartEmptyText");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Start(DispatcherQueue.GetForCurrentThread());
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Stop();
    }

    private async void OnPauseClick(object sender, RoutedEventArgs e) => await ViewModel.PauseAsync(XamlRoot);

    private async void OnResumeClick(object sender, RoutedEventArgs e) => await ViewModel.ResumeAsync(XamlRoot);

    private async void OnChart7Click(object sender, RoutedEventArgs e) => await ViewModel.SetChartRangeAsync(7);

    private async void OnChart30Click(object sender, RoutedEventArgs e) => await ViewModel.SetChartRangeAsync(30);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.ChartData) or nameof(DashboardViewModel.IsChartEmpty))
        {
            RenderChart();
        }
    }

    private void OnChartCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RenderChart();

    /// <summary>
    /// Mục 6.2.5/11 (câu hỏi mở thư viện chart) — vẽ trực tiếp bằng <see cref="Rectangle"/>/<see cref="Canvas"/>
    /// thay vì thêm NuGet chart mới: sandbox dev không có màn hình để verify UI thật (Architecture/10
    /// mục 11 xác nhận đây là chi tiết implement, không chặn kiến trúc) — 0 dependency mới giảm rủi ro.
    /// </summary>
    private void RenderChart()
    {
        ChartCanvas.Children.Clear();
        if (ViewModel.IsChartEmpty)
        {
            return;
        }

        IReadOnlyList<DailyBlockCount> data = ViewModel.ChartData;
        uint max = data.Max(d => d.BlockedCount);
        if (max == 0)
        {
            return;
        }

        double width = ChartCanvas.ActualWidth > 0 ? ChartCanvas.ActualWidth : 400;
        double height = ChartCanvas.ActualHeight > 0 ? ChartCanvas.ActualHeight : 160;
        double slot = width / data.Count;
        double barWidth = Math.Max(2, slot * 0.6);
        double plotHeight = height - _chartLabelSpace; // chừa chỗ phía trên cho số đếm của cột cao nhất

        for (int i = 0; i < data.Count; i++)
        {
            uint count = data[i].BlockedCount;
            double barHeight = Math.Max(1, count / (double)max * (plotHeight - 4));
            double left = (i * slot) + ((slot - barWidth) / 2);
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = _barBrush,
                RadiusX = 3,
                RadiusY = 3,
            };
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, height - barHeight);

            // Yêu cầu chủ dự án 2026-10-01: hiệu ứng khi di chuột lên cột (sáng màu + tooltip ngày/số lần).
            rect.PointerEntered += (_, _) => rect.Fill = _barHoverBrush;
            rect.PointerExited += (_, _) => rect.Fill = _barBrush;
            ToolTipService.SetToolTip(rect, LocalizationService.GetFormatted("DashboardChartBarTooltipFormat", FormatChartDate(data[i].DateUtc), count));
            ChartCanvas.Children.Add(rect);

            // Yêu cầu chủ dự án 2026-10-01: hiện số lần chặn trên đầu mỗi cột có giá trị > 0.
            if (count > 0)
            {
                var label = new TextBlock
                {
                    Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture),
                    Width = slot,
                    TextAlignment = TextAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, i * slot);
                Canvas.SetTop(label, height - barHeight - _chartLabelSpace + 2);
                ChartCanvas.Children.Add(label);
            }
        }
    }

    private const double _chartLabelSpace = 20;
    private static readonly SolidColorBrush _barBrush = new(Microsoft.UI.Colors.IndianRed);
    private static readonly SolidColorBrush _barHoverBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 240, 128, 128));

    /// <summary>"yyyy-MM-dd" (UTC, từ Service) → "dd/MM" cho tooltip; giữ nguyên nếu không parse được.</summary>
    private static string FormatChartDate(string dateUtc) =>
        DateOnly.TryParseExact(dateUtc, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly d)
            ? d.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture)
            : dateUtc;
}

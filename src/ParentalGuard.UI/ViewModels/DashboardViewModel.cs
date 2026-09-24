using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.ViewModels;

public enum DashboardCardState
{
    Active,
    Paused,
    Error,
}

/// <summary>
/// `S2` Dashboard (Architecture/10-ui-architecture.md mục 6.2, ADR-120) — poll 5 giây qua
/// <see cref="DispatcherQueueTimer"/> khi trang đang hiển thị (<see cref="Start"/>/<see cref="Stop"/>
/// gọi từ <c>OnNavigatedTo</c>/<c>OnNavigatedFrom</c>, KHÔNG poll nền khi rời trang).
/// </summary>
public sealed partial class DashboardViewModel(
    IDashboardFacade dashboardFacade,
    IPauseFacade pauseFacade,
    IAuthPromptService authPromptService) : ObservableObject
{
    private const string PauseActionContext = "pause_monitoring";
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    // Mục 6.2.4, ADR-123 — ngưỡng UX tự quyết định, không phải số liệu bảo mật.
    private const long _diskSpaceWarningThresholdBytes = 500L * 1024 * 1024;

    private DispatcherQueueTimer? _timer;

    // Instance property (không static) để x:Bind trực tiếp qua `ViewModel.PauseDurationChoices`, cùng
    // mẫu hình mọi property khác trong file — tránh cú pháp x:Bind tới static member.
    private static readonly IReadOnlyList<PauseDurationChoice> _pauseDurationChoices =
    [
        new(PauseDurationOption.FifteenMinutes, LocalizationService.Get("PauseDuration15Min")),
        new(PauseDurationOption.ThirtyMinutes, LocalizationService.Get("PauseDuration30Min")),
        new(PauseDurationOption.OneHour, LocalizationService.Get("PauseDuration1Hour")),
        new(PauseDurationOption.FourHours, LocalizationService.Get("PauseDuration4Hours")),
        new(PauseDurationOption.EndOfDay, LocalizationService.Get("PauseDurationEndOfDay")),
    ];

    public IReadOnlyList<PauseDurationChoice> PauseDurationChoices => _pauseDurationChoices;

    [ObservableProperty]
    public partial PauseDurationChoice SelectedPauseDurationChoice { get; set; } = _pauseDurationChoices[2];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveState), nameof(IsPausedState), nameof(IsErrorState), nameof(StatusCardText))]
    public partial DashboardCardState CardState { get; set; } = DashboardCardState.Active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCardText))]
    public partial string PauseCountdownText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPaused))]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial bool ShowAnomalyBanner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchdogStatusText))]
    public partial bool WatchdogAlive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisionStatusText))]
    public partial bool VisionConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisionStatusText))]
    public partial bool VisionCpuFallback { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayStatusText))]
    public partial bool OverlayConnected { get; set; }

    [ObservableProperty]
    public partial bool DiskSpaceLow { get; set; }

    [ObservableProperty]
    public partial bool UsingFallbackConfig { get; set; }

    [ObservableProperty]
    public partial string VisionDiagnosticStateDetail { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChartEmpty), nameof(HasChartData), nameof(ChartSummaryText))]
    public partial ObservableCollection<DailyBlockCount> ChartData { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChartSummaryText))]
    public partial uint ChartRangeDays { get; set; } = 7;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentOpacity))]
    public partial bool ConnectionLost { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsActiveState => CardState == DashboardCardState.Active;

    public bool IsPausedState => CardState == DashboardCardState.Paused;

    public bool IsErrorState => CardState == DashboardCardState.Error;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    // x:Bind (WinUI 3) không hỗ trợ toán tử `!` trong markup (đã xác nhận qua build thật — lỗi parse
    // "token recognition error at: '!'") — expose thẳng property phủ định thay vì phủ định trong XAML.
    public bool IsNotPaused => !IsPaused;

    public bool IsNotBusy => !IsBusy;

    public bool HasChartData => !IsChartEmpty;

    public string StatusCardText => CardState switch
    {
        DashboardCardState.Paused => LocalizationService.GetFormatted("DashboardStatusPaused", PauseCountdownText),
        DashboardCardState.Error => LocalizationService.Get("DashboardStatusError"),
        _ => LocalizationService.Get("DashboardStatusActive"),
    };

    public string WatchdogStatusText => WatchdogAlive
        ? LocalizationService.Get("DashboardHealthWatchdogOk")
        : LocalizationService.Get("DashboardHealthWatchdogDown");

    /// <summary>Mục 6.2.4/`FE-041` — so khớp chuỗi con đơn giản trên <c>vision_diagnostic_state</c>, không tự diễn giải thêm.</summary>
    public string VisionStatusText => !VisionConnected
        ? LocalizationService.Get("DashboardHealthVisionDown")
        : VisionCpuFallback
            ? LocalizationService.Get("DashboardHealthVisionCpuFallback")
            : LocalizationService.Get("DashboardHealthVisionOk");

    public string OverlayStatusText => OverlayConnected
        ? LocalizationService.Get("DashboardHealthOverlayOk")
        : LocalizationService.Get("DashboardHealthOverlayDown");

    /// <summary>`FE-040` — toàn bộ <c>blocked_count=0</c> trong khoảng xem.</summary>
    public bool IsChartEmpty => ChartData.Count == 0 || ChartData.All(d => d.BlockedCount == 0);

    /// <summary>Mục 8 accessibility — tóm tắt text cho screen reader (chart tự vẽ không có accessibility tree đầy đủ).</summary>
    public string ChartSummaryText => LocalizationService.GetFormatted(
        "DashboardChartSummaryFormat", ChartRangeDays, ChartData.Sum(d => (long)d.BlockedCount));

    /// <summary>Mục 9 — giữ dữ liệu cũ hiển thị mờ nhẹ (không xoá trắng) khi mất kết nối giữa chừng.</summary>
    public double ContentOpacity => ConnectionLost ? 0.5 : 1.0;

    public void Start(DispatcherQueue dispatcherQueue)
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = _pollInterval;
        _timer.Tick += async (_, _) => await PollAsync().ConfigureAwait(true);

        _ = PollAsync();
        _ = LoadChartAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task PollAsync()
    {
        try
        {
            DashboardStatus status = await dashboardFacade.GetStatusAsync(CancellationToken.None).ConfigureAwait(true);
            ApplyStatus(status);
            ConnectionLost = false;
        }
        catch (UiIpcConnectionException)
        {
            // Mục 9 — không xoá dữ liệu cũ, tự thử lại ở tick kế tiếp (5s, mục 3.3).
            ConnectionLost = true;
        }
    }

    /// <summary>Internal — seam test-only (<c>InternalsVisibleTo</c> mục AssemblyInfo.cs), verify logic suy ra CardState/health-check không cần DispatcherQueue thật.</summary>
    internal void ApplyStatus(DashboardStatus status)
    {
        WatchdogAlive = status.WatchdogAlive;
        VisionConnected = status.VisionConnected;
        OverlayConnected = status.OverlayConnected;
        UsingFallbackConfig = status.UsingFallbackConfig;
        DiskSpaceLow = status.AuditLogFreeDiskBytes < _diskSpaceWarningThresholdBytes;
        VisionDiagnosticStateDetail = status.VisionDiagnosticState;
        VisionCpuFallback = status.VisionDiagnosticState.Contains("cpu-fallback", StringComparison.Ordinal);
        ShowAnomalyBanner = status.PauseAnomalyPendingAck;
        IsPaused = status.IsPaused;
        PauseCountdownText = status.IsPaused ? FormatCountdown(status.PauseExpiresAtUnixMs) : string.Empty;

        // Mục 6.2.1 — Error nếu bất kỳ kênh nào gián đoạn, kể cả khi đang Paused (sức khoẻ hệ thống
        // ưu tiên hiển thị hơn trạng thái pause).
        CardState = !status.WatchdogAlive || !status.VisionConnected || !status.OverlayConnected
            ? DashboardCardState.Error
            : status.IsPaused ? DashboardCardState.Paused : DashboardCardState.Active;
    }

    private static string FormatCountdown(long expiresAtUnixMs)
    {
        TimeSpan remaining = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtUnixMs) - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining.ToString(@"hh\:mm") : "00:00";
    }

    /// <summary>Mục 6.2.2 — mở `S5` (`pause_monitoring`) TRƯỚC khi gửi <c>PauseMonitoringRequest</c> thật.</summary>
    public async Task PauseAsync(XamlRoot xamlRoot)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            byte[]? actionToken = await authPromptService.ShowAuthPromptAsync(PauseActionContext, xamlRoot).ConfigureAwait(true);
            if (actionToken is null)
            {
                return;
            }

            await PauseWithTokenAsync(actionToken, xamlRoot, allowRetry: true).ConfigureAwait(true);
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Mục 6.5 — <c>action_token</c> hết hạn giữa lúc `S5` trả `SUCCESS` và lúc request thật gửi đi
    /// (race, vd chọn thời lượng Pause quá lâu) → tự mở lại `S5` từ đầu NGAY, không silent-retry với
    /// token cũ. <paramref name="allowRetry"/> giới hạn đúng 1 lần tự động retry (chống đệ quy vô hạn
    /// nếu `InvalidToken` lặp lại).
    /// </summary>
    private async Task PauseWithTokenAsync(byte[] actionToken, XamlRoot xamlRoot, bool allowRetry)
    {
        PauseMonitoringResult result = await pauseFacade.PauseMonitoringAsync(actionToken, SelectedPauseDurationChoice.Value, CancellationToken.None).ConfigureAwait(true);
        switch (result.Outcome)
        {
            case PauseOutcome.Success:
            case PauseOutcome.AlreadyPaused:
                await PollAsync().ConfigureAwait(true);
                break;
            case PauseOutcome.InvalidToken:
                ErrorMessage = LocalizationService.Get("DashboardActionTokenExpired");
                if (!allowRetry)
                {
                    break;
                }

                byte[]? retryToken = await authPromptService.ShowAuthPromptAsync(PauseActionContext, xamlRoot).ConfigureAwait(true);
                if (retryToken is null)
                {
                    break;
                }

                ErrorMessage = null;
                await PauseWithTokenAsync(retryToken, xamlRoot, allowRetry: false).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>Mục 6.2.2 — cùng cơ chế gate `S5` như <see cref="PauseAsync"/>.</summary>
    public async Task ResumeAsync(XamlRoot xamlRoot)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            byte[]? actionToken = await authPromptService.ShowAuthPromptAsync(PauseActionContext, xamlRoot).ConfigureAwait(true);
            if (actionToken is null)
            {
                return;
            }

            await ResumeWithTokenAsync(actionToken, xamlRoot, allowRetry: true).ConfigureAwait(true);
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Mục 6.5 — cùng cơ chế tự mở lại `S5` (1 lần) như <see cref="PauseWithTokenAsync"/>.</summary>
    private async Task ResumeWithTokenAsync(byte[] actionToken, XamlRoot xamlRoot, bool allowRetry)
    {
        ResumeMonitoringResult result = await pauseFacade.ResumeMonitoringAsync(actionToken, CancellationToken.None).ConfigureAwait(true);
        switch (result.Outcome)
        {
            case ResumeOutcome.Success:
            case ResumeOutcome.NotPaused:
                await PollAsync().ConfigureAwait(true);
                break;
            case ResumeOutcome.InvalidToken:
                ErrorMessage = LocalizationService.Get("DashboardActionTokenExpired");
                if (!allowRetry)
                {
                    break;
                }

                byte[]? retryToken = await authPromptService.ShowAuthPromptAsync(PauseActionContext, xamlRoot).ConfigureAwait(true);
                if (retryToken is null)
                {
                    break;
                }

                ErrorMessage = null;
                await ResumeWithTokenAsync(retryToken, xamlRoot, allowRetry: false).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>Mục 6.2.3 — không gọi lại field này tới lần poll kế tiếp (chỉ ẩn cục bộ ngay khi acknowledge thành công).</summary>
    [RelayCommand]
    private async Task AcknowledgeAnomalyAsync()
    {
        try
        {
            await dashboardFacade.AcknowledgePauseAnomalyAsync(CancellationToken.None).ConfigureAwait(true);
            ShowAnomalyBanner = false;
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Mục 6.2.5 — toggle 7/30 ngày gọi lại đây với <paramref name="rangeDays"/> mới.</summary>
    public async Task SetChartRangeAsync(uint rangeDays)
    {
        ChartRangeDays = rangeDays;
        await LoadChartAsync().ConfigureAwait(true);
    }

    private async Task LoadChartAsync()
    {
        try
        {
            IReadOnlyList<DailyBlockCount> data = await dashboardFacade.GetAuditChartAsync(ChartRangeDays, CancellationToken.None).ConfigureAwait(true);
            ChartData = new ObservableCollection<DailyBlockCount>(data);
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

public sealed record PauseDurationChoice(PauseDurationOption Value, string Label);

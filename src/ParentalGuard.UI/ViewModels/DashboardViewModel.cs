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
    /// <summary>Chưa nhận được <c>DashboardStatusResponse</c> đầu tiên — không suy đoán Active/Error.</summary>
    Checking,
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
    IAuthPromptService authPromptService,
    IConfigFacade? configFacade = null,
    IParentChallengePromptService? challengePrompt = null) : ObservableObject
{
    private const string PauseActionContext = "pause_monitoring";

    /// <summary>Chỉ để hiển thị đúng câu chữ ở `S5` ("tiếp tục giám sát") — Service vẫn nhận token "pause_monitoring" (xem AuthPromptViewModel).</summary>
    private const string ResumeActionContext = "resume_monitoring";
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
    [NotifyPropertyChangedFor(nameof(IsActiveState), nameof(IsPausedState), nameof(IsErrorState), nameof(IsCheckingState), nameof(StatusCardText))]
    public partial DashboardCardState CardState { get; set; } = DashboardCardState.Checking;

    /// <summary>
    /// Bug real-hardware 2026-10-01: trước response đầu tiên, 3 cờ health mặc định <c>false</c> hiện thành
    /// "mất kết nối" (kèm thẻ "Đang hoạt động") dù Vision/Overlay vẫn chạy — hiện "Đang kiểm tra…" thay vì đoán.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchdogStatusText), nameof(VisionStatusText), nameof(OverlayStatusText))]
    public partial bool IsStatusKnown { get; set; }

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
    [NotifyPropertyChangedFor(nameof(VisionStatusText))]
    public partial bool VisionPipelineError { get; set; }

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
    [NotifyPropertyChangedFor(nameof(IsChartEmpty), nameof(HasChartData), nameof(ChartSummaryText), nameof(ChartBars))]
    public partial ObservableCollection<DailyBlockCount> ChartData { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChartSummaryText), nameof(ChartBars), nameof(ChartTitleText))]
    public partial uint ChartRangeDays { get; set; } = 7;

    /// <summary>`FE-071a`: cột theo ngày (1 tuần/1 tháng) hoặc gộp tuần (3 tháng/6 tháng).</summary>
    public IReadOnlyList<ChartBar> ChartBars => ChartBucketer.Bucket(ChartData, ChartRangeDays);

    public string ChartTitleText => LocalizationService.Get(ChartBucketer.IsWeekly(ChartRangeDays) ? "DashboardChartTitleWeekly" : "DashboardChartTitleDaily");

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

    public bool IsCheckingState => CardState == DashboardCardState.Checking;

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
        DashboardCardState.Checking => LocalizationService.Get("DashboardStatusChecking"),
        _ => LocalizationService.Get("DashboardStatusActive"),
    };

    public string WatchdogStatusText => !IsStatusKnown ? LocalizationService.Get("DashboardHealthChecking") : WatchdogAlive
        ? LocalizationService.Get("DashboardHealthWatchdogOk")
        : LocalizationService.Get("DashboardHealthWatchdogDown");

    /// <summary>Mục 6.2.4/`FE-041` — so khớp chuỗi con đơn giản trên <c>vision_diagnostic_state</c>, không tự diễn giải thêm.</summary>
    public string VisionStatusText => !IsStatusKnown ? LocalizationService.Get("DashboardHealthChecking") : !VisionConnected
        ? LocalizationService.Get("DashboardHealthVisionDown")
        : VisionPipelineError
            ? LocalizationService.Get("DashboardHealthVisionPipelineError")
        : VisionCpuFallback
            ? LocalizationService.Get("DashboardHealthVisionCpuFallback")
            : LocalizationService.Get("DashboardHealthVisionOk");

    public string OverlayStatusText => !IsStatusKnown ? LocalizationService.Get("DashboardHealthChecking") : OverlayConnected
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

        // Pipe UI xử lý tuần tự — status TRƯỚC, biểu đồ SAU, để thẻ sức khoẻ không phải chờ quét audit.log.
        _ = PollThenLoadChartAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task PollThenLoadChartAsync()
    {
        await PollAsync().ConfigureAwait(true);
        await LoadChartAsync().ConfigureAwait(true);
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
        IsStatusKnown = true;
        WatchdogAlive = status.WatchdogAlive;
        VisionConnected = status.VisionConnected;
        OverlayConnected = status.OverlayConnected;
        UsingFallbackConfig = status.UsingFallbackConfig;
        DiskSpaceLow = status.AuditLogFreeDiskBytes < _diskSpaceWarningThresholdBytes;
        VisionDiagnosticStateDetail = status.VisionDiagnosticState;
        VisionCpuFallback = status.VisionDiagnosticState.Contains("cpu-fallback", StringComparison.Ordinal);
        // FE-041a (2026-10-01): bỏ khung "Chi tiết kỹ thuật" (chuỗi chẩn đoán thô vô nghĩa với phụ huynh) — thay bằng
        // cảnh báo rõ ràng ngay trên dòng Vision khi pipeline đang lỗi liên tiếp.
        VisionPipelineError = status.VisionDiagnosticState.Contains("pipeline-error", StringComparison.Ordinal);
        ShowAnomalyBanner = status.PauseAnomalyPendingAck;
        IsPaused = status.IsPaused;
        PauseCountdownText = status.IsPaused ? FormatCountdown(status.PauseExpiresAtUnixMs) : string.Empty;

        // Mục 6.2.1 — Error nếu kênh bảo vệ gián đoạn, kể cả khi đang Paused (sức khoẻ hệ thống ưu tiên hiển thị hơn
        // trạng thái pause). FE-042 (2026-10-05): "Máy tính đang được bảo vệ" chỉ cần Vision + Overlay — Watchdog lỗi
        // chỉ hiện ở phần Kiểm tra tình trạng, không làm mất trạng thái xanh.
        CardState = !status.VisionConnected || !status.OverlayConnected
            ? DashboardCardState.Error
            : status.IsPaused ? DashboardCardState.Paused : DashboardCardState.Active;
    }

    internal void ApplyPausedLocally(long pauseExpiresAtUnixMs)
    {
        IsPaused = true;
        PauseCountdownText = pauseExpiresAtUnixMs > 0 ? FormatCountdown(pauseExpiresAtUnixMs) : string.Empty;
        if (CardState != DashboardCardState.Error)
        {
            CardState = DashboardCardState.Paused;
        }
    }

    internal void ApplyResumedLocally()
    {
        IsPaused = false;
        PauseCountdownText = string.Empty;
        if (CardState == DashboardCardState.Paused)
        {
            CardState = DashboardCardState.Active;
        }
    }

    private static string FormatCountdown(long expiresAtUnixMs)
    {
        TimeSpan remaining = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtUnixMs) - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining.ToString(@"hh\:mm") : "00:00";
    }

    /// <summary>
    /// Mục 6.2.2 — mở `S5` (`pause_monitoring`) TRƯỚC khi gửi <c>PauseMonitoringRequest</c> thật. `PAUSE-041` (2026-10-07):
    /// "Bảo vệ cả phụ huynh" đang bật → thử thách TRƯỚC `S5` (action_token chỉ sống 15 giây, không đủ để làm thử thách sau).
    /// </summary>
    public async Task PauseAsync(XamlRoot xamlRoot)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            if (!await PassChallengeIfRequiredAsync(xamlRoot).ConfigureAwait(true))
            {
                return;
            }

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

    /// <summary>`PAUSE-041`: <c>true</c> nếu chế độ đang tắt hoặc đã vượt thử thách.</summary>
    private async Task<bool> PassChallengeIfRequiredAsync(XamlRoot xamlRoot)
    {
        if (configFacade is null || challengePrompt is null)
        {
            return true;
        }

        ConfigSnapshot config = await configFacade.GetConfigAsync(CancellationToken.None).ConfigureAwait(true);
        return !config.ParentProtectionEnabled || await challengePrompt.ShowChallengeAsync(xamlRoot).ConfigureAwait(true);
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
                // Phản hồi tức thì (yêu cầu chủ dự án 2026-10-01): đổi sang "Tiếp tục ngay" NGAY khi Service xác
                // nhận, không chờ lượt poll — poll ngay sau đó chỉ để đồng bộ lại các chỉ số khác.
                ApplyPausedLocally(result.PauseExpiresAtUnixMs);
                await PollAsync().ConfigureAwait(true);
                break;
            case PauseOutcome.ChallengeRequired:
                // Chế độ vừa được bật ở nơi khác, hoặc kết quả thử thách đã hết hạn 2 phút — bấm Tạm dừng lại từ đầu.
                ErrorMessage = LocalizationService.Get("DashboardChallengeRequired");
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
            byte[]? actionToken = await authPromptService.ShowAuthPromptAsync(ResumeActionContext, xamlRoot).ConfigureAwait(true);
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
                ApplyResumedLocally();
                await PollAsync().ConfigureAwait(true);
                break;
            case ResumeOutcome.InvalidToken:
                ErrorMessage = LocalizationService.Get("DashboardActionTokenExpired");
                if (!allowRetry)
                {
                    break;
                }

                byte[]? retryToken = await authPromptService.ShowAuthPromptAsync(ResumeActionContext, xamlRoot).ConfigureAwait(true);
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

    /// <summary>Mục 6.2.5/`FE-071a` — 1 tuần/1 tháng/3 tháng/6 tháng (7/30/90/180) gọi lại đây với <paramref name="rangeDays"/> mới.</summary>
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

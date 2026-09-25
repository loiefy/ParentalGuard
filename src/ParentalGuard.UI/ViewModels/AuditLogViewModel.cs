using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.ViewModels;

/// <summary>
/// `S3` Lịch sử / Audit log (Architecture/10-ui-architecture.md mục 6.3) — vào tab luôn hiện `S5`
/// (<c>action_context="view_audit_log"</c>) TRƯỚC khi render bất kỳ nội dung nào (<see cref="InitializeAsync"/>,
/// gọi từ <c>AuditLogPage.OnNavigatedTo</c>). Trang đầu (<c>page=0</c>) dùng <c>action_token</c> thật
/// vừa nhận; các trang kế tiếp gửi token rỗng (mục 6.3 — Service tự nhớ "đã qua gate" theo session pipe).
/// </summary>
public sealed partial class AuditLogViewModel(IAuditFacade auditFacade, IAuthPromptService authPromptService) : ObservableObject
{
    private const string ViewAuditLogActionContext = "view_audit_log";
    private const string ManageWhitelistActionContext = "manage_whitelist";
    private const uint PageSize = 50;
    private static readonly byte[] _noToken = [];

    private uint _nextPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowEntries))]
    public partial bool IsGated { get; set; }

    /// <summary>Đặt <c>true</c> khi user huỷ `S5` (hoặc <c>InvalidToken</c> không thể phục hồi ở trang đầu) — <c>AuditLogPage</c> quan sát để điều hướng lại `S2`.</summary>
    [ObservableProperty]
    public partial bool GateCancelled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowEntries))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoadingMore))]
    public partial bool IsLoadingMore { get; set; }

    // x:Bind (WinUI 3) không hỗ trợ toán tử `!` trong markup — expose thẳng phủ định (cùng mẫu hình DashboardViewModel.IsNotBusy).
    public bool IsNotLoadingMore => !IsLoadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowEntries))]
    public partial ObservableCollection<AuditLogRowViewData> Entries { get; set; } = [];

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>`FE-040` — chỉ tích cực coi là rỗng SAU KHI đã qua gate và tải xong trang đầu (không nhấp nháy lúc đang tải).</summary>
    public bool IsEmpty => IsGated && !IsBusy && Entries.Count == 0;

    public bool ShowEntries => IsGated && !IsBusy && Entries.Count > 0;

    /// <summary>Mục 6.3 — mở `S5` (`view_audit_log`) trước khi render nội dung; huỷ → <see cref="GateCancelled"/>=true.</summary>
    public async Task InitializeAsync(XamlRoot xamlRoot)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            byte[]? actionToken = await authPromptService.ShowAuthPromptAsync(ViewAuditLogActionContext, xamlRoot).ConfigureAwait(true);
            if (actionToken is null)
            {
                GateCancelled = true;
                return;
            }

            bool loaded = await LoadPageAsync(actionToken, page: 0).ConfigureAwait(true);
            if (loaded)
            {
                IsGated = true;
            }
            else
            {
                // Mục 6.5 — race hiếm (action_token hết hạn giữa lúc S5 trả SUCCESS và request thật gửi
                // đi): mở lại S5 từ đầu NGAY, không silent-retry với token cũ; huỷ ở đây coi như huỷ cả tab.
                byte[]? retryToken = await authPromptService.ShowAuthPromptAsync(ViewAuditLogActionContext, xamlRoot).ConfigureAwait(true);
                if (retryToken is null || !await LoadPageAsync(retryToken, page: 0).ConfigureAwait(true))
                {
                    GateCancelled = true;
                    return;
                }

                IsGated = true;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
            GateCancelled = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Cuộn tới cuối / nút "Tải thêm" (mục 6.3) — gửi token rỗng, Service tự nhớ đã qua gate theo session pipe.</summary>
    public async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoadingMore)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            bool loaded = await LoadPageAsync(_noToken, _nextPage).ConfigureAwait(true);
            if (!loaded)
            {
                HasMore = false;
                ErrorMessage = LocalizationService.Get("AuditLogSessionExpired");
            }
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>Trả <c>true</c> nếu <c>SUCCESS</c> (dòng đã được thêm vào <see cref="Entries"/>), <c>false</c> nếu <c>InvalidToken</c>.</summary>
    private async Task<bool> LoadPageAsync(byte[] actionToken, uint page)
    {
        try
        {
            AuditLogFetchResult result = await auditFacade.GetAuditLogAsync(actionToken, page, PageSize, CancellationToken.None).ConfigureAwait(true);
            if (result.Outcome == AuditLogQueryOutcome.InvalidToken)
            {
                return false;
            }

            foreach (AuditLogEntry entry in result.Entries)
            {
                Entries.Add(ToRowViewData(entry));
            }

            HasMore = result.HasMore;
            _nextPage = page + 1;
            return true;
        }
        finally
        {
            if (actionToken.Length > 0)
            {
                CryptographicOperations.ZeroMemory(actionToken);
            }
        }
    }

    /// <summary>Nút "Đánh dấu sai" trên dòng `ContentBlocked` (mục 6.3, `MISC-030`) → `S5` (`manage_whitelist`) riêng.</summary>
    public async Task MarkFalsePositiveAsync(string processName, XamlRoot xamlRoot)
    {
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            byte[]? actionToken = await authPromptService.ShowAuthPromptAsync(ManageWhitelistActionContext, xamlRoot).ConfigureAwait(true);
            if (actionToken is null)
            {
                return;
            }

            await MarkFalsePositiveWithTokenAsync(actionToken, processName, xamlRoot, allowRetry: true).ConfigureAwait(true);
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task MarkFalsePositiveWithTokenAsync(byte[] actionToken, string processName, XamlRoot xamlRoot, bool allowRetry)
    {
        try
        {
            MarkFalsePositiveOutcome outcome = await auditFacade.MarkFalsePositiveAsync(actionToken, processName, CancellationToken.None).ConfigureAwait(true);
            switch (outcome)
            {
                case MarkFalsePositiveOutcome.Success:
                    StatusMessage = LocalizationService.GetFormatted("AuditMarkFalsePositiveSuccessFormat", processName);
                    break;
                case MarkFalsePositiveOutcome.AlreadyListed:
                    StatusMessage = LocalizationService.GetFormatted("AuditMarkFalsePositiveAlreadyListedFormat", processName);
                    break;
                case MarkFalsePositiveOutcome.InvalidToken:
                    ErrorMessage = LocalizationService.Get("DashboardActionTokenExpired");
                    if (!allowRetry)
                    {
                        break;
                    }

                    byte[]? retryToken = await authPromptService.ShowAuthPromptAsync(ManageWhitelistActionContext, xamlRoot).ConfigureAwait(true);
                    if (retryToken is null)
                    {
                        break;
                    }

                    ErrorMessage = null;
                    await MarkFalsePositiveWithTokenAsync(retryToken, processName, xamlRoot, allowRetry: false).ConfigureAwait(true);
                    break;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actionToken);
        }
    }

    private static AuditLogRowViewData ToRowViewData(AuditLogEntry entry)
    {
        bool isContentBlocked = entry.EventType == "ContentBlocked";
        return new AuditLogRowViewData(
            DateTimeOffset.FromUnixTimeMilliseconds(entry.TsUnixMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            EventTypeDisplay.ToText(entry.EventType),
            isContentBlocked,
            entry.ProcessName,
            isContentBlocked ? LocalizationService.GetFormatted("AuditProcessNameFormat", entry.ProcessName) : string.Empty,
            isContentBlocked ? LocalizationService.GetFormatted("AuditRiskScoreFormat", entry.RiskScore) : string.Empty);
    }
}

/// <summary>1 dòng lịch sử đã dựng sẵn text hiển thị (mục 6.3) — thuần POCO cho binding, không mang logic.</summary>
public sealed record AuditLogRowViewData(
    string TimestampText,
    string EventTypeText,
    bool IsContentBlocked,
    string ProcessName,
    string ProcessDisplayText,
    string RiskScoreDisplayText);

/// <summary>
/// Bảng tra cứu <c>event_type</c> → text hiển thị tiếng Việt (Architecture/04-data-architecture.md mục
/// 5.1) — thuần mapping hiển thị, không phải nghiệp vụ (mục 6.3). Giá trị không có trong bảng (chưa
/// biết trước / thêm ở Đợt sau) hiển thị nguyên văn literal — không rớt lỗi, không ẩn thông tin.
/// </summary>
internal static class EventTypeDisplay
{
    private static readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal)
    {
        ["ServiceStarted"] = "AuditEventTypeServiceStarted",
        ["MonitoringToggled"] = "AuditEventTypeMonitoringToggled",
        ["PauseActivated"] = "AuditEventTypePauseActivated",
        ["PauseResumed"] = "AuditEventTypePauseResumed",
        ["PauseFrequencyAnomalyDetected"] = "AuditEventTypePauseFrequencyAnomalyDetected",
        ["ContentBlocked"] = "AuditEventTypeContentBlocked",
        ["ForceCloseRequested"] = "AuditEventTypeForceCloseRequested",
        ["AuthAttempt"] = "AuditEventTypeAuthAttempt",
        ["ProcessRestarted"] = "AuditEventTypeProcessRestarted",
        ["ConfigChanged"] = "AuditEventTypeConfigChanged",
        ["ConfigFallbackTriggered"] = "AuditEventTypeConfigFallbackTriggered",
        ["AttackPatternDetected"] = "AuditEventTypeAttackPatternDetected",
        ["AuditChainBrokenDetected"] = "AuditEventTypeAuditChainBrokenDetected",
        ["SoftwareUpdated"] = "AuditEventTypeSoftwareUpdated",
        ["VisionNetworkBlocked"] = "AuditEventTypeVisionNetworkBlocked",
        ["AuthBruteForceThresholdReached"] = "AuditEventTypeAuthBruteForceThresholdReached",
        ["TamperDetected"] = "AuditEventTypeTamperDetected",
        ["UninstallInitiated"] = "AuditEventTypeUninstallInitiated",
        ["UninstallPartialFailure"] = "AuditEventTypeUninstallPartialFailure",
        ["WFPFiltersRemoved"] = "AuditEventTypeWfpFiltersRemoved",
    };

    public static string ToText(string eventType) => _keys.TryGetValue(eventType, out string? key) ? LocalizationService.Get(key) : eventType;
}

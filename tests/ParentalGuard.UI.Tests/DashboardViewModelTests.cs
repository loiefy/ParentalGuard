using Microsoft.UI.Xaml;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `DashboardViewModel` (Architecture/10-ui-architecture.md mục 6.2) — verify logic suy ra
/// CardState/health-check (mục 6.2.1/6.2.4) qua <c>ApplyStatus</c> (internal, seam test-only) và các
/// hành động không cần <c>XamlRoot</c> (banner ack, biểu đồ) qua fake facade — không dựng
/// <c>DispatcherQueue</c>/<c>ContentDialog</c> thật (mục 3.3 dùng UI thread thật, ngoài phạm vi unit test).
/// </summary>
public sealed class DashboardViewModelTests
{
    private static DashboardViewModel CreateViewModel(
        FakeDashboardFacade? dashboardFacade = null,
        FakePauseFacade? pauseFacade = null,
        IAuthPromptService? authPromptService = null)
    {
        return new DashboardViewModel(
            dashboardFacade ?? new FakeDashboardFacade(),
            pauseFacade ?? new FakePauseFacade(),
            authPromptService ?? new NavigationService(new FakeAuthFacade()));
    }

    private static readonly DashboardStatus _healthyActiveStatus = new(
        WatchdogAlive: true, VisionConnected: true, VisionDiagnosticState: "alive", OverlayConnected: true,
        UsingFallbackConfig: false, AuditLogFreeDiskBytes: 1_000_000_000, PauseAnomalyPendingAck: false,
        IsPaused: false, PauseExpiresAtUnixMs: 0);

    [Fact]
    public void ApplyStatus_AllChannelsHealthyAndNotPaused_YieldsActiveState()
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyStatus(new DashboardStatus(
            WatchdogAlive: true, VisionConnected: true, VisionDiagnosticState: "alive", OverlayConnected: true,
            UsingFallbackConfig: false, AuditLogFreeDiskBytes: 1_000_000_000, PauseAnomalyPendingAck: false,
            IsPaused: false, PauseExpiresAtUnixMs: 0));

        Assert.Equal(DashboardCardState.Active, viewModel.CardState);
        Assert.True(viewModel.IsActiveState);
        Assert.False(viewModel.IsPaused);
        Assert.False(viewModel.VisionCpuFallback);
        Assert.False(viewModel.DiskSpaceLow);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ApplyStatus_AnyChannelDown_YieldsErrorState_EvenWhenPaused(bool watchdogAlive, bool visionConnected, bool overlayConnected)
    {
        var viewModel = CreateViewModel();

        // Mục 6.2.1 — Error phải thắng Paused khi bất kỳ kênh nào gián đoạn (sức khoẻ hệ thống ưu
        // tiên hiển thị hơn trạng thái pause).
        viewModel.ApplyStatus(new DashboardStatus(
            WatchdogAlive: watchdogAlive, VisionConnected: visionConnected, VisionDiagnosticState: "alive", OverlayConnected: overlayConnected,
            UsingFallbackConfig: false, AuditLogFreeDiskBytes: 1_000_000_000, PauseAnomalyPendingAck: false,
            IsPaused: true, PauseExpiresAtUnixMs: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));

        Assert.Equal(DashboardCardState.Error, viewModel.CardState);
        Assert.True(viewModel.IsErrorState);
    }

    [Fact]
    public void ApplyStatus_PausedWithAllChannelsHealthy_YieldsPausedStateWithCountdown()
    {
        var viewModel = CreateViewModel();
        long expiresAt = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds();

        viewModel.ApplyStatus(new DashboardStatus(
            WatchdogAlive: true, VisionConnected: true, VisionDiagnosticState: "alive", OverlayConnected: true,
            UsingFallbackConfig: false, AuditLogFreeDiskBytes: 1_000_000_000, PauseAnomalyPendingAck: false,
            IsPaused: true, PauseExpiresAtUnixMs: expiresAt));

        Assert.Equal(DashboardCardState.Paused, viewModel.CardState);
        Assert.True(viewModel.IsPaused);
        Assert.False(viewModel.IsNotPaused);
        Assert.NotEqual(string.Empty, viewModel.PauseCountdownText);
        Assert.Contains(viewModel.PauseCountdownText, viewModel.StatusCardText, StringComparison.Ordinal);
    }

    /// <summary>Mục 6.2.4/`FE-041` — so khớp chuỗi con `"cpu-fallback"` đơn giản, không parse cấu trúc.</summary>
    [Theory]
    [InlineData("alive", false)]
    [InlineData("ep=cpu-fallback", true)]
    public void ApplyStatus_DetectsCpuFallbackBySubstringMatch(string diagnosticState, bool expectFallback)
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyStatus(new DashboardStatus(
            WatchdogAlive: true, VisionConnected: true, VisionDiagnosticState: diagnosticState, OverlayConnected: true,
            UsingFallbackConfig: false, AuditLogFreeDiskBytes: 1_000_000_000, PauseAnomalyPendingAck: false,
            IsPaused: false, PauseExpiresAtUnixMs: 0));

        Assert.Equal(expectFallback, viewModel.VisionCpuFallback);
        Assert.Equal(diagnosticState, viewModel.VisionDiagnosticStateDetail);
    }

    /// <summary>ADR-123 — ngưỡng 500 MB.</summary>
    [Theory]
    [InlineData(500L * 1024 * 1024, false)]
    [InlineData((500L * 1024 * 1024) - 1, true)]
    public void ApplyStatus_DiskSpaceThreshold_500MB(long freeBytes, bool expectLow)
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyStatus(new DashboardStatus(
            WatchdogAlive: true, VisionConnected: true, VisionDiagnosticState: "alive", OverlayConnected: true,
            UsingFallbackConfig: false, AuditLogFreeDiskBytes: freeBytes, PauseAnomalyPendingAck: false,
            IsPaused: false, PauseExpiresAtUnixMs: 0));

        Assert.Equal(expectLow, viewModel.DiskSpaceLow);
    }

    [Fact]
    public async Task AcknowledgeAnomalyCommand_ClearsBannerOnSuccess()
    {
        var dashboardFacade = new FakeDashboardFacade();
        var viewModel = CreateViewModel(dashboardFacade);
        viewModel.ApplyStatus(new DashboardStatus(true, true, "alive", true, false, 1_000_000_000, PauseAnomalyPendingAck: true, false, 0));
        Assert.True(viewModel.ShowAnomalyBanner);

        await viewModel.AcknowledgeAnomalyCommand.ExecuteAsync(null);

        Assert.False(viewModel.ShowAnomalyBanner);
        Assert.True(dashboardFacade.AcknowledgeCalled);
    }

    [Fact]
    public async Task SetChartRangeAsync_LoadsChartDataAndUpdatesEmptyFlag()
    {
        var dashboardFacade = new FakeDashboardFacade
        {
            ChartData = [new DailyBlockCount("2026-09-20", 3), new DailyBlockCount("2026-09-21", 0)],
        };
        var viewModel = CreateViewModel(dashboardFacade);

        await viewModel.SetChartRangeAsync(30);

        Assert.Equal(30u, dashboardFacade.LastRequestedRangeDays);
        Assert.Equal(2, viewModel.ChartData.Count);
        Assert.False(viewModel.IsChartEmpty);
        Assert.True(viewModel.HasChartData);
    }

    [Fact]
    public async Task SetChartRangeAsync_AllZeroCounts_IsChartEmptyTrue()
    {
        var dashboardFacade = new FakeDashboardFacade
        {
            ChartData = [new DailyBlockCount("2026-09-20", 0), new DailyBlockCount("2026-09-21", 0)],
        };
        var viewModel = CreateViewModel(dashboardFacade);

        await viewModel.SetChartRangeAsync(7);

        Assert.True(viewModel.IsChartEmpty);
        Assert.False(viewModel.HasChartData);
    }

    /// <summary>Mục 6.5 — `InvalidToken` → tự mở lại `S5` NGAY (không chờ user bấm lại nút), token mới thành công → hoàn tất `PauseMonitoringRequest` gốc.</summary>
    [Fact]
    public async Task PauseAsync_InvalidToken_ReopensS5Automatically_SucceedsWithNewToken()
    {
        byte[] firstToken = [1];
        byte[] retryToken = [2];
        var authPromptService = new FakeAuthPromptService(firstToken, retryToken);
        var pauseFacade = new FakePauseFacade(pauseOutcomes: [PauseOutcome.InvalidToken, PauseOutcome.Success]);
        var dashboardFacade = new FakeDashboardFacade { Status = _healthyActiveStatus };
        var viewModel = CreateViewModel(dashboardFacade, pauseFacade, authPromptService);

        await viewModel.PauseAsync(null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Equal(2, pauseFacade.PauseTokensReceived.Count);
        Assert.Equal(firstToken, pauseFacade.PauseTokensReceived[0]);
        Assert.Equal(retryToken, pauseFacade.PauseTokensReceived[1]);
        Assert.Null(viewModel.ErrorMessage);
    }

    /// <summary>Mục 6.5 — user huỷ dialog `S5` mở lại (Cancel) → dừng, giữ `ErrorMessage`, không gửi thêm request.</summary>
    [Fact]
    public async Task PauseAsync_InvalidToken_UserCancelsRetryDialog_StopsWithErrorMessage()
    {
        byte[] firstToken = [1];
        var authPromptService = new FakeAuthPromptService(firstToken, null);
        var pauseFacade = new FakePauseFacade(pauseOutcomes: [PauseOutcome.InvalidToken]);
        var viewModel = CreateViewModel(pauseFacade: pauseFacade, authPromptService: authPromptService);

        await viewModel.PauseAsync(null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Single(pauseFacade.PauseTokensReceived);
        Assert.False(string.IsNullOrEmpty(viewModel.ErrorMessage));
    }

    /// <summary>Mục 6.5 — retry cũng `InvalidToken` → dừng hẳn sau đúng 1 lần tự động retry, không lặp mãi.</summary>
    [Fact]
    public async Task PauseAsync_InvalidToken_RetryAlsoInvalidToken_StopsAfterOneRetry()
    {
        byte[] firstToken = [1];
        byte[] retryToken = [2];
        var authPromptService = new FakeAuthPromptService(firstToken, retryToken);
        var pauseFacade = new FakePauseFacade(pauseOutcomes: [PauseOutcome.InvalidToken, PauseOutcome.InvalidToken]);
        var viewModel = CreateViewModel(pauseFacade: pauseFacade, authPromptService: authPromptService);

        await viewModel.PauseAsync(null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Equal(2, pauseFacade.PauseTokensReceived.Count);
        Assert.False(string.IsNullOrEmpty(viewModel.ErrorMessage));
    }

    /// <summary>Mục 6.5 — cùng luồng retry cho Resume.</summary>
    [Fact]
    public async Task ResumeAsync_InvalidToken_ReopensS5Automatically_SucceedsWithNewToken()
    {
        byte[] firstToken = [1];
        byte[] retryToken = [2];
        var authPromptService = new FakeAuthPromptService(firstToken, retryToken);
        var pauseFacade = new FakePauseFacade(resumeOutcomes: [ResumeOutcome.InvalidToken, ResumeOutcome.Success]);
        var dashboardFacade = new FakeDashboardFacade { Status = _healthyActiveStatus };
        var viewModel = CreateViewModel(dashboardFacade, pauseFacade, authPromptService);

        await viewModel.ResumeAsync(null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Equal(2, pauseFacade.ResumeTokensReceived.Count);
        Assert.Equal(firstToken, pauseFacade.ResumeTokensReceived[0]);
        Assert.Equal(retryToken, pauseFacade.ResumeTokensReceived[1]);
        Assert.Null(viewModel.ErrorMessage);
    }

    /// <summary>Mục 6.5 — Resume: user huỷ dialog mở lại → dừng, giữ `ErrorMessage`.</summary>
    [Fact]
    public async Task ResumeAsync_InvalidToken_UserCancelsRetryDialog_StopsWithErrorMessage()
    {
        byte[] firstToken = [1];
        var authPromptService = new FakeAuthPromptService(firstToken, null);
        var pauseFacade = new FakePauseFacade(resumeOutcomes: [ResumeOutcome.InvalidToken]);
        var viewModel = CreateViewModel(pauseFacade: pauseFacade, authPromptService: authPromptService);

        await viewModel.ResumeAsync(null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Single(pauseFacade.ResumeTokensReceived);
        Assert.False(string.IsNullOrEmpty(viewModel.ErrorMessage));
    }

    private sealed class FakeDashboardFacade : IDashboardFacade
    {
        public bool AcknowledgeCalled { get; private set; }

        public uint LastRequestedRangeDays { get; private set; }

        public IReadOnlyList<DailyBlockCount> ChartData { get; set; } = [];

        /// <summary>Null (mặc định) = test không gọi <c>PollAsync</c> nên giữ hành vi cũ (throw nếu lỡ gọi).</summary>
        public DashboardStatus? Status { get; set; }

        public Task<DashboardStatus> GetStatusAsync(CancellationToken cancellationToken)
            => Status is null ? throw new NotSupportedException() : Task.FromResult(Status);

        public Task AcknowledgePauseAnomalyAsync(CancellationToken cancellationToken)
        {
            AcknowledgeCalled = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DailyBlockCount>> GetAuditChartAsync(uint rangeDays, CancellationToken cancellationToken)
        {
            LastRequestedRangeDays = rangeDays;
            return Task.FromResult(ChartData);
        }
    }

    /// <summary>Trả lần lượt outcome trong <c>_pauseOutcomes</c>/<c>_resumeOutcomes</c> (mỗi lần gọi 1 outcome kế tiếp), ghi lại token nhận được — verify luồng retry mục 6.5.</summary>
    private sealed class FakePauseFacade(
        IEnumerable<PauseOutcome>? pauseOutcomes = null,
        IEnumerable<ResumeOutcome>? resumeOutcomes = null) : IPauseFacade
    {
        private readonly Queue<PauseOutcome> _pauseOutcomes = new(pauseOutcomes ?? []);
        private readonly Queue<ResumeOutcome> _resumeOutcomes = new(resumeOutcomes ?? []);

        public List<byte[]> PauseTokensReceived { get; } = [];

        public List<byte[]> ResumeTokensReceived { get; } = [];

        public Task<PauseMonitoringResult> PauseMonitoringAsync(byte[] actionToken, PauseDurationOption duration, CancellationToken cancellationToken)
        {
            PauseTokensReceived.Add(actionToken);
            PauseOutcome outcome = _pauseOutcomes.Count > 0 ? _pauseOutcomes.Dequeue() : throw new NotSupportedException();
            return Task.FromResult(new PauseMonitoringResult(outcome, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));
        }

        public Task<ResumeMonitoringResult> ResumeMonitoringAsync(byte[] actionToken, CancellationToken cancellationToken)
        {
            ResumeTokensReceived.Add(actionToken);
            ResumeOutcome outcome = _resumeOutcomes.Count > 0 ? _resumeOutcomes.Dequeue() : throw new NotSupportedException();
            return Task.FromResult(new ResumeMonitoringResult(outcome));
        }
    }

    /// <summary>Trả lần lượt token trong <c>_tokens</c> mỗi lần <c>ShowAuthPromptAsync</c> được gọi (null = user huỷ dialog); đếm số lần gọi để verify tự mở lại `S5`.</summary>
    private sealed class FakeAuthPromptService(params byte[]?[] tokens) : IAuthPromptService
    {
        private readonly Queue<byte[]?> _tokens = new(tokens);

        public int CallCount { get; private set; }

        public Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot)
        {
            CallCount++;
            return Task.FromResult(_tokens.Count > 0 ? _tokens.Dequeue() : null);
        }
    }

    private sealed class FakeAuthFacade : IAuthFacade
    {
        public Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ChangePasswordResult> ChangePasswordAsync(byte[] oldPasswordUtf8Pinned, byte[] newPasswordUtf8Pinned, bool regenerateRecoveryKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

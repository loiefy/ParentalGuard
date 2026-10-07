using Microsoft.UI.Xaml;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>`PAUSE-040`–`PAUSE-043` phía UI (2026-10-07): thử thách đứng TRƯỚC `S5` khi tạm dừng; tắt chế độ cần thử thách.</summary>
public sealed class ParentProtectionUiTests
{
    private static DashboardViewModel CreateDashboard(bool protectionEnabled, StubChallengePrompt challenge, StubAuthPrompt auth, StubPauseFacade pause) =>
        new(new StubDashboardFacade(), pause, auth, new StubConfigFacade { Enabled = protectionEnabled }, challenge);

    [Fact]
    public async Task Pause_ProtectionOff_NoChallenge()
    {
        var challenge = new StubChallengePrompt(true);
        var auth = new StubAuthPrompt();
        var pause = new StubPauseFacade(PauseOutcome.Success);

        await CreateDashboard(false, challenge, auth, pause).PauseAsync(null!);

        Assert.Equal(0, challenge.Calls);
        Assert.Equal(1, auth.Calls);
        Assert.Equal(1, pause.Calls);
    }

    [Fact]
    public async Task Pause_ProtectionOn_ChallengeCancelled_NeverAsksPasswordOrPauses()
    {
        var challenge = new StubChallengePrompt(false);
        var auth = new StubAuthPrompt();
        var pause = new StubPauseFacade(PauseOutcome.Success);

        await CreateDashboard(true, challenge, auth, pause).PauseAsync(null!);

        Assert.Equal(1, challenge.Calls);
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, pause.Calls);
    }

    [Fact]
    public async Task Pause_ProtectionOn_ChallengePassed_ThenPasswordThenPause()
    {
        var challenge = new StubChallengePrompt(true);
        var auth = new StubAuthPrompt();
        var pause = new StubPauseFacade(PauseOutcome.Success);
        DashboardViewModel vm = CreateDashboard(true, challenge, auth, pause);

        await vm.PauseAsync(null!);

        Assert.Equal(1, challenge.Calls);
        Assert.Equal(1, auth.Calls);
        Assert.Equal(1, pause.Calls);
        Assert.True(vm.IsPaused);
    }

    [Fact]
    public async Task Pause_ServiceSaysChallengeRequired_ShowsErrorNotPaused()
    {
        var pause = new StubPauseFacade(PauseOutcome.ChallengeRequired);
        DashboardViewModel vm = CreateDashboard(false, new StubChallengePrompt(true), new StubAuthPrompt(), pause);

        await vm.PauseAsync(null!);

        Assert.False(vm.IsPaused);
        Assert.Equal(LocalizationService.Get("DashboardChallengeRequired"), vm.ErrorMessage);
    }

    [Fact]
    public async Task Settings_DisableRequiresChallenge_EnableDoesNot()
    {
        var facade = new StubProtectionFacade();
        var challenge = new StubChallengePrompt(false);
        var vm = new SettingsViewModel(new StubConfigFacade { Enabled = true }, null!, new StubAuthPrompt(), null, facade, challenge);
        await vm.InitializeAsync(CancellationToken.None);
        Assert.True(vm.ParentProtectionEnabled);

        Assert.False(await vm.SetParentProtectionAsync(false, null!)); // huỷ thử thách → không gửi gì
        Assert.Empty(facade.Requests);
        Assert.True(vm.ParentProtectionEnabled);

        challenge.Result = true;
        Assert.True(await vm.SetParentProtectionAsync(false, null!));
        Assert.Equal([false], facade.Requests);
        Assert.False(vm.ParentProtectionEnabled);

        Assert.True(await vm.SetParentProtectionAsync(true, null!));
        Assert.Equal(2, challenge.Calls); // bật lại không cần thử thách
        Assert.True(vm.ParentProtectionEnabled);
    }

    private sealed class StubChallengePrompt(bool result) : IParentChallengePromptService
    {
        public bool Result { get; set; } = result;

        public int Calls { get; private set; }

        public Task<bool> ShowChallengeAsync(XamlRoot xamlRoot)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubAuthPrompt : IAuthPromptService
    {
        public int Calls { get; private set; }

        public Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot)
        {
            Calls++;
            return Task.FromResult<byte[]?>([1]);
        }
    }

    private sealed class StubPauseFacade(PauseOutcome outcome) : IPauseFacade
    {
        public int Calls { get; private set; }

        public Task<PauseMonitoringResult> PauseMonitoringAsync(byte[] actionToken, PauseDurationOption duration, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new PauseMonitoringResult(outcome, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));
        }

        public Task<ResumeMonitoringResult> ResumeMonitoringAsync(byte[] actionToken, CancellationToken cancellationToken) =>
            Task.FromResult(new ResumeMonitoringResult(ResumeOutcome.Success));
    }

    private sealed class StubDashboardFacade : IDashboardFacade
    {
        public Task<DashboardStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new DashboardStatus(
            WatchdogAlive: true, VisionConnected: true, VisionDiagnosticState: "alive", OverlayConnected: true,
            UsingFallbackConfig: false, AuditLogFreeDiskBytes: 1_000_000_000, PauseAnomalyPendingAck: false,
            IsPaused: true, PauseExpiresAtUnixMs: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));

        public Task AcknowledgePauseAnomalyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<DailyBlockCount>> GetAuditChartAsync(uint rangeDays, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DailyBlockCount>>([]);
    }

    private sealed class StubConfigFacade : IConfigFacade
    {
        public bool Enabled { get; init; }

        public Task<ConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ConfigSnapshot(string.Empty, [], PerformanceModeOption.Balanced, Enabled));

        public Task<ConfigUpdateOutcome> UpdateOverlayMessageAsync(string overlayMessage, PerformanceModeOption currentPerformanceMode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConfigUpdateOutcome> UpdatePerformanceModeAsync(string currentOverlayMessage, PerformanceModeOption performanceMode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RemoveWhitelistOutcome> RemoveWhitelistEntryAsync(byte[] actionToken, string processName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WhitelistResetOutcome> ResetWhitelistAsync(byte[] actionToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubProtectionFacade : IParentProtectionFacade
    {
        public List<bool> Requests { get; } = [];

        public Task<ChallengeStart> StartChallengeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ChallengeSubmitResult> SubmitChallengeAsync(IReadOnlyList<int> answers, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SetParentProtectionOutcome> SetParentProtectionAsync(bool enabled, CancellationToken cancellationToken)
        {
            Requests.Add(enabled);
            return Task.FromResult(SetParentProtectionOutcome.Success);
        }

        public Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}

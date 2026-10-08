using Microsoft.UI.Xaml;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>`PAUSE-044`–`PAUSE-047` phía UI (2026-10-08): mật khẩu TRƯỚC rồi trò chơi nhảy rào; tắt/giảm độ khó phải chơi.</summary>
public sealed class ParentProtectionUiTests
{
    private static DashboardViewModel CreateDashboard(bool protectionEnabled, StubGameService game, StubAuthPrompt auth, StubPauseFacade pause) =>
        new(new StubDashboardFacade(), pause, auth, new StubConfigFacade { Enabled = protectionEnabled }, game);

    [Fact]
    public async Task Pause_ProtectionOff_NoGame_DirectPause()
    {
        var game = new StubGameService(ParentGameOutcome.Paused);
        var auth = new StubAuthPrompt();
        var pause = new StubPauseFacade(PauseOutcome.Success);

        await CreateDashboard(false, game, auth, pause).PauseAsync(null!);

        Assert.Equal(0, game.PauseCalls);
        Assert.Equal(1, auth.Calls);
        Assert.Equal(1, pause.Calls);
    }

    [Fact]
    public async Task Pause_ProtectionOn_PasswordFirst_ThenGame_WinPauses_NoDirectPauseRequest()
    {
        var game = new StubGameService(ParentGameOutcome.Paused);
        var auth = new StubAuthPrompt();
        var pause = new StubPauseFacade(PauseOutcome.Success);
        DashboardViewModel vm = CreateDashboard(true, game, auth, pause);

        await vm.PauseAsync(null!);

        Assert.Equal(1, auth.Calls);
        Assert.Equal(1, game.PauseCalls);
        Assert.Equal([1], game.LastToken);
        Assert.Equal(PauseDurationOption.OneHour, game.LastDuration);
        Assert.Equal(0, pause.Calls); // Service tự tạm dừng khi về đích
        Assert.True(vm.IsPaused);
    }

    [Fact]
    public async Task Pause_ProtectionOn_PasswordCancelled_NoGame()
    {
        var game = new StubGameService(ParentGameOutcome.Paused);
        var auth = new StubAuthPrompt { Token = null };

        await CreateDashboard(true, game, auth, new StubPauseFacade(PauseOutcome.Success)).PauseAsync(null!);

        Assert.Equal(0, game.PauseCalls);
    }

    [Theory]
    [InlineData(ParentGameOutcome.Lost, "DashboardGameNotPaused")]
    [InlineData(ParentGameOutcome.TooFast, "GameResultTooFast")]
    [InlineData(ParentGameOutcome.InvalidToken, "DashboardActionTokenExpired")]
    public async Task Pause_ProtectionOn_GameNotWon_NotPaused_ShowsReason(ParentGameOutcome outcome, string messageKey)
    {
        DashboardViewModel vm = CreateDashboard(true, new StubGameService(outcome), new StubAuthPrompt(), new StubPauseFacade(PauseOutcome.Success));

        await vm.PauseAsync(null!);

        Assert.False(vm.IsPaused);
        Assert.Equal(LocalizationService.Get(messageKey), vm.ErrorMessage);
    }

    [Fact]
    public async Task Settings_DisableNeedsGame_LoseKeepsOn_WinTurnsOff()
    {
        var facade = new StubProtectionFacade();
        var game = new StubGameService(ParentGameOutcome.Lost);
        var vm = new SettingsViewModel(new StubConfigFacade { Enabled = true }, null!, new StubAuthPrompt(), null, facade, game);
        await vm.InitializeAsync(CancellationToken.None);

        Assert.False(await vm.SetParentProtectionAsync(false));
        Assert.True(vm.ParentProtectionEnabled);
        Assert.Equal(LocalizationService.Get("SettingsGameNotApplied"), vm.ParentProtectionError);
        Assert.Empty(facade.Requests); // không gửi yêu cầu tắt trực tiếp

        game.Outcome = ParentGameOutcome.Applied;
        Assert.True(await vm.SetParentProtectionAsync(false));
        Assert.False(vm.ParentProtectionEnabled);
        Assert.Equal([(false, 1000u)], game.SettingsCalls.Skip(1));
    }

    [Fact]
    public async Task Settings_RaiseDifficulty_Direct_LowerDifficulty_NeedsGame()
    {
        var facade = new StubProtectionFacade();
        var game = new StubGameService(ParentGameOutcome.Applied);
        var vm = new SettingsViewModel(new StubConfigFacade { Enabled = true, Meters = 2000 }, null!, new StubAuthPrompt(), null, facade, game);
        await vm.InitializeAsync(CancellationToken.None);

        Assert.True(await vm.SetParentProtectionAsync(true, 3000));
        Assert.Equal([(true, 3000u)], facade.Requests);
        Assert.Empty(game.SettingsCalls);

        Assert.True(await vm.SetParentProtectionAsync(true, 1000));
        Assert.Equal([(true, 1000u)], game.SettingsCalls);
        Assert.Equal(1000u, vm.ParentGameMeters);
    }

    [Fact]
    public async Task Settings_EnableIsDirect_NoGame()
    {
        var facade = new StubProtectionFacade();
        var game = new StubGameService(ParentGameOutcome.Applied);
        var vm = new SettingsViewModel(new StubConfigFacade { Enabled = false }, null!, new StubAuthPrompt(), null, facade, game);
        await vm.InitializeAsync(CancellationToken.None);

        Assert.True(await vm.SetParentProtectionAsync(true));
        Assert.Equal([(true, 1000u)], facade.Requests);
        Assert.Empty(game.SettingsCalls);
    }

    private sealed class StubGameService(ParentGameOutcome outcome) : IParentGameService
    {
        public ParentGameOutcome Outcome { get; set; } = outcome;

        public int PauseCalls { get; private set; }

        public byte[]? LastToken { get; private set; }

        public PauseDurationOption LastDuration { get; private set; }

        public List<(bool Enabled, uint Meters)> SettingsCalls { get; } = [];

        public Task<ParentGameFinish> PauseWithGameAsync(byte[] actionToken, PauseDurationOption duration)
        {
            PauseCalls++;
            LastToken = actionToken;
            LastDuration = duration;
            return Task.FromResult(new ParentGameFinish(Outcome, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));
        }

        public Task<ParentGameFinish> ChangeSettingsWithGameAsync(bool enabled, uint gameMeters)
        {
            SettingsCalls.Add((enabled, gameMeters));
            return Task.FromResult(new ParentGameFinish(Outcome, 0));
        }
    }

    private sealed class StubAuthPrompt : IAuthPromptService
    {
        public byte[]? Token { get; init; } = [1];

        public int Calls { get; private set; }

        public Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot)
        {
            Calls++;
            return Task.FromResult(Token);
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

        public uint Meters { get; init; } = 1000;

        public Task<ConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ConfigSnapshot(string.Empty, [], PerformanceModeOption.Balanced, Enabled, "vi", Meters));

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
        public List<(bool Enabled, uint Meters)> Requests { get; } = [];

        public Task<ParentGameStart> StartPauseGameAsync(byte[] actionToken, PauseDurationOption duration, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ParentGameStart> StartSettingsGameAsync(bool enabled, uint gameMeters, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ParentGameFinish> FinishGameAsync(bool completed, uint metersReached, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SetParentProtectionOutcome> SetParentProtectionAsync(bool enabled, uint gameMeters, CancellationToken cancellationToken)
        {
            Requests.Add((enabled, gameMeters));
            return Task.FromResult(SetParentProtectionOutcome.Success);
        }

        public Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}

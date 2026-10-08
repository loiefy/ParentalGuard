using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Config;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

/// <summary>`PAUSE-040`, `PAUSE-044`–`PAUSE-048` "Bảo vệ cả phụ huynh" — trò chơi nhảy rào (2026-10-08) + `FE-063a` lưu ngôn ngữ.</summary>
public class ParentProtectionTests : IDisposable
{
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-pp-{Guid.NewGuid():N}.log");
    private readonly string _configDbPath = Path.Combine(Path.GetTempPath(), $"pg-config-pp-{Guid.NewGuid():N}.db");
    private readonly FakeMonotonicClock _clock = new();

    /// <summary>Ghi lại mọi lần coordinator gọi ra ngoài (tiêu thụ token, áp dụng tạm dừng/cài đặt).</summary>
    private sealed class GameHarness
    {
        public bool ProtectionEnabled { get; set; } = true;

        public uint Meters { get; set; } = 1000;

        public bool TokenValid { get; set; } = true;

        public bool Paused { get; set; }

        public List<PauseDuration> PausesApplied { get; } = [];

        public List<(bool Enabled, uint Meters)> SettingsApplied { get; } = [];
    }

    private async Task<(ParentGameCoordinator Coordinator, GameHarness Harness)> CreateGameAsync()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var h = new GameHarness();
        var coordinator = new ParentGameCoordinator(
            _clock,
            auditLog,
            () => h.ProtectionEnabled,
            () => h.Meters,
            (_, _) => Task.FromResult(h.TokenValid),
            () => h.Paused,
            (duration, _) =>
            {
                h.PausesApplied.Add(duration);
                h.Paused = true;
                return Task.FromResult((PauseResult.Success, 12345L));
            },
            (enabled, meters, _) =>
            {
                h.SettingsApplied.Add((enabled, meters));
                return Task.FromResult(true);
            });
        return (coordinator, h);
    }

    private static async Task<ParentGameStartResponse> StartPause(ParentGameCoordinator c, UiParentSession s) =>
        (await c.HandleAsync(
            new IpcPayload
            {
                MessageId = 1,
                ParentGameStartReq = new ParentGameStartRequest { Purpose = ParentGamePurpose.Pause, ActionToken = ByteString.CopyFrom(1, 2, 3), Duration = PauseDuration.OneHour },
            },
            s,
            CancellationToken.None)).ParentGameStartResp;

    private static async Task<ParentGameFinishResponse> Finish(ParentGameCoordinator c, UiParentSession s, bool completed, uint meters) =>
        (await c.HandleAsync(
            new IpcPayload { MessageId = 2, ParentGameFinishReq = new ParentGameFinishRequest { Completed = completed, MetersReached = meters } },
            s,
            CancellationToken.None)).ParentGameFinishResp;

    [Fact]
    public void MinDuration_1000Meters_IsAtLeastThreeMinutes_ForEveryDifficulty()
    {
        Assert.True(ParentGameCoordinator.MinDurationMs(1000) >= 180_000);
        Assert.True(ParentGameCoordinator.MinDurationMs(2000) >= 360_000);
        Assert.True(ParentGameCoordinator.MinDurationMs(3000) >= 540_000);
    }

    [Fact]
    public async Task PauseGame_FinishAfterMinDuration_PausesWithChosenDuration_GameUsableOnce()
    {
        (ParentGameCoordinator game, GameHarness h) = await CreateGameAsync();
        var session = new UiParentSession(_clock);

        ParentGameStartResponse start = await StartPause(game, session);
        Assert.Equal(ParentGameResult.Started, start.Result);
        Assert.Equal(1000u, start.TargetMeters);
        Assert.Equal((uint)ParentGameCoordinator.MinDurationMs(1000), start.MinDurationMs);

        _clock.Now += 182_000;
        ParentGameFinishResponse finish = await Finish(game, session, completed: true, meters: 1000);

        Assert.Equal(ParentGameResult.Paused, finish.Result);
        Assert.Equal(12345L, finish.PauseExpiresAtUnixMs);
        Assert.Equal([PauseDuration.OneHour], h.PausesApplied);
        Assert.Equal(ParentGameResult.NoGame, (await Finish(game, session, true, 1000)).Result); // không dùng lại ván đã kết thúc
    }

    [Fact]
    public async Task PauseGame_FinishedTooFast_Refused_NoPause()
    {
        (ParentGameCoordinator game, GameHarness h) = await CreateGameAsync();
        var session = new UiParentSession(_clock);
        await StartPause(game, session);

        _clock.Now += 60_000; // "về đích" 1000 m sau 1 phút — không thể ở tốc độ cố định
        Assert.Equal(ParentGameResult.TooFast, (await Finish(game, session, true, 1000)).Result);
        Assert.Empty(h.PausesApplied);
    }

    [Theory]
    [InlineData(false, 523u)] // vấp rào
    [InlineData(false, 0u)]   // thoát game
    [InlineData(true, 999u)]  // chưa đủ quãng đường
    public async Task PauseGame_LostOrQuit_NoPause(bool completed, uint meters)
    {
        (ParentGameCoordinator game, GameHarness h) = await CreateGameAsync();
        var session = new UiParentSession(_clock);
        await StartPause(game, session);
        _clock.Now += 200_000;

        Assert.Equal(ParentGameResult.Lost, (await Finish(game, session, completed, meters)).Result);
        Assert.Empty(h.PausesApplied);
    }

    [Fact]
    public async Task PauseGame_InvalidToken_NotStarted()
    {
        (ParentGameCoordinator game, GameHarness h) = await CreateGameAsync();
        h.TokenValid = false;

        Assert.Equal(ParentGameResult.InvalidToken, (await StartPause(game, new UiParentSession(_clock))).Result);
    }

    [Fact]
    public async Task Game_ProtectionOff_NotRequired()
    {
        (ParentGameCoordinator game, GameHarness h) = await CreateGameAsync();
        h.ProtectionEnabled = false;

        Assert.Equal(ParentGameResult.NotRequired, (await StartPause(game, new UiParentSession(_clock))).Result);
    }

    [Fact]
    public async Task Game_FinishWithoutStart_NoGame()
    {
        (ParentGameCoordinator game, _) = await CreateGameAsync();

        Assert.Equal(ParentGameResult.NoGame, (await Finish(game, new UiParentSession(_clock), true, 1000)).Result);
    }

    [Fact]
    public async Task SettingsGame_DisableOrLower_RequiresSessionThenAppliesAfterWin()
    {
        (ParentGameCoordinator game, GameHarness h) = await CreateGameAsync();
        h.Meters = 3000;
        var session = new UiParentSession(_clock);
        var startReq = new IpcPayload { MessageId = 3, ParentGameStartReq = new ParentGameStartRequest { Purpose = ParentGamePurpose.Settings, SettingsEnabled = true, SettingsGameMeters = 1000 } };

        Assert.Equal(ParentGameResult.NotAuthenticated, (await game.HandleAsync(startReq, session, CancellationToken.None)).ParentGameStartResp.Result);

        session.Open();
        ParentGameStartResponse start = (await game.HandleAsync(startReq, session, CancellationToken.None)).ParentGameStartResp;
        Assert.Equal(ParentGameResult.Started, start.Result);
        Assert.Equal(3000u, start.TargetMeters); // chơi ở độ khó HIỆN HÀNH

        _clock.Now += 546_000;
        Assert.Equal(ParentGameResult.Applied, (await Finish(game, session, true, 3000)).Result);
        Assert.Equal([(true, 1000u)], h.SettingsApplied);
    }

    [Theory]
    [InlineData(true, 3000u, 1000u, false)] // tăng độ khó — không cần chơi
    [InlineData(true, 0u, 2000u, false)]    // giữ nguyên
    [InlineData(true, 1000u, 2000u, true)]  // giảm độ khó
    [InlineData(false, 0u, 1000u, true)]    // tắt chế độ
    public void NeedsGame_OnlyWhenProtectionWouldWeaken(bool wantEnabled, uint wantMeters, uint currentMeters, bool expected) =>
        Assert.Equal(expected, ParentGameCoordinator.NeedsGame(wantEnabled, wantMeters, currentMeters));

    private async Task<(ConfigCoordinator Config, MonitoringStateHolder Holder, List<string> LanguagesPushed)> CreateConfigAsync(MonitoringStateData? state = null)
    {
        state ??= MonitoringStateData.CreateFirstRunDefault();
        ConfigDb.CreateFresh(_configDbPath, state, PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var auth = new AuthCoordinator(Path.Combine(Path.GetTempPath(), $"pg-auth-pp-{Guid.NewGuid():N}.dat"), auditLog, _clock, NullLogger.Instance);
        var holder = new MonitoringStateHolder(state);
        var pushed = new List<string>();
        return (new ConfigCoordinator(holder, _configDbPath, auth, auditLog, () => { }, pushLanguage: pushed.Add), holder, pushed);
    }

    private static Task<IpcPayload> SetProtection(ConfigCoordinator c, UiParentSession s, bool enabled, uint meters = 0) =>
        c.HandleAsync(new IpcPayload { MessageId = 3, SetParentProtectionReq = new SetParentProtectionRequest { Enabled = enabled, ParentGameMeters = meters } }, s, CancellationToken.None);

    [Fact]
    public async Task SetParentProtection_EnableAndRaiseDirect_DisableAndLowerNeedGame_Persists()
    {
        (ConfigCoordinator config, MonitoringStateHolder holder, _) = await CreateConfigAsync();
        var session = new UiParentSession(_clock);

        Assert.Equal(SetParentProtectionResult.NotAuthenticated, (await SetProtection(config, session, true)).SetParentProtectionResp.Result);

        session.Open();
        Assert.Equal(SetParentProtectionResult.Success, (await SetProtection(config, session, true, 2000)).SetParentProtectionResp.Result);
        Assert.True(config.ParentProtectionEnabled);
        Assert.Equal(2000u, config.ParentGameMeters);

        Assert.Equal(SetParentProtectionResult.Success, (await SetProtection(config, session, true, 3000)).SetParentProtectionResp.Result);
        Assert.Equal(SetParentProtectionResult.ChallengeRequired, (await SetProtection(config, session, true, 1000)).SetParentProtectionResp.Result);
        Assert.Equal(SetParentProtectionResult.ChallengeRequired, (await SetProtection(config, session, false)).SetParentProtectionResp.Result);
        Assert.Equal(SetParentProtectionResult.Unspecified, (await SetProtection(config, session, true, 1234)).SetParentProtectionResp.Result);
        Assert.True(holder.Current.ParentProtectionEnabled);
        Assert.Equal(3000u, holder.Current.ParentGameMeters);

        // Đường về đích trò chơi (ParentGameCoordinator gọi) mới được tắt.
        Assert.True(await config.ApplyParentProtectionAsync(false, 0, CancellationToken.None));
        Assert.False(holder.Current.ParentProtectionEnabled);

        IpcPayload query = await config.HandleAsync(new IpcPayload { MessageId = 7, ConfigQuery = new ConfigQuery() }, session, CancellationToken.None);
        Assert.Equal(3000u, query.ConfigResp.ParentGameMeters);

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.False(db.ReadSnapshot().MonitoringState.ParentProtectionEnabled);
        Assert.Equal(3000u, db.ReadSnapshot().MonitoringState.ParentGameMeters);
    }

    [Fact]
    public async Task SetLanguage_NoLoginNeeded_ValidatesCode_PersistsAndPushesToOverlay()
    {
        (ConfigCoordinator config, MonitoringStateHolder holder, List<string> pushed) = await CreateConfigAsync();
        var noSession = new UiParentSession(_clock);

        IpcPayload bad = await config.HandleAsync(new IpcPayload { MessageId = 4, SetLanguageReq = new SetLanguageRequest { Language = "de" } }, noSession, CancellationToken.None);
        IpcPayload ok = await config.HandleAsync(new IpcPayload { MessageId = 5, SetLanguageReq = new SetLanguageRequest { Language = "zh-Hans" } }, noSession, CancellationToken.None);

        Assert.False(bad.SetLanguageResp.Accepted);
        Assert.True(ok.SetLanguageResp.Accepted);
        Assert.Equal("zh-Hans", holder.Current.Language);
        Assert.Equal(["zh-Hans"], pushed);

        IpcPayload query = await config.HandleAsync(new IpcPayload { MessageId = 6, ConfigQuery = new ConfigQuery() }, noSession, CancellationToken.None);
        Assert.Equal("zh-Hans", query.ConfigResp.Language);

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.Equal("zh-Hans", db.ReadSnapshot().MonitoringState.Language);
    }

    [Fact]
    public void OldConfigWithoutNewFields_DefaultsToVietnameseProtectionOffAnd1000Meters()
    {
        MonitoringStateData state = MonitoringStateData.CreateFirstRunDefault();

        Assert.Equal("vi", state.Language);
        Assert.False(state.ParentProtectionEnabled);
        Assert.Equal(1000u, state.ParentGameMeters);
    }

    public void Dispose()
    {
        foreach (string path in new[] { _auditLogPath, _configDbPath, _configDbPath + "-wal", _configDbPath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}

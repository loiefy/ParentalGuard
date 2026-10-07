using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Config;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

/// <summary>`PAUSE-040`–`PAUSE-043` "Bảo vệ cả phụ huynh" + `FE-063a` lưu ngôn ngữ (2026-10-07).</summary>
public class ParentProtectionTests : IDisposable
{
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-pp-{Guid.NewGuid():N}.log");
    private readonly string _configDbPath = Path.Combine(Path.GetTempPath(), $"pg-config-pp-{Guid.NewGuid():N}.db");
    private readonly FakeMonotonicClock _clock = new();

    private static int Solve(string question)
    {
        string[] parts = question.Split(' ');
        int a = int.Parse(parts[0]);
        int b = int.Parse(parts[2]);
        return parts[1] switch { "+" => a + b, "-" => a - b, _ => a * b };
    }

    private static IpcPayload Submit(ParentChallengeCoordinator c, UiParentSession s, IEnumerable<int> answers)
    {
        var req = new ChallengeSubmitRequest();
        req.Answers.AddRange(answers);
        return c.HandleAsync(new IpcPayload { MessageId = 2, ChallengeSubmitReq = req }, s).Result;
    }

    private static ChallengeStartResponse Start(ParentChallengeCoordinator c, UiParentSession s) =>
        c.HandleAsync(new IpcPayload { MessageId = 1, ChallengeStartReq = new ChallengeStartRequest() }, s).Result.ChallengeStartResp;

    [Fact]
    public void Challenge_CorrectAnswers_Passes_PassUsableExactlyOnce()
    {
        var coordinator = new ParentChallengeCoordinator(_clock);
        var session = new UiParentSession(_clock);

        ChallengeStartResponse start = Start(coordinator, session);
        Assert.Equal(ChallengeResult.Success, start.Result);
        Assert.Equal(ParentChallengeCoordinator.QuestionCount, start.Questions.Count);

        Assert.Equal(ChallengeResult.Passed, Submit(coordinator, session, start.Questions.Select(Solve)).ChallengeSubmitResp.Result);
        Assert.True(session.TryConsumeChallengePass());
        Assert.False(session.TryConsumeChallengePass());
    }

    [Fact]
    public void Challenge_PassExpiresAfterTwoMinutes()
    {
        var coordinator = new ParentChallengeCoordinator(_clock);
        var session = new UiParentSession(_clock);
        ChallengeStartResponse start = Start(coordinator, session);
        Submit(coordinator, session, start.Questions.Select(Solve));

        _clock.Now += (long)ParentChallengeCoordinator.PassValidity.TotalMilliseconds;

        Assert.False(session.TryConsumeChallengePass());
    }

    [Fact]
    public void Challenge_LateAnswer_Expired_NoPass()
    {
        var coordinator = new ParentChallengeCoordinator(_clock);
        var session = new UiParentSession(_clock);
        ChallengeStartResponse start = Start(coordinator, session);
        _clock.Now += (long)ParentChallengeCoordinator.AnswerTime.TotalMilliseconds + 1;

        Assert.Equal(ChallengeResult.Expired, Submit(coordinator, session, start.Questions.Select(Solve)).ChallengeSubmitResp.Result);
        Assert.False(session.TryConsumeChallengePass());
    }

    [Fact]
    public void Challenge_ThreeWrongAttempts_LocksOutFiveMinutes_EvenForNewConnection()
    {
        var coordinator = new ParentChallengeCoordinator(_clock);
        var session = new UiParentSession(_clock);
        IpcPayload last = new();
        for (int i = 0; i < ParentChallengeCoordinator.MaxFailuresBeforeLockout; i++)
        {
            ChallengeStartResponse start = Start(coordinator, session);
            last = Submit(coordinator, session, start.Questions.Select(q => Solve(q) + 1));
        }

        Assert.Equal(ChallengeResult.LockedOut, last.ChallengeSubmitResp.Result);
        Assert.Equal(ChallengeResult.LockedOut, Start(coordinator, new UiParentSession(_clock)).Result); // kết nối mới cũng bị khoá

        _clock.Now += (long)ParentChallengeCoordinator.LockoutDuration.TotalMilliseconds;
        Assert.Equal(ChallengeResult.Success, Start(coordinator, session).Result);
    }

    [Fact]
    public void Challenge_SubmitWithoutStart_NoChallenge()
    {
        var coordinator = new ParentChallengeCoordinator(_clock);

        Assert.Equal(ChallengeResult.NoChallenge, Submit(coordinator, new UiParentSession(_clock), [1, 2, 3, 4, 5]).ChallengeSubmitResp.Result);
    }

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

    private static Task<IpcPayload> SetProtection(ConfigCoordinator c, UiParentSession s, bool enabled) =>
        c.HandleAsync(new IpcPayload { MessageId = 3, SetParentProtectionReq = new SetParentProtectionRequest { Enabled = enabled } }, s, CancellationToken.None);

    [Fact]
    public async Task SetParentProtection_RequiresSession_DisableRequiresChallengePass_Persists()
    {
        (ConfigCoordinator config, MonitoringStateHolder holder, _) = await CreateConfigAsync();
        var session = new UiParentSession(_clock);

        Assert.Equal(SetParentProtectionResult.NotAuthenticated, (await SetProtection(config, session, true)).SetParentProtectionResp.Result);

        session.Open();
        Assert.Equal(SetParentProtectionResult.Success, (await SetProtection(config, session, true)).SetParentProtectionResp.Result);
        Assert.True(holder.Current.ParentProtectionEnabled);
        Assert.True(config.ParentProtectionEnabled);

        Assert.Equal(SetParentProtectionResult.ChallengeRequired, (await SetProtection(config, session, false)).SetParentProtectionResp.Result);

        var challenge = new ParentChallengeCoordinator(_clock);
        ChallengeStartResponse start = Start(challenge, session);
        Submit(challenge, session, start.Questions.Select(Solve));
        Assert.Equal(SetParentProtectionResult.Success, (await SetProtection(config, session, false)).SetParentProtectionResp.Result);
        Assert.False(holder.Current.ParentProtectionEnabled);

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.False(db.ReadSnapshot().MonitoringState.ParentProtectionEnabled);
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
    public void OldConfigWithoutNewFields_DefaultsToVietnameseAndProtectionOff()
    {
        MonitoringStateData state = MonitoringStateData.CreateFirstRunDefault();

        Assert.Equal("vi", state.Language);
        Assert.False(state.ParentProtectionEnabled);
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

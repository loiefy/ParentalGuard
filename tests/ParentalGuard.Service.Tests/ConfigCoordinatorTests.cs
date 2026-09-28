using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Config;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Gap fix Đợt 7 (`10-ui-architecture.md` mục 6.4, `03-ipc-communication.md` mục 3.7) — chỉ test qua
/// <see cref="ConfigCoordinator.HandleAsync"/> (production path), cùng mẫu hình <c>PauseCoordinatorTests</c>.
/// </summary>
public class ConfigCoordinatorTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-config-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-config-{Guid.NewGuid():N}.log");
    private readonly string _configDbPath = Path.Combine(Path.GetTempPath(), $"pg-config-config-{Guid.NewGuid():N}.db");

    private sealed record Fixture(ConfigCoordinator Config, AuthCoordinator Auth, AuditLogWriter AuditLog, MonitoringStateHolder Holder, Func<int> PushCount);

    private async Task<Fixture> CreateAsync(MonitoringStateData? initial = null)
    {
        MonitoringStateData state = initial ?? MonitoringStateData.CreateFirstRunDefault();
        ConfigDb.CreateFresh(_configDbPath, state, PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);

        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var authCoordinator = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);
        var holder = new MonitoringStateHolder(state);

        int pushCount = 0;
        var configCoordinator = new ConfigCoordinator(holder, _configDbPath, authCoordinator, auditLog, () => pushCount++);

        return new Fixture(configCoordinator, authCoordinator, auditLog, holder, () => pushCount);
    }

    private static async Task<byte[]> GetValidActionTokenAsync(AuthCoordinator auth, string actionContext = "manage_whitelist")
    {
        IpcPayload setupResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 1, SetInitialPasswordReq = new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8("Passw0rd!") } }, CancellationToken.None);
        await auth.HandleAsync(
            new IpcPayload { MessageId = 2, ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = setupResponse.SetInitialPasswordResp.SetupToken, Confirmed = true } }, CancellationToken.None);
        IpcPayload verifyResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 3, AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFromUtf8("Passw0rd!"), ActionContext = actionContext } }, CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyResponse.AuthVerifyResp.Result);
        return verifyResponse.AuthVerifyResp.ActionToken.ToByteArray();
    }

    [Fact]
    public async Task ConfigQuery_ReturnsCurrentStateFromRam_NoGate()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { OverlayMessage = "Hi.", UserWhitelistedProcessNames = ["a.exe"] });

        IpcPayload response = await fx.Config.HandleAsync(new IpcPayload { MessageId = 1, ConfigQuery = new ConfigQuery() }, CancellationToken.None);

        Assert.Equal("Hi.", response.ConfigResp.OverlayMessage);
        Assert.Equal(["a.exe"], response.ConfigResp.UserWhitelistedProcessNames);
        Assert.Equal(PerformanceMode.Balanced, response.ConfigResp.PerformanceMode);
    }

    [Fact]
    public async Task ConfigUpdate_ValidOverlayMessage_PersistsAndUpdatesHolder_NoPush()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "New message.", PerformanceMode = PerformanceMode.Balanced } },
            CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.Success, response.ConfigUpdateResp.Result);
        Assert.Equal("New message.", fx.Holder.Current.OverlayMessage);
        Assert.Equal(0, fx.PushCount()); // performance_mode không đổi -> không push ControlVisionCommand

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.Equal("New message.", db.ReadSnapshot().MonitoringState.OverlayMessage);
    }

    [Fact]
    public async Task ConfigUpdate_PerformanceModeChanged_PushesControlVisionCommand()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "", PerformanceMode = PerformanceMode.MaximumProtection } },
            CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.Success, response.ConfigUpdateResp.Result);
        Assert.Equal(PerformanceMode.MaximumProtection, fx.Holder.Current.PerformanceMode);
        Assert.Equal(1, fx.PushCount());
    }

    [Fact]
    public async Task ConfigUpdate_PerformanceModeUnspecified_KeepsCurrentMode()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { PerformanceMode = PerformanceMode.MaximumProtection });

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "", PerformanceMode = PerformanceMode.Unspecified } },
            CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.Success, response.ConfigUpdateResp.Result);
        Assert.Equal(PerformanceMode.MaximumProtection, fx.Holder.Current.PerformanceMode);
        Assert.Equal(0, fx.PushCount());
    }

    [Fact]
    public async Task ConfigUpdate_MessageTooLong_ReturnsTooLong_DoesNotPersist()
    {
        Fixture fx = await CreateAsync();
        string tooLong = new('a', 256);

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = tooLong, PerformanceMode = PerformanceMode.Balanced } },
            CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.TooLong, response.ConfigUpdateResp.Result);
        Assert.Equal("", fx.Holder.Current.OverlayMessage);
    }

    [Fact]
    public async Task ConfigUpdate_InvalidCharacters_ReturnsInvalidCharacters()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "50% off!", PerformanceMode = PerformanceMode.Balanced } },
            CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.InvalidCharacters, response.ConfigUpdateResp.Result);
    }

    [Fact]
    public async Task RemoveWhitelist_InvalidToken_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { UserWhitelistedProcessNames = ["a.exe"] });

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ActionToken = ByteString.CopyFrom([1, 2, 3]), ProcessName = "a.exe" } },
            CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.InvalidToken, response.RemoveWhitelistResp.Result);
        Assert.Equal(["a.exe"], fx.Holder.Current.UserWhitelistedProcessNames);
    }

    [Fact]
    public async Task RemoveWhitelist_NotFound_ReturnsNotFound()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ActionToken = ByteString.CopyFrom(token), ProcessName = "missing.exe" } },
            CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.NotFound, response.RemoveWhitelistResp.Result);
    }

    [Fact]
    public async Task RemoveWhitelist_ValidTokenAndEntryPresent_RemovesAndPushes()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { UserWhitelistedProcessNames = ["a.exe", "b.exe"] });
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ActionToken = ByteString.CopyFrom(token), ProcessName = "a.exe" } },
            CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.Success, response.RemoveWhitelistResp.Result);
        Assert.Equal(["b.exe"], fx.Holder.Current.UserWhitelistedProcessNames);
        Assert.Equal(1, fx.PushCount());

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.Equal(["b.exe"], db.ReadSnapshot().MonitoringState.UserWhitelistedProcessNames);
    }

    [Fact]
    public async Task TryAddUserWhitelistEntry_NewEntry_AddsAndPushes()
    {
        Fixture fx = await CreateAsync();

        WhitelistAddResult result = await fx.Config.TryAddUserWhitelistEntryAsync("chrome.exe", CancellationToken.None);

        Assert.Equal(WhitelistAddResult.Added, result);
        Assert.Equal(["chrome.exe"], fx.Holder.Current.UserWhitelistedProcessNames);
        Assert.Equal(1, fx.PushCount());
    }

    [Fact]
    public async Task TryAddUserWhitelistEntry_AlreadyListed_ReturnsAlreadyListed_CaseInsensitive()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { UserWhitelistedProcessNames = ["chrome.exe"] });

        WhitelistAddResult result = await fx.Config.TryAddUserWhitelistEntryAsync("CHROME.EXE", CancellationToken.None);

        Assert.Equal(WhitelistAddResult.AlreadyListed, result);
        Assert.Equal(0, fx.PushCount());
    }

    public void Dispose()
    {
        foreach (string path in new[] { _authDatPath, _auditLogPath, _configDbPath, _configDbPath + "-wal", _configDbPath + "-shm" })
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

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

    private sealed record Fixture(ConfigCoordinator Config, AuthCoordinator Auth, AuditLogWriter AuditLog, MonitoringStateHolder Holder, Func<int> PushCount, FakeMonotonicClock Clock, UiParentSession Session)
    {
        public List<string> OverlayMessagesPushed { get; init; } = [];
    }

    private async Task<Fixture> CreateAsync(MonitoringStateData? initial = null)
    {
        MonitoringStateData state = initial ?? MonitoringStateData.CreateFirstRunDefault();
        ConfigDb.CreateFresh(_configDbPath, state, PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);

        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var authCoordinator = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);
        var holder = new MonitoringStateHolder(state);

        int pushCount = 0;
        List<string> overlayMessagesPushed = [];
        var configCoordinator = new ConfigCoordinator(holder, _configDbPath, authCoordinator, auditLog, () => pushCount++, overlayMessagesPushed.Add);

        return new Fixture(configCoordinator, authCoordinator, auditLog, holder, () => pushCount, clock, new UiParentSession(clock)) { OverlayMessagesPushed = overlayMessagesPushed };
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

        IpcPayload response = await fx.Config.HandleAsync(new IpcPayload { MessageId = 1, ConfigQuery = new ConfigQuery() }, fx.Session, CancellationToken.None);

        Assert.Equal("Hi.", response.ConfigResp.OverlayMessage);
        // MISC-030b: whitelist hiển thị = danh sách cấp sẵn BE-073a + mục cũ người dùng thêm.
        Assert.Equal([.. MonitoringStateData.InitialExcludeProcessNames, "a.exe"], response.ConfigResp.UserWhitelistedProcessNames);
        Assert.Equal(PerformanceMode.Balanced, response.ConfigResp.PerformanceMode);
    }

    [Fact]
    public async Task ConfigUpdate_ValidOverlayMessage_PersistsAndUpdatesHolder_NoPush()
    {
        Fixture fx = await CreateAsync();
        fx.Session.Open(); // PWD-024/ADR-150: ConfigUpdate bắt buộc phiên phụ huynh

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "New message.", PerformanceMode = PerformanceMode.Balanced } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.Success, response.ConfigUpdateResp.Result);
        Assert.Equal("New message.", fx.Holder.Current.OverlayMessage);
        Assert.Equal(0, fx.PushCount()); // performance_mode không đổi -> không push ControlVisionCommand
        Assert.Equal(["New message."], fx.OverlayMessagesPushed); // ADR-110: OverlayMessageUpdate xuống Overlay

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.Equal("New message.", db.ReadSnapshot().MonitoringState.OverlayMessage);
    }

    [Fact]
    public async Task ConfigUpdate_PerformanceModeChanged_PushesControlVisionCommand()
    {
        Fixture fx = await CreateAsync();
        fx.Session.Open(); // PWD-024/ADR-150: ConfigUpdate bắt buộc phiên phụ huynh

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "", PerformanceMode = PerformanceMode.MaximumProtection } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.Success, response.ConfigUpdateResp.Result);
        Assert.Equal(PerformanceMode.MaximumProtection, fx.Holder.Current.PerformanceMode);
        Assert.Equal(1, fx.PushCount());
        Assert.Empty(fx.OverlayMessagesPushed); // overlay_message không đổi -> không push OverlayMessageUpdate
    }

    [Fact]
    public async Task ConfigUpdate_PerformanceModeUnspecified_KeepsCurrentMode()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { PerformanceMode = PerformanceMode.MaximumProtection });
        fx.Session.Open(); // PWD-024/ADR-150: ConfigUpdate bắt buộc phiên phụ huynh

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "", PerformanceMode = PerformanceMode.Unspecified } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.Success, response.ConfigUpdateResp.Result);
        Assert.Equal(PerformanceMode.MaximumProtection, fx.Holder.Current.PerformanceMode);
        Assert.Equal(0, fx.PushCount());
    }

    [Fact]
    public async Task ConfigUpdate_MessageTooLong_ReturnsTooLong_DoesNotPersist()
    {
        Fixture fx = await CreateAsync();
        fx.Session.Open(); // PWD-024/ADR-150: ConfigUpdate bắt buộc phiên phụ huynh
        string tooLong = new('a', 256);

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = tooLong, PerformanceMode = PerformanceMode.Balanced } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.TooLong, response.ConfigUpdateResp.Result);
        Assert.Equal("", fx.Holder.Current.OverlayMessage);
    }

    [Fact]
    public async Task ConfigUpdate_InvalidCharacters_ReturnsInvalidCharacters()
    {
        Fixture fx = await CreateAsync();
        fx.Session.Open(); // PWD-024/ADR-150: ConfigUpdate bắt buộc phiên phụ huynh

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "50% off!", PerformanceMode = PerformanceMode.Balanced } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.InvalidCharacters, response.ConfigUpdateResp.Result);
    }

    [Fact]
    public async Task RemoveWhitelist_InvalidToken_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { UserWhitelistedProcessNames = ["a.exe"] });

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ActionToken = ByteString.CopyFrom([1, 2, 3]), ProcessName = "a.exe" } },
            fx.Session, CancellationToken.None);

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
            fx.Session, CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.NotFound, response.RemoveWhitelistResp.Result);
    }

    [Fact]
    public async Task RemoveWhitelist_ValidTokenAndEntryPresent_RemovesAndPushes()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { UserWhitelistedProcessNames = ["a.exe", "b.exe"] });
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ActionToken = ByteString.CopyFrom(token), ProcessName = "a.exe" } },
            fx.Session, CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.Success, response.RemoveWhitelistResp.Result);
        Assert.Equal(["b.exe"], fx.Holder.Current.UserWhitelistedProcessNames);
        Assert.Equal(1, fx.PushCount());

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.Equal(["b.exe"], db.ReadSnapshot().MonitoringState.UserWhitelistedProcessNames);
    }

    /// <summary>`MISC-030b`: xoá được mục thuộc danh sách cấp sẵn BE-073a — ứng dụng đó quay lại bị giám sát.</summary>
    [Fact]
    public async Task RemoveWhitelist_EntryFromBuiltInList_RemovedFromExcludeList_AndPushed()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ActionToken = ByteString.CopyFrom(token), ProcessName = "TASKMGR.EXE" } },
            fx.Session, CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.Success, response.RemoveWhitelistResp.Result);
        Assert.DoesNotContain(fx.Holder.Current.ExcludeProcessNames, n => n.Equals("Taskmgr.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, fx.PushCount());

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.DoesNotContain("Taskmgr.exe", db.ReadSnapshot().MonitoringState.ExcludeProcessNames);
    }

    /// <summary>`MISC-030c`: "Khôi phục cài đặt gốc" → whitelist về đúng danh sách BE-073a, bỏ mục cũ người dùng thêm.</summary>
    [Fact]
    public async Task ResetWhitelist_RestoresBuiltInList_DropsLegacyEntries()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with
        {
            ExcludeProcessNames = ["regedit.exe"],
            UserWhitelistedProcessNames = ["chrome.exe"],
        });
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ResetWhitelistReq = new ResetWhitelistRequest { ActionToken = ByteString.CopyFrom(token) } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ResetWhitelistResult.Success, response.ResetWhitelistResp.Result);
        Assert.Equal(MonitoringStateData.InitialExcludeProcessNames, response.ResetWhitelistResp.Whitelist);
        Assert.Equal(MonitoringStateData.InitialExcludeProcessNames, fx.Holder.Current.ExcludeProcessNames);
        Assert.Empty(fx.Holder.Current.UserWhitelistedProcessNames);
        Assert.Equal(1, fx.PushCount());
    }

    [Fact]
    public async Task ResetWhitelist_InvalidToken_Rejected_StateUnchanged()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { ExcludeProcessNames = ["regedit.exe"] });

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ResetWhitelistReq = new ResetWhitelistRequest { ActionToken = ByteString.CopyFrom([1, 2, 3]) } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ResetWhitelistResult.InvalidToken, response.ResetWhitelistResp.Result);
        Assert.Equal(["regedit.exe"], fx.Holder.Current.ExcludeProcessNames);
    }

    /// <summary>`PWD-024`/ADR-150 — supersedes ADR-125: đổi cài đặt khi chưa đăng nhập phụ huynh bị từ chối, không ghi gì.</summary>
    [Fact]
    public async Task ConfigUpdate_NoParentSession_ReturnsNotAuthenticated_DoesNotPersist()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "New message.", PerformanceMode = PerformanceMode.MaximumProtection } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.NotAuthenticated, response.ConfigUpdateResp.Result);
        Assert.Equal("", fx.Holder.Current.OverlayMessage);
        Assert.Equal(PerformanceMode.Balanced, fx.Holder.Current.PerformanceMode);
        Assert.Equal(0, fx.PushCount());
    }

    /// <summary>`PWD-024`: phiên tự hết hạn sau 10 phút không thao tác.</summary>
    [Fact]
    public async Task ConfigUpdate_ParentSessionIdleExpired_ReturnsNotAuthenticated()
    {
        Fixture fx = await CreateAsync();
        fx.Session.Open();
        fx.Clock.Now += (long)UiParentSession.IdleTimeout.TotalMilliseconds;

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "New message.", PerformanceMode = PerformanceMode.Balanced } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.NotAuthenticated, response.ConfigUpdateResp.Result);
        Assert.False(fx.Session.IsActive);
    }

    /// <summary>`PWD-024`: mỗi request hợp lệ gia hạn idle — thao tác liên tục không bị đá ra giữa chừng.</summary>
    [Fact]
    public async Task ConfigUpdate_WithinIdleWindow_ExtendsSession()
    {
        Fixture fx = await CreateAsync();
        fx.Session.Open();
        fx.Clock.Now += (long)UiParentSession.IdleTimeout.TotalMilliseconds - 1000;

        IpcPayload first = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "One.", PerformanceMode = PerformanceMode.Balanced } },
            fx.Session, CancellationToken.None);
        fx.Clock.Now += (long)UiParentSession.IdleTimeout.TotalMilliseconds - 1000;
        IpcPayload second = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 2, ConfigUpdateReq = new ConfigUpdateRequest { OverlayMessage = "Two.", PerformanceMode = PerformanceMode.Balanced } },
            fx.Session, CancellationToken.None);

        Assert.Equal(ConfigUpdateResult.Success, first.ConfigUpdateResp.Result);
        Assert.Equal(ConfigUpdateResult.Success, second.ConfigUpdateResp.Result);
        Assert.Equal("Two.", fx.Holder.Current.OverlayMessage);
    }

    /// <summary>ADR-150: đã đăng nhập phụ huynh thì xoá whitelist không cần action_token (UI gửi token rỗng).</summary>
    [Fact]
    public async Task RemoveWhitelist_ActiveParentSession_EmptyToken_Succeeds()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { UserWhitelistedProcessNames = ["a.exe"] });
        fx.Session.Open();

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ProcessName = "a.exe" } },
            fx.Session, CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.Success, response.RemoveWhitelistResp.Result);
        Assert.Empty(fx.Holder.Current.UserWhitelistedProcessNames);
    }

    [Fact]
    public async Task RemoveWhitelist_NoParentSession_EmptyToken_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync(MonitoringStateData.CreateFirstRunDefault() with { UserWhitelistedProcessNames = ["a.exe"] });

        IpcPayload response = await fx.Config.HandleAsync(
            new IpcPayload { MessageId = 1, RemoveWhitelistReq = new RemoveWhitelistEntryRequest { ProcessName = "a.exe" } },
            fx.Session, CancellationToken.None);

        Assert.Equal(RemoveWhitelistEntryResult.InvalidToken, response.RemoveWhitelistResp.Result);
        Assert.Equal(["a.exe"], fx.Holder.Current.UserWhitelistedProcessNames);
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

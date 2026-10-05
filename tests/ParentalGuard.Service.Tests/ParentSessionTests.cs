using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// `PWD-024` — phiên đăng nhập phụ huynh dùng chung `S3`/`S4` (Architecture/08 mục 7.10, Architecture/03 mục
/// 3.7b ADR-149/150). Test qua <see cref="AuthCoordinator.HandleParentSessionAsync"/> + <see cref="AuditLogCoordinator.HandleAsync"/> (production path).
/// </summary>
public class ParentSessionTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-session-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-session-{Guid.NewGuid():N}.log");
    private readonly string _configDbPath = Path.Combine(Path.GetTempPath(), $"pg-config-session-{Guid.NewGuid():N}.db");

    private sealed record Fixture(AuthCoordinator Auth, AuditLogCoordinator Audit, FakeMonotonicClock Clock, UiParentSession Session);

    private async Task<Fixture> CreateAsync()
    {
        ConfigDb.CreateFresh(_configDbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var auth = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);
        await SetupPasswordAsync(auth);
        return new Fixture(auth, new AuditLogCoordinator(auth, auditLog, clock, _configDbPath), clock, new UiParentSession(clock));
    }

    private static async Task SetupPasswordAsync(AuthCoordinator auth)
    {
        IpcPayload setupResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 1, SetInitialPasswordReq = new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8("Passw0rd!") } }, CancellationToken.None);
        await auth.HandleAsync(
            new IpcPayload { MessageId = 2, ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = setupResponse.SetInitialPasswordResp.SetupToken, Confirmed = true } }, CancellationToken.None);
    }

    private static async Task<ByteString> VerifyAsync(AuthCoordinator auth, string actionContext)
    {
        IpcPayload verifyResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 3, AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFromUtf8("Passw0rd!"), ActionContext = actionContext } }, CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyResponse.AuthVerifyResp.Result);
        return verifyResponse.AuthVerifyResp.ActionToken;
    }

    private static Task<IpcPayload> SendAsync(Fixture fx, ParentSessionOp op, ByteString? token = null) =>
        fx.Auth.HandleParentSessionAsync(
            new IpcPayload { MessageId = 10, ParentSessionReq = new ParentSessionRequest { Op = op, ActionToken = token ?? ByteString.Empty } },
            fx.Session,
            CancellationToken.None);

    [Fact]
    public async Task Open_WithParentSessionToken_OpensSessionFor10Minutes()
    {
        Fixture fx = await CreateAsync();
        ByteString token = await VerifyAsync(fx.Auth, AuthCoordinator.ParentSessionActionContext);

        IpcPayload response = await SendAsync(fx, ParentSessionOp.Open, token);

        Assert.Equal(ParentSessionResult.Success, response.ParentSessionResp.Result);
        Assert.Equal(fx.Clock.Now + (long)UiParentSession.IdleTimeout.TotalMilliseconds, response.ParentSessionResp.IdleExpiresAtUnixMs);
        Assert.True(fx.Session.IsActive);
    }

    /// <summary>Token cấp cho hành động khác (vd tạm dừng) không được đổi lấy phiên — `PWD-024` tách riêng pause/uninstall.</summary>
    [Fact]
    public async Task Open_WithTokenForOtherActionContext_Rejected()
    {
        Fixture fx = await CreateAsync();
        ByteString token = await VerifyAsync(fx.Auth, "pause_monitoring");

        IpcPayload response = await SendAsync(fx, ParentSessionOp.Open, token);

        Assert.Equal(ParentSessionResult.InvalidToken, response.ParentSessionResp.Result);
        Assert.False(fx.Session.IsActive);
    }

    [Fact]
    public async Task Open_TokenUsedTwice_SecondRejected()
    {
        Fixture fx = await CreateAsync();
        ByteString token = await VerifyAsync(fx.Auth, AuthCoordinator.ParentSessionActionContext);
        byte[] copy = token.ToByteArray();

        await SendAsync(fx, ParentSessionOp.Open, token);
        fx.Session.Close();
        IpcPayload second = await SendAsync(fx, ParentSessionOp.Open, ByteString.CopyFrom(copy));

        Assert.Equal(ParentSessionResult.InvalidToken, second.ParentSessionResp.Result);
        Assert.False(fx.Session.IsActive);
    }

    [Fact]
    public async Task Open_EmptyToken_Rejected()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await SendAsync(fx, ParentSessionOp.Open);

        Assert.Equal(ParentSessionResult.InvalidToken, response.ParentSessionResp.Result);
    }

    [Fact]
    public async Task Keepalive_BeforeExpiry_Extends_AfterExpiry_NotActive()
    {
        Fixture fx = await CreateAsync();
        await SendAsync(fx, ParentSessionOp.Open, await VerifyAsync(fx.Auth, AuthCoordinator.ParentSessionActionContext));
        long idle = (long)UiParentSession.IdleTimeout.TotalMilliseconds;

        fx.Clock.Now += idle - 1;
        IpcPayload extended = await SendAsync(fx, ParentSessionOp.Keepalive);
        Assert.Equal(ParentSessionResult.Success, extended.ParentSessionResp.Result);
        Assert.Equal(fx.Clock.Now + idle, extended.ParentSessionResp.IdleExpiresAtUnixMs);

        fx.Clock.Now += idle;
        IpcPayload expired = await SendAsync(fx, ParentSessionOp.Keepalive);
        Assert.Equal(ParentSessionResult.NotActive, expired.ParentSessionResp.Result);
        Assert.False(fx.Session.IsActive);
    }

    [Fact]
    public async Task Close_EndsSession_Idempotent()
    {
        Fixture fx = await CreateAsync();
        await SendAsync(fx, ParentSessionOp.Open, await VerifyAsync(fx.Auth, AuthCoordinator.ParentSessionActionContext));

        IpcPayload first = await SendAsync(fx, ParentSessionOp.Close);
        IpcPayload second = await SendAsync(fx, ParentSessionOp.Close);

        Assert.Equal(ParentSessionResult.Success, first.ParentSessionResp.Result);
        Assert.Equal(ParentSessionResult.Success, second.ParentSessionResp.Result);
        Assert.False(fx.Session.IsActive);
        Assert.Equal(ParentSessionResult.NotActive, (await SendAsync(fx, ParentSessionOp.Keepalive)).ParentSessionResp.Result);
    }

    /// <summary>ADR-149: phiên thuộc đúng 1 kết nối — phiên của kết nối khác (object khác) không mở gate.</summary>
    [Fact]
    public async Task Session_IsPerConnection_OtherConnectionNotAuthenticated()
    {
        Fixture fx = await CreateAsync();
        await SendAsync(fx, ParentSessionOp.Open, await VerifyAsync(fx.Auth, AuthCoordinator.ParentSessionActionContext));
        var otherConnection = new UiParentSession(fx.Clock);

        IpcPayload response = await fx.Audit.HandleAsync(
            new IpcPayload { MessageId = 20, AuditLogQuery = new AuditLogQuery { Page = 0, PageSize = 50 } },
            new AuditLogViewSession { ParentSession = otherConnection },
            CancellationToken.None);

        Assert.Equal(AuditLogQueryResult.InvalidToken, response.AuditLogResp.Result);
    }

    /// <summary>`PWD-024`/`FE-080`: đã đăng nhập thì tab Lịch sử tải trang đầu bằng token rỗng.</summary>
    [Fact]
    public async Task AuditLogQuery_ActiveParentSession_EmptyToken_Succeeds()
    {
        Fixture fx = await CreateAsync();
        await SendAsync(fx, ParentSessionOp.Open, await VerifyAsync(fx.Auth, AuthCoordinator.ParentSessionActionContext));

        IpcPayload response = await fx.Audit.HandleAsync(
            new IpcPayload { MessageId = 20, AuditLogQuery = new AuditLogQuery { Page = 0, PageSize = 50 } },
            new AuditLogViewSession { ParentSession = fx.Session },
            CancellationToken.None);

        Assert.Equal(AuditLogQueryResult.Success, response.AuditLogResp.Result);
    }

    [Fact]
    public async Task AuditLogQuery_ParentSessionExpired_EmptyToken_Rejected()
    {
        Fixture fx = await CreateAsync();
        await SendAsync(fx, ParentSessionOp.Open, await VerifyAsync(fx.Auth, AuthCoordinator.ParentSessionActionContext));
        fx.Clock.Now += (long)UiParentSession.IdleTimeout.TotalMilliseconds;

        IpcPayload response = await fx.Audit.HandleAsync(
            new IpcPayload { MessageId = 20, AuditLogQuery = new AuditLogQuery { Page = 0, PageSize = 50 } },
            new AuditLogViewSession { ParentSession = fx.Session },
            CancellationToken.None);

        Assert.Equal(AuditLogQueryResult.InvalidToken, response.AuditLogResp.Result);
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

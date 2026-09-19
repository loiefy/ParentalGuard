using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Ipc;
using ParentalGuard.Service.Security;
using ParentalGuard.Service.Tamper;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// ANTI-020 (Architecture/09-anti-tamper-architecture.md mục 5.5) — chỉ test được qua
/// <see cref="AuthCoordinator.HandleAsync"/> (production path), không cần pipe/UI thật. Registry
/// service/WFP thao tác thật (best-effort, mục 5.5 "1 bước lỗi không chặn các bước còn lại") — sandbox
/// test không có quyền SYSTEM vẫn phải PASS nhờ toàn bộ code path là best-effort/idempotent.
/// </summary>
public class UninstallCoordinatorTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-uninstall-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-uninstall-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task HandleAsync_InvalidToken_ReturnsInvalidTokenAndDoesNotStartDestructiveSteps()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var authCoordinator = new AuthCoordinator(_authDatPath, auditLog, new FakeMonotonicClock(), NullLogger.Instance);
        var wfpBlocker = new WfpVisionBlocker(NullLogger<WfpVisionBlocker>.Instance);
        var uninstallCoordinator = new UninstallCoordinator(authCoordinator, auditLog, wfpBlocker, CreateWatchdogSessionServerForTest(auditLog), NullLogger.Instance);

        IpcPayload request = new()
        {
            MessageId = 1,
            UninstallExecuteReq = new UninstallExecuteRequest { ActionToken = ByteString.CopyFrom([9, 9, 9, 9]), KeepAuditLog = false },
        };

        IpcPayload response = await uninstallCoordinator.HandleAsync(request, CancellationToken.None);

        Assert.Equal(UninstallResult.InvalidToken, response.UninstallExecuteResp.Result);
        Assert.False(uninstallCoordinator.ConsumeShutdownRequested());

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.DoesNotContain("UninstallInitiated", auditContent);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_ExecutesAndRequestsShutdownExactlyOnce()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var authCoordinator = new AuthCoordinator(_authDatPath, auditLog, new FakeMonotonicClock(), NullLogger.Instance);
        var wfpBlocker = new WfpVisionBlocker(NullLogger<WfpVisionBlocker>.Instance);
        var uninstallCoordinator = new UninstallCoordinator(authCoordinator, auditLog, wfpBlocker, CreateWatchdogSessionServerForTest(auditLog), NullLogger.Instance);

        IpcPayload setupResponse = await authCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, SetInitialPasswordReq = new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8("Passw0rd!") } }, CancellationToken.None);
        await authCoordinator.HandleAsync(
            new IpcPayload { MessageId = 2, ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = setupResponse.SetInitialPasswordResp.SetupToken, Confirmed = true } }, CancellationToken.None);
        IpcPayload verifyResponse = await authCoordinator.HandleAsync(
            new IpcPayload { MessageId = 3, AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFromUtf8("Passw0rd!"), ActionContext = "uninstall" } }, CancellationToken.None);

        IpcPayload request = new()
        {
            MessageId = 4,
            UninstallExecuteReq = new UninstallExecuteRequest { ActionToken = verifyResponse.AuthVerifyResp.ActionToken, KeepAuditLog = false },
        };

        IpcPayload response = await uninstallCoordinator.HandleAsync(request, CancellationToken.None);

        Assert.True(response.UninstallExecuteResp.Result is UninstallResult.Success or UninstallResult.PartialFailure);
        Assert.True(uninstallCoordinator.ConsumeShutdownRequested());
        Assert.False(uninstallCoordinator.ConsumeShutdownRequested()); // đúng 1 lần (Interlocked.Exchange tiêu thụ).

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.Contains("UninstallInitiated", auditContent);
    }

    /// <summary>Chỉ cần đủ để gọi <c>SuppressRecovery()</c> an toàn (không <c>.Start()</c>) — dependency chain thật (ADR-95 fix, xem <c>WatchdogSessionServer.SuppressRecovery</c>).</summary>
    private static WatchdogSessionServer CreateWatchdogSessionServerForTest(AuditLogWriter auditLog)
    {
        var overlaySupervisor = new ChildProcessSupervisor(
            ProcessType.Overlay, "test-pipe", "test.exe", TimeSpan.FromSeconds(1), null, new byte[32], auditLog, NullLogger.Instance);
        var iconStatus = new IconStatusCoordinator(overlaySupervisor);
        var attackPatternCoordinator = new AttackPatternCoordinator(auditLog, iconStatus);
        return new WatchdogSessionServer("test-watchdog-pipe", "watchdog.exe", auditLog, attackPatternCoordinator, NullLogger.Instance);
    }

    public void Dispose()
    {
        File.Delete(_authDatPath);
        File.Delete(_auditLogPath);
    }
}

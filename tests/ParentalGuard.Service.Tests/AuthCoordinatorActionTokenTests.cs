using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// ANTI-020 (Architecture/09-anti-tamper-architecture.md mục 5.3) — <c>TryConsumeActionTokenAsync</c>
/// là cổng xác thực cho luồng uninstall, tái dùng nguyên <c>action_token</c> đã có ở `08` mục 7.2.
/// </summary>
public class AuthCoordinatorActionTokenTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-token-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-token-{Guid.NewGuid():N}.log");

    private async Task<(AuthCoordinator Coordinator, FakeMonotonicClock Clock, byte[] ActionToken)> CreateWithValidTokenAsync(string actionContext = "uninstall")
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var coordinator = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);

        IpcPayload setupResponse = await coordinator.HandleAsync(Request(new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8("Passw0rd!") }), CancellationToken.None);
        await coordinator.HandleAsync(Request(new ConfirmRecoveryKeySavedRequest { SetupToken = setupResponse.SetInitialPasswordResp.SetupToken, Confirmed = true }), CancellationToken.None);

        IpcPayload verifyResponse = await coordinator.HandleAsync(
            Request(new AuthVerifyRequest { Password = ByteString.CopyFromUtf8("Passw0rd!"), ActionContext = actionContext }), CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyResponse.AuthVerifyResp.Result);

        return (coordinator, clock, verifyResponse.AuthVerifyResp.ActionToken.ToByteArray());
    }

    private static IpcPayload Request(SetInitialPasswordRequest req) => new() { MessageId = 1, SetInitialPasswordReq = req };

    private static IpcPayload Request(ConfirmRecoveryKeySavedRequest req) => new() { MessageId = 2, ConfirmRecoveryReq = req };

    private static IpcPayload Request(AuthVerifyRequest req) => new() { MessageId = 3, AuthVerifyReq = req };

    [Fact]
    public async Task TryConsumeActionTokenAsync_ValidTokenCorrectContext_ReturnsTrue()
    {
        (AuthCoordinator coordinator, _, byte[] token) = await CreateWithValidTokenAsync("uninstall");

        bool result = await coordinator.TryConsumeActionTokenAsync(token, "uninstall", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TryConsumeActionTokenAsync_TokenIsSingleUse_SecondAttemptFails()
    {
        (AuthCoordinator coordinator, _, byte[] token) = await CreateWithValidTokenAsync("uninstall");

        bool first = await coordinator.TryConsumeActionTokenAsync(token, "uninstall", CancellationToken.None);
        bool second = await coordinator.TryConsumeActionTokenAsync(token, "uninstall", CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task TryConsumeActionTokenAsync_WrongActionContext_ReturnsFalse()
    {
        // action_token phát hành cho "pause_monitoring" không được dùng để uninstall (least-privilege theo action_context).
        (AuthCoordinator coordinator, _, byte[] token) = await CreateWithValidTokenAsync("pause_monitoring");

        bool result = await coordinator.TryConsumeActionTokenAsync(token, "uninstall", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryConsumeActionTokenAsync_ExpiredToken_ReturnsFalse()
    {
        (AuthCoordinator coordinator, FakeMonotonicClock clock, byte[] token) = await CreateWithValidTokenAsync("uninstall");
        clock.Now += 16_000; // TTL 15s (PendingActionToken.Ttl)

        bool result = await coordinator.TryConsumeActionTokenAsync(token, "uninstall", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryConsumeActionTokenAsync_UnknownToken_ReturnsFalse()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var coordinator = new AuthCoordinator(_authDatPath, auditLog, new FakeMonotonicClock(), NullLogger.Instance);

        bool result = await coordinator.TryConsumeActionTokenAsync([1, 2, 3, 4], "uninstall", CancellationToken.None);

        Assert.False(result);
    }

    public void Dispose()
    {
        File.Delete(_authDatPath);
        File.Delete(_auditLogPath);
    }
}

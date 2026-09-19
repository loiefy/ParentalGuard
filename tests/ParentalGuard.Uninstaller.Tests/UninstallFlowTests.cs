using Google.Protobuf;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Uninstaller;

namespace ParentalGuard.Uninstaller.Tests;

/// <summary>
/// ADR-98 (Architecture/09-anti-tamper-architecture.md mục 5.3) — BẤT BIẾN AN TOÀN CỐT LÕI:
/// <c>Uninstaller.exe</c> KHÔNG BAO GIỜ tự xoá tài nguyên nếu chưa nhận
/// <c>UninstallExecuteResponse{result=SUCCESS}</c> từ Service. Test qua fake
/// <see cref="IUninstallServiceConnection"/>/<see cref="ILocalCleanup"/> — không cần pipe/Service thật.
/// </summary>
public class UninstallFlowTests
{
    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task RunAsync_WhenServiceUnreachable_NeverPromptsOrCleansUp()
    {
        var connection = new FakeConnection { ConnectThrows = true };
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);
        int promptCalls = 0;

        UninstallFlowResult result = await flow.RunAsync(
            (_, _) => { promptCalls++; return Task.FromResult<byte[]?>(Utf8("x")); },
            _ => Task.FromResult<bool?>(false),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.ServiceUnreachable, result.Outcome);
        Assert.Equal(0, promptCalls);
        Assert.False(cleanup.CleanupCalled);
    }

    [Fact]
    public async Task RunAsync_WhenUserCancelsAtPasswordPrompt_DoesNotCleanUp()
    {
        var connection = new FakeConnection();
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);

        UninstallFlowResult result = await flow.RunAsync(
            (_, _) => Task.FromResult<byte[]?>(null),
            _ => Task.FromResult<bool?>(false),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.CancelledByUser, result.Outcome);
        Assert.False(connection.ExecuteCalled);
        Assert.False(cleanup.CleanupCalled);
    }

    [Fact]
    public async Task RunAsync_WhenUserCancelsAtConfirmScreen_DoesNotExecuteOrCleanUp()
    {
        var connection = new FakeConnection { VerifyResponses = [new AuthVerifyResponse { Result = AuthResult.Success, ActionToken = ByteString.CopyFrom(Utf8("tok")) }] };
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);

        UninstallFlowResult result = await flow.RunAsync(
            (_, _) => Task.FromResult<byte[]?>(Utf8("pw")),
            _ => Task.FromResult<bool?>(null),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.CancelledByUser, result.Outcome);
        Assert.False(connection.ExecuteCalled);
        Assert.False(cleanup.CleanupCalled);
    }

    [Theory]
    [InlineData(UninstallResult.PartialFailure)]
    [InlineData(UninstallResult.InvalidToken)]
    public async Task RunAsync_WhenServiceDoesNotReturnSuccess_NeverCallsCleanup(UninstallResult serviceResult)
    {
        var connection = new FakeConnection
        {
            VerifyResponses = [new AuthVerifyResponse { Result = AuthResult.Success, ActionToken = ByteString.CopyFrom(Utf8("tok")) }],
            ExecuteResponse = new UninstallExecuteResponse { Result = serviceResult },
        };
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);

        UninstallFlowResult result = await flow.RunAsync(
            (_, _) => Task.FromResult<byte[]?>(Utf8("pw")),
            _ => Task.FromResult<bool?>(false),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.ExecutionFailed, result.Outcome);
        Assert.Equal(serviceResult, result.ServiceResult);
        Assert.True(connection.ExecuteCalled);
        Assert.False(cleanup.CleanupCalled); // ADR-98 — kể cả PARTIAL_FAILURE cũng KHÔNG tự xoá.
    }

    [Fact]
    public async Task RunAsync_WhenServiceReturnsSuccess_CallsCleanupExactlyOnce()
    {
        var connection = new FakeConnection
        {
            VerifyResponses = [new AuthVerifyResponse { Result = AuthResult.Success, ActionToken = ByteString.CopyFrom(Utf8("tok")) }],
            ExecuteResponse = new UninstallExecuteResponse { Result = UninstallResult.Success, AuditLogCopyPath = "C:\\Users\\x\\Desktop\\log.jsonl" },
        };
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);

        UninstallFlowResult result = await flow.RunAsync(
            (_, _) => Task.FromResult<byte[]?>(Utf8("pw")),
            _ => Task.FromResult<bool?>(true),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.Completed, result.Outcome);
        Assert.Equal(1, cleanup.CleanupCallCount);
        Assert.Equal("C:\\Users\\x\\Desktop\\log.jsonl", result.AuditLogCopyPath);
    }

    [Fact]
    public async Task RunAsync_WhenPipeBreaksMidVerify_ReturnsConnectionLostAndDoesNotCleanUp()
    {
        var connection = new FakeConnection { VerifyThrows = true };
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);

        UninstallFlowResult result = await flow.RunAsync(
            (_, _) => Task.FromResult<byte[]?>(Utf8("pw")),
            _ => Task.FromResult<bool?>(false),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.ConnectionLost, result.Outcome);
        Assert.False(connection.ExecuteCalled);
        Assert.False(cleanup.CleanupCalled);
    }

    [Fact]
    public async Task RunAsync_WhenPipeBreaksMidExecute_ReturnsConnectionLostAndDoesNotCleanUp()
    {
        var connection = new FakeConnection
        {
            VerifyResponses = [new AuthVerifyResponse { Result = AuthResult.Success, ActionToken = ByteString.CopyFrom(Utf8("tok")) }],
            ExecuteThrows = true,
        };
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);

        UninstallFlowResult result = await flow.RunAsync(
            (_, _) => Task.FromResult<byte[]?>(Utf8("pw")),
            _ => Task.FromResult<bool?>(false),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.ConnectionLost, result.Outcome);
        Assert.True(connection.ExecuteCalled);
        Assert.False(cleanup.CleanupCalled);
    }

    [Fact]
    public async Task RunAsync_WrongPasswordThenSuccess_RetriesAndEventuallySucceeds()
    {
        var connection = new FakeConnection
        {
            VerifyResponses =
            [
                new AuthVerifyResponse { Result = AuthResult.WrongPassword, ConsecutiveFailures = 1 },
                new AuthVerifyResponse { Result = AuthResult.Success, ActionToken = ByteString.CopyFrom(Utf8("tok")) },
            ],
            ExecuteResponse = new UninstallExecuteResponse { Result = UninstallResult.Success },
        };
        var cleanup = new FakeCleanup();
        var flow = new UninstallFlow(connection, cleanup);
        var seenPreviousAttempts = new List<AuthVerifyResponse?>();

        UninstallFlowResult result = await flow.RunAsync(
            (previous, _) => { seenPreviousAttempts.Add(previous); return Task.FromResult<byte[]?>(Utf8("pw")); },
            _ => Task.FromResult<bool?>(false),
            CancellationToken.None);

        Assert.Equal(UninstallFlowOutcome.Completed, result.Outcome);
        Assert.Equal(2, seenPreviousAttempts.Count);
        Assert.Null(seenPreviousAttempts[0]);
        Assert.Equal(AuthResult.WrongPassword, seenPreviousAttempts[1]!.Result);
        Assert.Equal(1, cleanup.CleanupCallCount);
    }

    private sealed class FakeConnection : IUninstallServiceConnection
    {
        public bool ConnectThrows { get; init; }

        public bool VerifyThrows { get; init; }

        public bool ExecuteThrows { get; init; }

        public AuthVerifyResponse[] VerifyResponses { get; init; } = [];

        public UninstallExecuteResponse ExecuteResponse { get; init; } = new() { Result = UninstallResult.Success };

        public bool ExecuteCalled { get; private set; }

        private int _verifyIndex;

        public Task ConnectAsync(CancellationToken cancellationToken) =>
            ConnectThrows ? throw new IOException("Service not reachable (test).") : Task.CompletedTask;

        public Task<AuthVerifyResponse> VerifyPasswordAsync(byte[] password, CancellationToken cancellationToken)
        {
            if (VerifyThrows)
            {
                throw new IOException("Pipe broke mid-verify (test).");
            }

            AuthVerifyResponse response = VerifyResponses[Math.Min(_verifyIndex, VerifyResponses.Length - 1)];
            _verifyIndex++;
            return Task.FromResult(response);
        }

        public Task<UninstallExecuteResponse> ExecuteUninstallAsync(byte[] actionToken, bool keepAuditLog, CancellationToken cancellationToken)
        {
            ExecuteCalled = true;
            if (ExecuteThrows)
            {
                throw new IOException("Pipe broke mid-execute (test).");
            }

            return Task.FromResult(ExecuteResponse);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCleanup : ILocalCleanup
    {
        public int CleanupCallCount { get; private set; }

        public bool CleanupCalled => CleanupCallCount > 0;

        public Task CleanupAsync(CancellationToken cancellationToken)
        {
            CleanupCallCount++;
            return Task.CompletedTask;
        }
    }
}

using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `AuthFacade.ChangePasswordAsync` (Architecture/10-ui-architecture.md mục 6.4, `08` mục 7.4) — cùng
/// mẫu hình loopback pipe với <see cref="PauseFacadeTests"/>. Chỉ phủ <c>ChangePasswordAsync</c> (thêm
/// ở Đợt 6 `S4`) — các method khác của <c>AuthFacade</c> đã phủ gián tiếp qua
/// <c>OnboardingViewModelTests</c>/<c>AuthPromptViewModel</c> ở giai đoạn trước.
/// </summary>
public sealed class AuthFacadeTests
{
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    [Theory]
    [InlineData(ChangeResult.Success, ChangeOutcome.Success)]
    [InlineData(ChangeResult.WrongOldPassword, ChangeOutcome.WrongOldPassword)]
    [InlineData(ChangeResult.LockedOut, ChangeOutcome.LockedOut)]
    [InlineData(ChangeResult.NewPasswordTooLong, ChangeOutcome.NewPasswordTooLong)]
    public async Task ChangePasswordAsync_MapsResultCorrectly(ChangeResult protoResult, ChangeOutcome expectedOutcome)
    {
        string pipeName = "test-auth-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.ChangePasswordReq, req.BodyCase);
            Assert.Equal(new byte[] { 1, 2 }, req.ChangePasswordReq.OldPassword.ToByteArray());
            Assert.Equal(new byte[] { 3, 4 }, req.ChangePasswordReq.NewPassword.ToByteArray());
            Assert.True(req.ChangePasswordReq.RegenerateRecoveryKey);
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.ChangePasswordResp = new ChangePasswordResponse { Result = protoResult });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new AuthFacade(client);

        byte[] oldPasswordPinned = GC.AllocateArray<byte>(2, pinned: true);
        oldPasswordPinned[0] = 1;
        oldPasswordPinned[1] = 2;
        byte[] newPasswordPinned = GC.AllocateArray<byte>(2, pinned: true);
        newPasswordPinned[0] = 3;
        newPasswordPinned[1] = 4;

        ChangePasswordResult result = await facade.ChangePasswordAsync(oldPasswordPinned, newPasswordPinned, regenerateRecoveryKey: true, CancellationToken.None);

        Assert.Equal(expectedOutcome, result.Outcome);
        await serverTask;
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task ChangePasswordAsync_SuccessWithRegenerate_ReturnsRecoveryKeyPlaintext()
    {
        string pipeName = "test-auth-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.ChangePasswordResp = new ChangePasswordResponse
            {
                Result = ChangeResult.Success,
                NewRecoveryKeyPlaintext = ByteString.CopyFromUtf8("ABCD-1234-EFGH-5678"),
            });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new AuthFacade(client);

        ChangePasswordResult result = await facade.ChangePasswordAsync(
            GC.AllocateArray<byte>(1, pinned: true),
            GC.AllocateArray<byte>(1, pinned: true),
            regenerateRecoveryKey: true,
            CancellationToken.None);

        Assert.Equal(ChangeOutcome.Success, result.Outcome);
        Assert.Equal("ABCD-1234-EFGH-5678", System.Text.Encoding.UTF8.GetString(result.NewRecoveryKeyPlaintextUtf8!));

        await serverTask;
        await client.DisconnectAsync();
    }

    [Theory]
    [InlineData(RecoveryResetResult.Success, RecoveryOutcome.Success)]
    [InlineData(RecoveryResetResult.WrongRecoveryKey, RecoveryOutcome.WrongRecoveryKey)]
    [InlineData(RecoveryResetResult.LockedOut, RecoveryOutcome.LockedOut)]
    [InlineData(RecoveryResetResult.NewPasswordTooLong, RecoveryOutcome.NewPasswordTooLong)]
    public async Task RecoveryResetAsync_MapsResultCorrectly(RecoveryResetResult protoResult, RecoveryOutcome expectedOutcome)
    {
        string pipeName = "test-auth-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.RecoveryResetReq, req.BodyCase);
            Assert.Equal(new byte[] { 5, 6 }, req.RecoveryResetReq.RecoveryKey.ToByteArray());
            Assert.Equal(new byte[] { 7, 8 }, req.RecoveryResetReq.NewPassword.ToByteArray());
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.RecoveryResetResp = new RecoveryResetResponse { Result = protoResult, LockoutUntilUnixMs = 12345 });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new AuthFacade(client);

        byte[] recoveryKeyPinned = GC.AllocateArray<byte>(2, pinned: true);
        recoveryKeyPinned[0] = 5;
        recoveryKeyPinned[1] = 6;
        byte[] newPasswordPinned = GC.AllocateArray<byte>(2, pinned: true);
        newPasswordPinned[0] = 7;
        newPasswordPinned[1] = 8;

        RecoveryResult result = await facade.RecoveryResetAsync(recoveryKeyPinned, newPasswordPinned, CancellationToken.None);

        Assert.Equal(expectedOutcome, result.Outcome);
        if (expectedOutcome == RecoveryOutcome.LockedOut)
        {
            Assert.Equal(12345, result.LockoutUntilUnixMs);
        }

        await serverTask;
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task RecoveryResetAsync_Success_ReturnsNewRecoveryKeyPlaintext()
    {
        string pipeName = "test-auth-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.RecoveryResetResp = new RecoveryResetResponse
            {
                Result = RecoveryResetResult.Success,
                NewRecoveryKeyPlaintext = ByteString.CopyFromUtf8("WXYZ-2345-IJKL-6789"),
            });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new AuthFacade(client);

        RecoveryResult result = await facade.RecoveryResetAsync(
            GC.AllocateArray<byte>(1, pinned: true),
            GC.AllocateArray<byte>(1, pinned: true),
            CancellationToken.None);

        Assert.Equal(RecoveryOutcome.Success, result.Outcome);
        Assert.Equal("WXYZ-2345-IJKL-6789", System.Text.Encoding.UTF8.GetString(result.NewRecoveryKeyPlaintextUtf8!));

        await serverTask;
        await client.DisconnectAsync();
    }

    private static async Task RunServerAsync(NamedPipeServerStream serverPipe, Func<byte[], Func<Task<IpcPayload>>, Task> handle)
    {
        await serverPipe.WaitForConnectionAsync();

        IpcPayload hello = await IpcFrameTransport.ReadFrameAsync(serverPipe, _unsignedHelloKey, CancellationToken.None);
        Assert.Equal(IpcPayload.BodyOneofCase.Hello, hello.BodyCase);

        byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
        IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Service, messageId: 1, correlationId: hello.MessageId);
        ack.HelloAck = new HelloAck { Accepted = true, SessionKey = ByteString.CopyFrom(sessionKey), ServerTimeUnixMs = 0 };
        await IpcFrameTransport.WriteFrameAsync(serverPipe, ack, _unsignedHelloKey, CancellationToken.None);

        await handle(sessionKey, () => IpcFrameTransport.ReadFrameAsync(serverPipe, sessionKey, CancellationToken.None));
    }

    private static ulong _nextServerMessageId = 2;

    private static Task RespondAsync(NamedPipeServerStream serverPipe, byte[] sessionKey, ulong correlationId, Action<IpcPayload> setBody)
    {
        IpcPayload resp = IpcEnvelope.NewEnvelope(ProcessType.Service, messageId: _nextServerMessageId++, correlationId: correlationId);
        setBody(resp);
        return IpcFrameTransport.WriteFrameAsync(serverPipe, resp, sessionKey, CancellationToken.None);
    }
}

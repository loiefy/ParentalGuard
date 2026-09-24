using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Tự viết server-side handshake/dispatch tối giản trong test (mô phỏng đúng hành vi
/// <c>UiSessionServer</c>, Architecture/03 mục 5.3) trên 1 named pipe loopback thật — xác nhận
/// <see cref="UiIpcClient"/> tự implement đúng khung khoá 2 giai đoạn (ADR-82/118/119), không cần
/// dựng cả <c>ParentalGuard.Service</c> thật.
/// </summary>
public sealed class UiIpcClientTests
{
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    [Fact]
    public async Task ConnectAsync_CompletesHandshake_AndSendRequestAsync_RoundTrips()
    {
        string pipeName = "test-ui-ipc-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task<byte[]> serverTask = RunFakeUiSessionServerAsync(serverPipe, respondToAuthStatus: true);

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        Assert.True(client.IsConnected);

        IpcPayload request = client.NewEnvelope();
        request.AuthStatusQuery = new AuthStatusQuery();
        bool configured = await client.SendRequestAsync(request, resp => resp.AuthStatusResp.PasswordConfigured, CancellationToken.None);

        Assert.True(configured);
        await serverTask; // đảm bảo server-side không ném lỗi.

        await client.DisconnectAsync();
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task SendRequestAsync_WithMismatchedCorrelationId_ThrowsUiIpcConnectionException_AndDisconnects()
    {
        string pipeName = "test-ui-ipc-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task<byte[]> serverTask = RunFakeUiSessionServerAsync(serverPipe, respondToAuthStatus: false, sendWrongCorrelationId: true);

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);

        IpcPayload request = client.NewEnvelope();
        request.AuthStatusQuery = new AuthStatusQuery();

        await Assert.ThrowsAsync<UiIpcConnectionException>(
            () => client.SendRequestAsync(request, resp => resp.AuthStatusResp.PasswordConfigured, CancellationToken.None));

        Assert.False(client.IsConnected);
        await serverTask;
    }

    /// <summary>Mô phỏng đúng <c>UiSessionServer.HandshakeAsync</c> (khoá hằng số → session_key ngẫu nhiên).</summary>
    private static async Task<byte[]> RunFakeUiSessionServerAsync(NamedPipeServerStream serverPipe, bool respondToAuthStatus, bool sendWrongCorrelationId = false)
    {
        await serverPipe.WaitForConnectionAsync();

        IpcPayload hello = await IpcFrameTransport.ReadFrameAsync(serverPipe, _unsignedHelloKey, CancellationToken.None);
        Assert.Equal(IpcPayload.BodyOneofCase.Hello, hello.BodyCase);
        Assert.Equal(ProcessType.Ui, hello.Hello.ProcessType);

        byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
        IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Service, messageId: 1, correlationId: hello.MessageId);
        ack.HelloAck = new HelloAck { Accepted = true, SessionKey = ByteString.CopyFrom(sessionKey), ServerTimeUnixMs = 0 };
        await IpcFrameTransport.WriteFrameAsync(serverPipe, ack, _unsignedHelloKey, CancellationToken.None);

        if (respondToAuthStatus)
        {
            IpcPayload req = await IpcFrameTransport.ReadFrameAsync(serverPipe, sessionKey, CancellationToken.None);
            IpcPayload resp = IpcEnvelope.NewEnvelope(ProcessType.Service, messageId: 2, correlationId: req.MessageId);
            resp.AuthStatusResp = new AuthStatusResponse { PasswordConfigured = true };
            await IpcFrameTransport.WriteFrameAsync(serverPipe, resp, sessionKey, CancellationToken.None);
        }
        else if (sendWrongCorrelationId)
        {
            IpcPayload req = await IpcFrameTransport.ReadFrameAsync(serverPipe, sessionKey, CancellationToken.None);
            IpcPayload resp = IpcEnvelope.NewEnvelope(ProcessType.Service, messageId: 2, correlationId: req.MessageId + 999);
            resp.AuthStatusResp = new AuthStatusResponse { PasswordConfigured = true };
            await IpcFrameTransport.WriteFrameAsync(serverPipe, resp, sessionKey, CancellationToken.None);
        }

        return sessionKey;
    }
}

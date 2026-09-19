using System.IO.Pipes;
using Google.Protobuf;
using ParentalGuard.Ipc;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Uninstaller;

/// <summary>
/// Client pipe <c>ParentalGuard.Svc.Uninstaller</c> (Architecture/09-anti-tamper-architecture.md mục
/// 5.2/5.3) — ephemeral (giống UI, không heartbeat định kỳ): connect timeout 10s, khoá phiên HMAC
/// thương lượng qua <c>Hello</c> chưa ký (ADR-19, cùng khuôn mẫu <c>UiSessionServer</c>).
/// </summary>
public sealed class UninstallPipeClient : IUninstallServiceConnection
{
    private static readonly byte[] _unsignedHelloKey = new byte[32];
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

    private readonly IpcMessageIdGenerator _messageIds = new();
    private NamedPipeClientStream? _pipe;
    private byte[]? _sessionKey;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", "ParentalGuard.Svc.Uninstaller", PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_connectTimeout);
        try
        {
            await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, timeoutCts.Token).ConfigureAwait(false);

            IpcPayload hello = IpcEnvelope.NewEnvelope(ProcessType.Uninstaller, _messageIds.Next());
            hello.Hello = new Hello
            {
                ProcessType = ProcessType.Uninstaller,
                Pid = (uint)Environment.ProcessId,
                ExecutablePath = Environment.ProcessPath ?? string.Empty,
                ProtocolVersion = IpcProtocol.CurrentVersion,
            };
            await IpcFrameTransport.WriteFrameAsync(pipe, hello, _unsignedHelloKey, timeoutCts.Token).ConfigureAwait(false);

            IpcPayload ack = await IpcFrameTransport.ReadFrameAsync(pipe, _unsignedHelloKey, timeoutCts.Token).ConfigureAwait(false);
            if (ack.BodyCase != IpcPayload.BodyOneofCase.HelloAck || !ack.HelloAck.Accepted)
            {
                throw new IOException("Handshake rejected by Service on Uninstaller pipe.");
            }

            _sessionKey = ack.HelloAck.SessionKey.ToByteArray();
            _pipe = pipe;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException("Không kết nối được ParentalGuard Service trong 10 giây.");
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AuthVerifyResponse> VerifyPasswordAsync(byte[] password, CancellationToken cancellationToken)
    {
        IpcPayload request = NewEnvelope();
        request.AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFrom(password), ActionContext = "uninstall" };
        IpcPayload response = await SendAndReceiveAsync(request, cancellationToken).ConfigureAwait(false);
        return response.AuthVerifyResp;
    }

    public async Task<UninstallExecuteResponse> ExecuteUninstallAsync(byte[] actionToken, bool keepAuditLog, CancellationToken cancellationToken)
    {
        IpcPayload request = NewEnvelope();
        request.UninstallExecuteReq = new UninstallExecuteRequest { ActionToken = ByteString.CopyFrom(actionToken), KeepAuditLog = keepAuditLog };
        IpcPayload response = await SendAndReceiveAsync(request, cancellationToken).ConfigureAwait(false);
        return response.UninstallExecuteResp;
    }

    private async Task<IpcPayload> SendAndReceiveAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        if (_pipe is null || _sessionKey is null)
        {
            throw new InvalidOperationException("ConnectAsync must succeed before sending requests.");
        }

        await IpcFrameTransport.WriteFrameAsync(_pipe, request, _sessionKey, cancellationToken).ConfigureAwait(false);
        return await IpcFrameTransport.ReadFrameAsync(_pipe, _sessionKey, cancellationToken).ConfigureAwait(false);
    }

    private IpcPayload NewEnvelope() => IpcEnvelope.NewEnvelope(ProcessType.Uninstaller, _messageIds.Next());

    public async ValueTask DisposeAsync()
    {
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}

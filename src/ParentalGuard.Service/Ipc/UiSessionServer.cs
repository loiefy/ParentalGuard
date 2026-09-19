using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Pipe server <c>ParentalGuard.Svc.UI</c> (Architecture/03 mục 2.1/4/5.3) — khác hẳn
/// <see cref="ChildProcessSupervisor"/> (Vision/Overlay): không spawn process (UI tự mở), không
/// heartbeat định kỳ (request/response theo nhu cầu), khoá HMAC ephemeral thương lượng ngay trong
/// phiên kết nối (ADR-19) thay vì bootstrap persistent qua anonymous pipe. Toàn bộ business logic
/// uỷ quyền cho <see cref="AuthCoordinator"/> — lớp này chỉ là transport glue.
/// </summary>
public sealed class UiSessionServer(
    string pipeName,
    string expectedExecutablePath,
    AuthCoordinator authCoordinator,
    AuditLogWriter auditLog,
    ILogger logger)
{
    /// <summary>
    /// Khung <c>Hello</c> đầu tiên của UI chưa có khoá phiên nào để ký ("chữ ký rỗng", mục 5.3) —
    /// cụ thể hoá bằng 1 khoá hằng số 32-byte-zero biết trước cả 2 phía, thay vì thêm 1 chế độ
    /// framing "không HMAC" riêng vào <see cref="IpcFrameTransport"/> dùng chung cho cả 3 kênh
    /// (giữ đơn giản — quyết định HOW cụ thể hoá, không đổi ý nghĩa "chữ ký rỗng" đã chốt ở ADR-19).
    /// </summary>
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    private readonly IpcMessageIdGenerator _messageIds = new();

    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public void Start(CancellationToken serviceStoppingToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(serviceStoppingToken);
        _runTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("UiSessionServer loop did not stop in time.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = PipeAclFactory.CreateUiServerInstance(pipeName);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);

                if (!VerifyClientIdentity(pipe, out string? actualPath))
                {
                    await auditLog.AppendAsync(
                        "IpcClientIdentityRejected", new { pipe = pipeName, expected = expectedExecutablePath, actual = actualPath }, CancellationToken.None)
                        .ConfigureAwait(false);
                    continue; // pipe đóng ở finally — vòng lặp tạo ngay pipe instance mới (mục 6)
                }

                byte[] sessionKey = await HandshakeAsync(pipe, token).ConfigureAwait(false);
                await RunConnectionAsync(pipe, sessionKey, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or IpcFrameViolationException or InvalidOperationException or Win32Exception)
            {
                // Đúng bảng xử lý lỗi mục 6 (`03-ipc-communication.md`): đóng kết nối, KHÔNG phản
                // hồi lỗi, tạo pipe instance mới ngay ở vòng lặp kế tiếp.
                logger.LogWarning(ex, "UI IPC session ended — accepting a new connection.");
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task<byte[]> HandshakeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        IpcPayload hello = await IpcFrameTransport.ReadFrameAsync(pipe, _unsignedHelloKey, token).ConfigureAwait(false);
        if (hello.BodyCase != IpcPayload.BodyOneofCase.Hello || hello.Hello.ProcessType != ProcessType.Ui)
        {
            throw new InvalidOperationException("Unexpected Hello message on UI pipe.");
        }

        byte[] sessionKey = RandomNumberGenerator.GetBytes(32); // ADR-19 — riêng cho phiên này, không lưu, không tái dùng.
        IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: hello.MessageId);
        ack.HelloAck = new HelloAck
        {
            Accepted = true,
            SessionKey = ByteString.CopyFrom(sessionKey),
            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        await IpcFrameTransport.WriteFrameAsync(pipe, ack, _unsignedHelloKey, token).ConfigureAwait(false);
        return sessionKey;
    }

    private async Task RunConnectionAsync(NamedPipeServerStream pipe, byte[] sessionKey, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            IpcPayload request = await IpcFrameTransport.ReadFrameAsync(pipe, sessionKey, token).ConfigureAwait(false);
            IpcPayload response = await authCoordinator.HandleAsync(request, token).ConfigureAwait(false);
            try
            {
                await IpcFrameTransport.WriteFrameAsync(pipe, response, sessionKey, token).ConfigureAwait(false);
            }
            finally
            {
                // Architecture/08 mục 5.5/ADR-83: Recovery Key plaintext (nếu response vừa gửi có
                // mang) chỉ được zero SAU KHI frame đã ghi xong pipe — chạy cả khi WriteFrameAsync
                // lỗi giữa chừng, không để sót buffer sống nếu pipe gãy.
                authCoordinator.ZeroRecoveryKeyPlaintextAfterSend();
            }
        }
    }

    /// <summary>Architecture/03 mục 4.2 điều kiện 3a — điều kiện 3b (chữ ký code-signing) chưa implement, cùng gap đã ghi nhận ở <c>ChildProcessSupervisor.VerifyClientIdentity</c> (Đợt 9, `DEV-012`).</summary>
    private bool VerifyClientIdentity(NamedPipeServerStream pipe, out string? actualExecutablePath)
    {
        actualExecutablePath = null;
        if (!PipeIdentityInterop.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid))
        {
            return false;
        }

        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById((int)pid);
            actualExecutablePath = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }

        return actualExecutablePath is not null && string.Equals(actualExecutablePath, expectedExecutablePath, StringComparison.OrdinalIgnoreCase);
    }
}

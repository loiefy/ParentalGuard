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
using ParentalGuard.Service.Tamper;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Pipe server <c>ParentalGuard.Svc.Uninstaller</c> (Architecture/09-anti-tamper-architecture.md mục
/// 5.2, ADR-92) — ACL/khoá phiên giống hệt pipe UI (ephemeral session key qua Hello chưa ký), nhưng
/// CHỈ whitelist <c>AuthVerifyRequest</c> (tái dùng <see cref="AuthCoordinator"/>) và
/// <c>UninstallExecuteRequest</c> (<see cref="UninstallCoordinator"/>) — least-privilege, không
/// whitelist bất kỳ message Password/Auth nào khác.
/// </summary>
public sealed class UninstallerSessionServer(
    string pipeName,
    string expectedExecutablePath,
    AuthCoordinator authCoordinator,
    UninstallCoordinator uninstallCoordinator,
    AuditLogWriter auditLog,
    Action requestServiceShutdown,
    ILogger logger)
{
    private static readonly byte[] _unsignedHelloKey = new byte[32]; // ADR-19, cùng khuôn mẫu UiSessionServer.

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
                logger.LogWarning("UninstallerSessionServer loop did not stop in time.");
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
                pipe = PipeAclFactory.CreateUiServerInstance(pipeName); // mục 5.2: ACL giống hệt pipe UI.
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);

                if (!VerifyClientIdentity(pipe, out string? actualPath))
                {
                    await auditLog.AppendAsync(
                        "IpcClientIdentityRejected", new { pipe = pipeName, expected = expectedExecutablePath, actual = actualPath }, CancellationToken.None)
                        .ConfigureAwait(false);
                    continue;
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
                logger.LogWarning(ex, "Uninstaller IPC session ended — accepting a new connection.");
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
        if (hello.BodyCase != IpcPayload.BodyOneofCase.Hello || hello.Hello.ProcessType != ProcessType.Uninstaller)
        {
            throw new InvalidOperationException("Unexpected Hello message on Uninstaller pipe.");
        }

        byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
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
            IpcPayload response = request.BodyCase switch
            {
                IpcPayload.BodyOneofCase.AuthVerifyReq => await authCoordinator.HandleAsync(request, token).ConfigureAwait(false),
                IpcPayload.BodyOneofCase.UninstallExecuteReq => await uninstallCoordinator.HandleAsync(request, token).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Message not whitelisted on Uninstaller pipe: {request.BodyCase}."),
            };

            try
            {
                await IpcFrameTransport.WriteFrameAsync(pipe, response, sessionKey, token).ConfigureAwait(false);
            }
            finally
            {
                authCoordinator.ZeroRecoveryKeyPlaintextAfterSend(); // no-op cho response Uninstall, có ý nghĩa nếu tương lai AuthVerify đổi hành vi.
            }

            // Bước 9 (mục 5.5, ADR-95) — chạy SAU KHI response đã ghi xong pipe, và SAU KHI Watchdog
            // đã Stopped hẳn (đã xảy ra bên trong UninstallCoordinator.HandleAsync trước khi trả response).
            if (uninstallCoordinator.ConsumeShutdownRequested())
            {
                requestServiceShutdown();
                return;
            }
        }
    }

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

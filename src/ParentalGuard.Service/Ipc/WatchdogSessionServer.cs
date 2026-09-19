using System.ComponentModel;
using System.IO.Pipes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Tamper;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Configuration;
using ParentalGuard.Service.Security;
using ParentalGuard.Service.Tamper;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Pipe server <c>ParentalGuard.Svc.Watchdog</c> (Architecture/09-anti-tamper-architecture.md mục
/// 3.2-3.4) — KHÔNG spawn process (Watchdog là Windows Service độc lập, tự khởi động qua SCM), chỉ
/// heartbeat 3s/3-miss (9s) và trình tự khôi phục đối xứng (ADR-88) khi phát hiện Watchdog mất tích.
/// </summary>
public sealed class WatchdogSessionServer(
    string pipeName,
    string expectedExecutablePath,
    AuditLogWriter auditLog,
    AttackPatternCoordinator attackPatternCoordinator,
    ILogger logger)
{
    private static readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(3);
    private static readonly string[] _restartedByWatchdogArgs = [RestartedByWatchdogDetector.Flag];

    private readonly IpcMessageIdGenerator _messageIds = new();

    private CancellationTokenSource? _cts;
    private Task? _runTask;

    // Uninstall (ADR-95/98, Architecture/09 mục 5.5 bước 4): UninstallCoordinator CHỦ ĐỘNG dừng
    // Watchdog — nếu không chặn ở đây, pipe vỡ do bước dừng đó sẽ khiến RunLoopAsync hiểu nhầm là
    // "Watchdog mất tích" và tự khởi động lại nó, phá hỏng trình tự uninstall (biến thể ngược của
    // race đã nêu ở ADR-95 cho chiều Watchdog→Service, nay áp dụng cho chiều Service→Watchdog).
    private volatile bool _recoverySuppressed;

    public void SuppressRecovery() => _recoverySuppressed = true;

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
                logger.LogWarning("WatchdogSessionServer loop did not stop in time.");
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
                pipe = PipeAclFactory.CreateWatchdogServerInstance(pipeName);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);

                if (!VerifyClientIdentity(pipe, out string? actualPath))
                {
                    await auditLog.AppendAsync(
                        "IpcClientIdentityRejected", new { pipe = pipeName, expected = expectedExecutablePath, actual = actualPath }, CancellationToken.None)
                        .ConfigureAwait(false);
                    continue;
                }

                await HandshakeAsync(pipe, token).ConfigureAwait(false);
                await RunConnectionAsync(pipe, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or IpcFrameViolationException or InvalidOperationException or Win32Exception)
            {
                if (_recoverySuppressed)
                {
                    logger.LogInformation("Watchdog IPC session ended during uninstall — recovery suppressed intentionally.");
                    break;
                }

                logger.LogWarning(ex, "Watchdog IPC session ended — recovering peer (ADR-88).");
                await RecoverWatchdogAsync().ConfigureAwait(false);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task HandshakeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        IpcPayload hello = await IpcFrameTransport.ReadFrameAsync(pipe, IpcProtocol.WatchdogPipeKey, token).ConfigureAwait(false);
        if (hello.BodyCase != IpcPayload.BodyOneofCase.Hello || hello.Hello.ProcessType != ProcessType.Watchdog)
        {
            throw new InvalidOperationException("Unexpected Hello message on Watchdog pipe.");
        }

        IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: hello.MessageId);
        ack.HelloAck = new HelloAck { Accepted = true, ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await IpcFrameTransport.WriteFrameAsync(pipe, ack, IpcProtocol.WatchdogPipeKey, token).ConfigureAwait(false);
    }

    private async Task RunConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        var outbound = Channel.CreateUnbounded<IpcPayload>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var heartbeatAcks = Channel.CreateUnbounded<ulong>();
        using CancellationTokenSource loopCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        Task readerTask = ReaderLoopAsync(pipe, heartbeatAcks.Writer, loopCts.Token);
        Task writerTask = WriterLoopAsync(pipe, outbound, loopCts.Token);
        Task pingTask = HeartbeatPingLoopAsync(outbound, heartbeatAcks.Reader, loopCts.Token);
        Task[] all = [readerTask, writerTask, pingTask];

        Task first = await Task.WhenAny(all).ConfigureAwait(false);
        await loopCts.CancelAsync().ConfigureAwait(false);

        foreach (Task other in all)
        {
            if (ReferenceEquals(other, first))
            {
                continue;
            }

            try
            {
                await other.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await first.ConfigureAwait(false);
    }

    private async Task ReaderLoopAsync(NamedPipeServerStream pipe, ChannelWriter<ulong> heartbeatAcks, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            IpcPayload message = await IpcFrameTransport.ReadFrameAsync(pipe, IpcProtocol.WatchdogPipeKey, token).ConfigureAwait(false);
            switch (message.BodyCase)
            {
                case IpcPayload.BodyOneofCase.HeartbeatAck:
                    heartbeatAcks.TryWrite(message.HeartbeatAck.Sequence);
                    break;
                case IpcPayload.BodyOneofCase.WatchdogReportEvent:
                    await HandleWatchdogReportEventAsync(pipe, message, token).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>Mục 3.6 — Watchdog là nơi duy nhất KHÔNG ghi audit.log trực tiếp; Service ghi thay dựa trên report này.</summary>
    private async Task HandleWatchdogReportEventAsync(NamedPipeServerStream pipe, IpcPayload message, CancellationToken token)
    {
        WatchdogReportEvent report = message.WatchdogReportEvent;
        switch (report.EventType)
        {
            case WatchdogEventType.PeerMissedHeartbeatRestarted:
            case WatchdogEventType.PeerRegistrationMissingRecreated:
                await auditLog.AppendAsync(
                    "ProcessRestarted",
                    new { process = report.TargetProcess, trigger = "watchdog_peer_recovery", action_taken = report.ActionTaken },
                    CancellationToken.None).ConfigureAwait(false);
                break;
            case WatchdogEventType.PeerRegistryTamperDetectedRestored:
                await auditLog.AppendAsync(
                    "TamperDetected",
                    new { key = report.TargetProcess, detected_by = "Watchdog" },
                    CancellationToken.None).ConfigureAwait(false);
                break;
            case WatchdogEventType.PeerRestartThresholdExceeded:
                attackPatternCoordinator.RecordWatchdogSideThresholdExceeded();
                break;
        }

        IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: message.MessageId);
        ack.WatchdogReportEventAck = new WatchdogReportEventAck { Received = true };
        await IpcFrameTransport.WriteFrameAsync(pipe, ack, IpcProtocol.WatchdogPipeKey, token).ConfigureAwait(false);
    }

    private async Task WriterLoopAsync(NamedPipeServerStream pipe, Channel<IpcPayload> outbound, CancellationToken token)
    {
        await foreach (IpcPayload payload in outbound.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            await IpcFrameTransport.WriteFrameAsync(pipe, payload, IpcProtocol.WatchdogPipeKey, token).ConfigureAwait(false);
        }
    }

    /// <summary>Architecture/09 mục 3.3 (ADR-87): 3 giây/chu kỳ, 3 lần miss liên tiếp (9s) = coi Watchdog mất tích.</summary>
    private async Task HeartbeatPingLoopAsync(Channel<IpcPayload> outbound, ChannelReader<ulong> heartbeatAcks, CancellationToken token)
    {
        int consecutiveMisses = 0;
        ulong sequence = 0;
        while (!token.IsCancellationRequested)
        {
            sequence++;
            IpcPayload ping = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next());
            ping.HeartbeatPing = new HeartbeatPing { Sequence = sequence, SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            await outbound.Writer.WriteAsync(ping, token).ConfigureAwait(false);

            bool acked = await WaitForAckAsync(heartbeatAcks, sequence, _heartbeatInterval, token).ConfigureAwait(false);
            consecutiveMisses = acked ? 0 : consecutiveMisses + 1;
            if (consecutiveMisses >= 3)
            {
                throw new InvalidOperationException("Watchdog missed 3 consecutive heartbeats (Architecture/09 mục 3.3).");
            }

            await Task.Delay(_heartbeatInterval, token).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitForAckAsync(ChannelReader<ulong> heartbeatAcks, ulong expectedSequence, TimeSpan timeout, CancellationToken token)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        waitCts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                ulong sequence = await heartbeatAcks.ReadAsync(waitCts.Token).ConfigureAwait(false);
                if (sequence == expectedSequence)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>ADR-88 mục 3.4 — trình tự khôi phục Watchdog (không đếm vào ANTI-060, mục 6.1 bảng chỉ liệt kê chiều ngược lại).</summary>
    private async Task RecoverWatchdogAsync()
    {
        try
        {
            PeerRecoveryResult result = await PeerServiceRecovery.RecoverAsync(
                InstallPaths.WatchdogServiceName,
                InstallPaths.WatchdogDisplayName,
                Path.GetFileName(InstallPaths.WatchdogExecutablePath),
                _restartedByWatchdogArgs,
                CancellationToken.None).ConfigureAwait(false);

            if (result.Outcome == PeerRecoveryOutcome.Failed)
            {
                logger.LogError("Failed to recover Watchdog: {Action}", result.ActionTaken);
                return;
            }

            await auditLog.AppendAsync(
                "ProcessRestarted",
                new { process = "Watchdog", trigger = "service_peer_recovery", action_taken = result.ActionTaken },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error recovering Watchdog.");
        }
    }

    /// <summary>Architecture/03 mục 4.2 điều kiện 3a — điều kiện 3b (code-signing) chưa implement (Đợt 9, `DEV-012`), cùng gap đã ghi nhận ở các pipe khác.</summary>
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

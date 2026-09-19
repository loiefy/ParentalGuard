using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Tamper;

namespace ParentalGuard.Watchdog;

/// <summary>
/// Client pipe <c>ParentalGuard.Svc.Watchdog</c> (Architecture/09-anti-tamper-architecture.md mục
/// 3.2-3.4) — KHÔNG dùng <c>IpcChildClient</c> (Vision/Overlay chỉ cần phát hiện pipe vỡ, không cần
/// tự phát hiện "Service còn sống nhưng treo"): <c>Watchdog</c> cần CẢ 2 hướng phát hiện (mục 3.3 —
/// "mất 3 heartbeat liên tiếp (9s) HOẶC pipe vỡ ngay lập tức"), nên tự theo dõi thời điểm
/// <c>HeartbeatPing</c> gần nhất bằng 1 monitor riêng thay vì chỉ phản ứng thụ động khi được ping.
/// </summary>
public sealed class WatchdogPeerConnection(ConcurrentQueue<WatchdogReportEvent> pendingReports, ILogger logger)
{
    private static readonly int[] _retryDelaysMs = [200, 400, 800, 1600, 3200];
    private const int _connectTimeoutMs = 2000;
    private static readonly TimeSpan _heartbeatMissThreshold = TimeSpan.FromSeconds(9); // ADR-87
    private static readonly string[] _restartedByWatchdogArgs = [RestartedByWatchdogDetector.Flag];
    private static readonly TimeSpan _attackWindow = TimeSpan.FromMinutes(30);
    private const int _attackThreshold = 5;

    private readonly IpcMessageIdGenerator _messageIds = new();
    private readonly SlidingWindowCounter _serviceRestartCounter = new(_attackThreshold, _attackWindow);

    public async Task RunForeverAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);
                await HandshakeAsync(pipe, cancellationToken).ConfigureAwait(false);
                await RunConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or IpcFrameViolationException or InvalidOperationException)
            {
                // Pipe vỡ HOẶC "Service treo" (missed heartbeat, ném từ HangMonitorAsync) — mục 3.3.
                logger.LogWarning(ex, "Watchdog↔Service connection ended — recovering Service (ADR-88).");
                await RecoverServiceAsync().ConfigureAwait(false);
            }
            finally
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<NamedPipeClientStream> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            var pipe = new NamedPipeClientStream(".", "ParentalGuard.Svc.Watchdog", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(_connectTimeoutMs, cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                int delayIndex = Math.Min(attempt, _retryDelaysMs.Length - 1);
                await Task.Delay(_retryDelaysMs[delayIndex], cancellationToken).ConfigureAwait(false);
                attempt++;
            }
        }
    }

    private async Task HandshakeAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        IpcPayload hello = IpcEnvelope.NewEnvelope(ProcessType.Watchdog, _messageIds.Next());
        hello.Hello = new Hello
        {
            ProcessType = ProcessType.Watchdog,
            Pid = (uint)Environment.ProcessId,
            ExecutablePath = Environment.ProcessPath ?? string.Empty,
            ProtocolVersion = IpcProtocol.CurrentVersion,
        };
        await IpcFrameTransport.WriteFrameAsync(pipe, hello, IpcProtocol.WatchdogPipeKey, cancellationToken).ConfigureAwait(false);

        IpcPayload response = await IpcFrameTransport.ReadFrameAsync(pipe, IpcProtocol.WatchdogPipeKey, cancellationToken).ConfigureAwait(false);
        if (response.BodyCase != IpcPayload.BodyOneofCase.HelloAck || !response.HelloAck.Accepted)
        {
            throw new IOException("Handshake rejected by Service on Watchdog pipe.");
        }
    }

    private async Task RunConnectionAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        var outbound = Channel.CreateUnbounded<IpcPayload>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var lastPingReceivedUtc = new StrongBox<DateTimeOffset>(DateTimeOffset.UtcNow);

        FlushPendingReports(outbound);

        using CancellationTokenSource loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task readerTask = ReaderLoopAsync(pipe, outbound.Writer, lastPingReceivedUtc, loopCts.Token);
        Task writerTask = WriterLoopAsync(pipe, outbound.Reader, loopCts.Token);
        Task hangMonitorTask = HangMonitorAsync(lastPingReceivedUtc, loopCts.Token);
        Task[] all = [readerTask, writerTask, hangMonitorTask];

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

    private void FlushPendingReports(Channel<IpcPayload> outbound)
    {
        while (pendingReports.TryDequeue(out WatchdogReportEvent? report))
        {
            IpcPayload payload = IpcEnvelope.NewEnvelope(ProcessType.Watchdog, _messageIds.Next());
            payload.WatchdogReportEvent = report;
            outbound.Writer.TryWrite(payload);
        }
    }

    private async Task ReaderLoopAsync(NamedPipeClientStream pipe, ChannelWriter<IpcPayload> outbound, StrongBox<DateTimeOffset> lastPingReceivedUtc, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            IpcPayload message = await IpcFrameTransport.ReadFrameAsync(pipe, IpcProtocol.WatchdogPipeKey, token).ConfigureAwait(false);
            if (message.BodyCase == IpcPayload.BodyOneofCase.HeartbeatPing)
            {
                lastPingReceivedUtc.Value = DateTimeOffset.UtcNow;
                IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Watchdog, _messageIds.Next(), correlationId: message.MessageId);
                ack.HeartbeatAck = new HeartbeatAck { Sequence = message.HeartbeatPing.Sequence, DiagnosticState = "alive" };
                await outbound.WriteAsync(ack, token).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriterLoopAsync(NamedPipeClientStream pipe, ChannelReader<IpcPayload> outbound, CancellationToken token)
    {
        await foreach (IpcPayload payload in outbound.ReadAllAsync(token).ConfigureAwait(false))
        {
            await IpcFrameTransport.WriteFrameAsync(pipe, payload, IpcProtocol.WatchdogPipeKey, token).ConfigureAwait(false);
        }
    }

    /// <summary>Mục 3.3 — phía client (Watchdog) tự phát hiện "Service còn kết nối nhưng ngừng ping" (treo), không chỉ chờ pipe vỡ.</summary>
    private static async Task HangMonitorAsync(StrongBox<DateTimeOffset> lastPingReceivedUtc, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            if (DateTimeOffset.UtcNow - lastPingReceivedUtc.Value > _heartbeatMissThreshold)
            {
                throw new InvalidOperationException("Service missed heartbeat pings for 9s (Architecture/09 mục 3.3).");
            }
        }
    }

    private async Task RecoverServiceAsync()
    {
        PeerRecoveryResult result = await PeerServiceRecovery.RecoverAsync(
            WatchdogPaths.ServiceName,
            WatchdogPaths.ServiceDisplayName,
            Path.GetFileName(WatchdogPaths.ServiceExecutablePath),
            _restartedByWatchdogArgs,
            CancellationToken.None).ConfigureAwait(false);

        if (result.Outcome == PeerRecoveryOutcome.Failed)
        {
            logger.LogError("Failed to recover Service: {Action}", result.ActionTaken);
            return;
        }

        var eventType = result.Outcome == PeerRecoveryOutcome.RecreatedThenStarted
            ? WatchdogEventType.PeerRegistrationMissingRecreated
            : WatchdogEventType.PeerMissedHeartbeatRestarted;
        pendingReports.Enqueue(new WatchdogReportEvent
        {
            EventType = eventType,
            TargetProcess = "Service",
            DetectedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActionTaken = result.ActionTaken,
        });

        bool exceeded = _serviceRestartCounter.RecordEventAndCheckThreshold(DateTimeOffset.UtcNow);
        if (exceeded)
        {
            pendingReports.Enqueue(new WatchdogReportEvent
            {
                EventType = WatchdogEventType.PeerRestartThresholdExceeded,
                TargetProcess = "Service",
                DetectedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ActionTaken = "watchdog_side_threshold_exceeded",
            });
        }
    }
}

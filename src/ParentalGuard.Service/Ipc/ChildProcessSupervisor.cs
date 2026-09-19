using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Vòng đời IPC + process cho 1 kênh con (<c>Vision</c> hoặc <c>Overlay</c>): tạo pipe instance
/// đúng ACL, spawn process, handshake, heartbeat, tự phục hồi khi crash/pipe vỡ
/// (Architecture/02-process-architecture.md mục 2.3/2.4/4, Architecture/03-ipc-communication.md
/// mục 4/6, BE-023). Đợt 1: Reader/Writer/Ping loop tách rời (tương tự ADR-39 phía
/// <c>IpcChildClient</c>) vì kênh <c>Vision</c>/<c>Overlay</c> giờ có nhiều nguồn ghi đồng thời
/// (heartbeat ping định kỳ + <see cref="TryEnqueueBusinessMessage"/> khi Service chủ động đẩy
/// <c>OverlayRectListCommand</c> mới) — không đồng bộ hoá sẽ interleave byte, vỡ framing.
/// </summary>
public sealed class ChildProcessSupervisor(
    ProcessType processType,
    string pipeName,
    string executablePath,
    TimeSpan heartbeatInterval,
    IReadOnlyList<Action<IpcPayload>>? initialPushBuilders,
    byte[] hmacKey,
    AuditLogWriter auditLog,
    ILogger logger,
    IReadOnlyList<Action<IpcPayload>>? oneTimeInitialMessages = null,
    Func<IpcPayload, CancellationToken, Task>? onBusinessMessage = null,
    Func<bool>? resolveLowIntegrityLevel = null,
    Action? onCaptureInitAccessDeniedExitCode = null,
    Action? onSessionConnected = null,
    Action? onSessionEnded = null)
{
    /// <summary>Architecture/05-image-pipeline-architecture.md mục 8.2: exit code 17 (CaptureInitAccessDenied).</summary>
    private const int _captureInitAccessDeniedExitCode = 17;

    // Architecture/03 mục 4.1: ngân sách connect+handshake sau spawn.
    private static readonly TimeSpan _connectBudget = TimeSpan.FromSeconds(2);

    // Giá trị chưa được chốt số cụ thể trong spec — placeholder hợp lý (tương tự DEFAULT_RISK_THRESHOLD).
    private const uint _gracefulStopDeadlineMs = 2000;

    private readonly IpcMessageIdGenerator _messageIds = new();

    private CancellationTokenSource? _sessionCts;
    private Task? _runTask;
    private volatile System.Diagnostics.Process? _currentProcess;

    // Kênh ghi dùng chung cho kết nối hiện tại (null khi chưa/không còn kết nối) — đọc/ghi qua
    // Volatile vì TryEnqueueBusinessMessage có thể được gọi từ thread khác (vd handler
    // VisionInferenceResult) đồng thời với RunLoopAsync đang tạo/huỷ kết nối.
    private volatile Channel<IpcPayload>? _outbound;

    // Gửi đúng 1 lần trong vòng đời supervisor (không lặp lại ở mỗi lần Vision/Overlay
    // crash-restart hay đổi session) — dùng cho thông báo theo sự kiện như ShowToastCommand
    // (BE-061b), khác OverlayRectListCommand/ControlVisionCommand vốn phản ánh state hiện hành
    // nên phải resend mỗi lần reconnect (Architecture/03 mục 4.3).
    private bool _oneTimeMessagesSent;

    public async Task StartForSessionAsync(uint sessionId, CancellationToken serviceStoppingToken)
    {
        await StopCurrentAsync().ConfigureAwait(false);

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(serviceStoppingToken);
        _runTask = Task.Run(() => RunLoopAsync(sessionId, _sessionCts.Token), CancellationToken.None);
    }

    public Task StopAsync() => StopCurrentAsync();

    /// <summary>Kill tiến trình con hiện tại để buộc supervisor tự phục hồi qua đúng luồng crash-restart (ADR-34).</summary>
    public void RequestChildRestart() => KillIfAlive(_currentProcess);

    /// <summary>
    /// Đẩy 1 message nghiệp vụ theo sự kiện (vd <c>OverlayRectListCommand</c> cập nhật khi
    /// Service đổi quyết định overlay — Architecture/03 mục 4.3) xuống kết nối hiện tại.
    /// Trả về <c>false</c> nếu hiện không có kết nối nào đang mở (chưa handshake xong, hoặc
    /// giữa 2 lần crash-restart) — caller không cần coi đây là lỗi, lần connect kế tiếp đã có
    /// <paramref name="configureInitialPush"/> gửi lại state hiện hành (fail-secure, không mất dữ liệu).
    /// </summary>
    public bool TryEnqueueBusinessMessage(Action<IpcPayload> configure)
    {
        Channel<IpcPayload>? outbound = _outbound;
        if (outbound is null)
        {
            return false;
        }

        IpcPayload payload = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next());
        configure(payload);
        return outbound.Writer.TryWrite(payload);
    }

    private async Task StopCurrentAsync()
    {
        if (_sessionCts is not null)
        {
            await _sessionCts.CancelAsync().ConfigureAwait(false);
            if (_runTask is not null)
            {
                try
                {
                    await _runTask.WaitAsync(TimeSpan.FromSeconds((_gracefulStopDeadlineMs / 1000.0) + 2)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    logger.LogWarning("{ProcessType} supervisor loop did not stop in time.", processType);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _sessionCts.Dispose();
            _sessionCts = null;
        }

        KillIfAlive(_currentProcess);
        _currentProcess = null;
    }

    private async Task RunLoopAsync(uint sessionId, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            System.Diagnostics.Process? childProcess = null;
            try
            {
                SecurityIdentifier userSid = SessionUserLookup.GetUserSid(sessionId);
                pipe = PipeAclFactory.CreateServerInstance(pipeName, userSid);

                bool useLowIntegrityLevel = resolveLowIntegrityLevel?.Invoke() ?? true;
                LaunchedChildProcess launched = ChildProcessLauncher.Launch(sessionId, executablePath, pipeName, IpcProtocol.CurrentVersion, hmacKey, useLowIntegrityLevel);
                childProcess = launched.Process;
                _currentProcess = childProcess;

                using (var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    acceptCts.CancelAfter(_connectBudget);
                    await pipe.WaitForConnectionAsync(acceptCts.Token).ConfigureAwait(false);
                }

                if (!VerifyClientIdentity(pipe, out string? actualPath))
                {
                    await auditLog.AppendAsync(
                        "IpcClientIdentityRejected",
                        new { pipe = pipeName, expected = executablePath, actual = actualPath },
                        CancellationToken.None).ConfigureAwait(false);
                    throw new InvalidOperationException("Named pipe client identity check failed.");
                }

                await HandshakeAsync(pipe, token).ConfigureAwait(false);
                onSessionConnected?.Invoke();

                var outbound = Channel.CreateUnbounded<IpcPayload>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                _outbound = outbound;

                // Architecture/03-ipc-communication.md mục 4.3: mỗi message push ban đầu (danh sách
                // rect hiện hành, trạng thái icon, layout icon đã lưu...) là 1 envelope riêng —
                // resend TOÀN BỘ mỗi lần (re)connect (khác oneTimeInitialMessages dưới, vốn chỉ gửi
                // đúng 1 lần trong vòng đời supervisor) vì đây là state hiện hành, không phải sự kiện.
                if (initialPushBuilders is { Count: > 0 })
                {
                    foreach (Action<IpcPayload> build in initialPushBuilders)
                    {
                        IpcPayload initial = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next());
                        build(initial);
                        outbound.Writer.TryWrite(initial);
                    }
                }

                if (!_oneTimeMessagesSent && oneTimeInitialMessages is { Count: > 0 })
                {
                    foreach (Action<IpcPayload> configureOneTime in oneTimeInitialMessages)
                    {
                        IpcPayload extra = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next());
                        configureOneTime(extra);
                        outbound.Writer.TryWrite(extra);
                    }

                    _oneTimeMessagesSent = true;
                }

                await RunConnectionAsync(pipe, outbound, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                if (pipe is not null)
                {
                    await SendGracefulStopAsync(pipe, "shutdown").ConfigureAwait(false);
                }

                break;
            }
            catch (Exception ex) when (ex is IOException or IpcFrameViolationException or InvalidOperationException or Win32Exception or OperationCanceledException)
            {
                logger.LogWarning(ex, "{ProcessType} IPC session ended, restarting.", processType);
                onSessionEnded?.Invoke(); // ADR-65 (Architecture/07 mục 4.1.2): respawn đang diễn ra → icon ERROR
                await auditLog.AppendAsync(
                    "ProcessRestarted", new { process = processType.ToString() }, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _outbound = null;
                _currentProcess = null;
                DetectCaptureInitAccessDeniedBestEffort(childProcess);
                KillIfAlive(childProcess);
                pipe?.Dispose();
            }
        }
    }

    /// <summary>
    /// Architecture/05 mục 8.1 bước 3-4: nếu <c>Vision</c> vừa tự thoát với exit code 17
    /// (nghi ngờ Low IL không tương thích Desktop Duplication API ở lần thử đầu), báo cho
    /// Worker để lần respawn kế tiếp dùng Medium IL — chỉ có ý nghĩa với kênh Vision
    /// (<paramref name="onCaptureInitAccessDeniedExitCode"/> null với kênh Overlay).
    /// </summary>
    private void DetectCaptureInitAccessDeniedBestEffort(System.Diagnostics.Process? childProcess)
    {
        if (onCaptureInitAccessDeniedExitCode is null || childProcess is null)
        {
            return;
        }

        try
        {
            if (childProcess.HasExited && childProcess.ExitCode == _captureInitAccessDeniedExitCode)
            {
                onCaptureInitAccessDeniedExitCode();
            }
        }
        catch (InvalidOperationException)
        {
            // Process chưa thực sự thoát (hiếm — race với KillIfAlive) — bỏ qua, không phải tín hiệu tin cậy.
        }
    }

    /// <summary>
    /// Reader/Writer/HeartbeatPing chạy song song trên cùng 1 kết nối (đối xứng ADR-39 phía
    /// <see cref="ParentalGuard.Ipc.Client.IpcChildClient"/>): khi 1 trong 3 kết thúc (exception
    /// hoặc cancellation), 2 vòng còn lại bị huỷ theo, rồi exception gốc (nếu có) được ném lại.
    /// </summary>
    private async Task RunConnectionAsync(NamedPipeServerStream pipe, Channel<IpcPayload> outbound, CancellationToken token)
    {
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
                // Kỳ vọng — loop kia bị huỷ chủ động ở trên, không phải lỗi gốc cần báo cáo.
            }
        }

        await first.ConfigureAwait(false);
    }

    private async Task ReaderLoopAsync(NamedPipeServerStream pipe, ChannelWriter<ulong> heartbeatAcks, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            IpcPayload message = await IpcFrameTransport.ReadFrameAsync(pipe, hmacKey, token).ConfigureAwait(false);
            if (message.BodyCase == IpcPayload.BodyOneofCase.HeartbeatAck)
            {
                heartbeatAcks.TryWrite(message.HeartbeatAck.Sequence);
            }
            else if (onBusinessMessage is not null)
            {
                await onBusinessMessage(message, token).ConfigureAwait(false);
            }
        }
    }

    private async Task WriterLoopAsync(NamedPipeServerStream pipe, Channel<IpcPayload> outbound, CancellationToken token)
    {
        await foreach (IpcPayload payload in outbound.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            await IpcFrameTransport.WriteFrameAsync(pipe, payload, hmacKey, token).ConfigureAwait(false);
        }
    }

    private async Task HandshakeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        IpcPayload hello = await IpcFrameTransport.ReadFrameAsync(pipe, hmacKey, token).ConfigureAwait(false);
        if (hello.BodyCase != IpcPayload.BodyOneofCase.Hello || hello.Hello.ProcessType != processType)
        {
            throw new InvalidOperationException("Unexpected Hello message.");
        }

        IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: hello.MessageId);
        ack.HelloAck = new HelloAck { Accepted = true, ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await IpcFrameTransport.WriteFrameAsync(pipe, ack, hmacKey, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Gửi <c>HeartbeatPing</c> định kỳ qua <paramref name="outbound"/> (không ghi trực tiếp pipe —
    /// tránh interleave với <see cref="WriterLoopAsync"/>/business message khác) và chờ
    /// <c>HeartbeatAck</c> tương ứng do <see cref="ReaderLoopAsync"/> đẩy vào <paramref name="heartbeatAcks"/>.
    /// </summary>
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

            bool acked = await WaitForAckAsync(heartbeatAcks, sequence, heartbeatInterval, token).ConfigureAwait(false);
            consecutiveMisses = acked ? 0 : consecutiveMisses + 1;
            if (consecutiveMisses >= 3)
            {
                throw new InvalidOperationException($"{processType} missed 3 consecutive heartbeats (BE-040).");
            }

            await Task.Delay(heartbeatInterval, token).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitForAckAsync(ChannelReader<ulong> heartbeatAcks, ulong expectedSequence, TimeSpan timeout, CancellationToken token)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        waitCts.CancelAfter(timeout);
        try
        {
            // Bỏ qua ack "cũ" tới muộn từ 1 chu kỳ trước (hiếm, race benign) — chỉ tính đạt khi
            // đúng sequence đang chờ tới trong ngân sách hiện tại (mục 4, BE-040).
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
            // Hết ngân sách chờ HeartbeatAck (mục 4) — tính là 1 lần miss, không phải lỗi kết nối.
            return false;
        }
    }

    private async Task SendGracefulStopAsync(NamedPipeServerStream pipe, string reason)
    {
        try
        {
            IpcPayload stop = IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next());
            stop.GracefulStop = new GracefulStopCommand { DeadlineMs = _gracefulStopDeadlineMs, Reason = reason };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await IpcFrameTransport.WriteFrameAsync(pipe, stop, hmacKey, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or IpcFrameViolationException)
        {
            // Pipe đã hỏng hoặc phía kia không phản hồi kịp — tiến trình con sẽ bị force-kill ở bước dọn dẹp.
        }
    }

    /// <summary>
    /// Architecture/03 mục 4.2 điều kiện 3a (đường dẫn cài đặt hợp lệ). Điều kiện 3b (chữ ký
    /// code-signing khớp thumbprint SignPath) CHƯA implement — cần chứng chỉ thật (SEC-030/
    /// DEV-012, Đợt 9). Không tự chế 1 pinned-thumbprint giả để tránh ảo tưởng an toàn (DEV-041).
    /// </summary>
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

        return actualExecutablePath is not null && string.Equals(actualExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void KillIfAlive(System.Diagnostics.Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Bao gồm ObjectDisposedException (kế thừa InvalidOperationException) — có thể xảy ra
            // nếu RequestChildRestart() và vòng lặp giám sát cùng thao tác 1 Process instance.
        }
        catch (Win32Exception)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}

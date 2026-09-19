using System.IO.Pipes;
using System.Threading.Channels;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Ipc.Client;

/// <summary>
/// Client IPC dùng chung cho <c>Vision</c> và <c>Overlay</c> (Architecture/03-ipc-communication.md
/// mục 4, Architecture/05-image-pipeline-architecture.md mục 3.2/ADR-39): connect-retry với
/// backoff, handshake Hello/HelloAck, Reader loop + Writer loop song song trên cùng 1 kết nối —
/// <see cref="EnqueueOutbound"/> cho phép thread khác (vd Thread Capture-Inference của Vision,
/// ADR-38) đẩy message nghiệp vụ mà không tự ghi trực tiếp lên pipe (tránh interleave byte, vỡ
/// framing — 03 mục 2.3).
/// </summary>
public sealed class IpcChildClient(ProcessType processType, ChildIpcBootstrap bootstrap)
{
    private static readonly int[] _retryDelaysMs = [200, 400, 800, 1600, 3200];
    private const int _connectTimeoutMs = 2000;

    private readonly IpcMessageIdGenerator _messageIds = new();

    // Unbounded (Architecture/05 mục 3.2): HeartbeatAck không được rớt, VisionInferenceResult
    // cũng phải gửi đủ — không áp dụng backpressure/drop ở tầng này.
    private readonly Channel<IpcPayload> _outbound = Channel.CreateUnbounded<IpcPayload>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>
    /// Chẩn đoán tự do hiển thị trong <c>HeartbeatAck.DiagnosticState</c> (vd "ep=cpu-fallback",
    /// ADR-45) — chỉ phục vụ chẩn đoán, không dùng để kích hoạt logic nghiệp vụ phía Service.
    /// </summary>
    public volatile string DiagnosticState = "alive";

    /// <summary>
    /// Đẩy 1 message nghiệp vụ vào hàng chờ ghi pipe, thread-safe — gọi được từ bất kỳ thread
    /// nào (kể cả Thread Capture-Inference chuyên dụng của Vision, ADR-38/39). Không tự tạo
    /// envelope — caller dùng <see cref="NewEnvelope"/>.
    /// </summary>
    public bool EnqueueOutbound(IpcPayload payload) => _outbound.Writer.TryWrite(payload);

    /// <summary>Tạo envelope mới với đúng <c>sender</c>/<c>message_id</c> của client này.</summary>
    public IpcPayload NewEnvelope(ulong correlationId = 0) => IpcEnvelope.NewEnvelope(processType, _messageIds.Next(), correlationId);

    /// <summary>
    /// Chạy vô hạn: connect → handshake → Reader/Writer loop song song; tự reconnect khi pipe vỡ
    /// (03 mục 6 "Service restart/crash"). Trả về khi nhận <c>GracefulStopCommand</c>
    /// (ném <see cref="GracefulStopRequestedException"/>) hoặc <paramref name="cancellationToken"/> bị huỷ.
    /// </summary>
    /// <param name="onDisconnected">
    /// Gọi mỗi khi kết nối vừa vỡ, trước khi vào lại vòng lặp retry-connect (03 mục 6) — Vision
    /// dùng để tự park (coi như <c>monitoring_enabled = false</c>) trong lúc chờ kết nối lại, vì
    /// không còn nguồn cấu hình đáng tin cậy nào (`SEC-017`). Không gọi khi thoát do
    /// <see cref="GracefulStopRequestedException"/> hoặc <paramref name="cancellationToken"/> huỷ
    /// (tiến trình sắp thoát hẳn, không có ý nghĩa "chờ reconnect").
    /// </param>
    public async Task RunForeverAsync(Func<IpcPayload, CancellationToken, Task>? onBusinessMessage, CancellationToken cancellationToken, Action? onDisconnected = null)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);
                await HandshakeAsync(pipe, cancellationToken).ConfigureAwait(false);
                await RunConnectionAsync(pipe, onBusinessMessage, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // Pipe vỡ (Service restart/crash) — vào lại vòng lặp retry-connect (03 mục 6).
                onDisconnected?.Invoke();
            }
            catch (IpcFrameViolationException)
            {
                // Message hỏng/HMAC sai từ phía server — không tin cậy, reconnect thay vì crash tiến trình.
                onDisconnected?.Invoke();
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
            var pipe = new NamedPipeClientStream(".", bootstrap.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
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
        IpcPayload hello = NewEnvelope();
        hello.Hello = new Hello
        {
            ProcessType = processType,
            Pid = (uint)Environment.ProcessId,
            ExecutablePath = Environment.ProcessPath ?? string.Empty,
            ProtocolVersion = bootstrap.ProtocolVersion,
        };
        await IpcFrameTransport.WriteFrameAsync(pipe, hello, bootstrap.HmacKey, cancellationToken).ConfigureAwait(false);

        IpcPayload response = await IpcFrameTransport.ReadFrameAsync(pipe, bootstrap.HmacKey, cancellationToken).ConfigureAwait(false);
        if (response.BodyCase != IpcPayload.BodyOneofCase.HelloAck || !response.HelloAck.Accepted)
        {
            throw new IOException("Handshake rejected by server.");
        }
    }

    /// <summary>
    /// ADR-39: Reader loop và Writer loop chạy song song trên cùng 1 kết nối, không luồng nào
    /// tự ghi trực tiếp — mọi write đi qua <see cref="_outbound"/> để tránh interleave byte.
    /// Khi 1 trong 2 vòng lặp kết thúc (exception hoặc cancellation), vòng lặp còn lại bị huỷ
    /// theo, rồi exception gốc (nếu có) được ném lại cho <see cref="RunForeverAsync"/> xử lý.
    /// </summary>
    private async Task RunConnectionAsync(NamedPipeClientStream pipe, Func<IpcPayload, CancellationToken, Task>? onBusinessMessage, CancellationToken cancellationToken)
    {
        using CancellationTokenSource loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task readerTask = ReaderLoopAsync(pipe, onBusinessMessage, loopCts.Token);
        Task writerTask = WriterLoopAsync(pipe, loopCts.Token);

        Task first = await Task.WhenAny(readerTask, writerTask).ConfigureAwait(false);
        await loopCts.CancelAsync().ConfigureAwait(false);

        Task other = ReferenceEquals(first, readerTask) ? writerTask : readerTask;
        try
        {
            await other.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Kỳ vọng — loop kia bị huỷ chủ động ở trên, không phải lỗi gốc cần báo cáo.
        }

        await first.ConfigureAwait(false);
    }

    private async Task ReaderLoopAsync(NamedPipeClientStream pipe, Func<IpcPayload, CancellationToken, Task>? onBusinessMessage, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IpcPayload message = await IpcFrameTransport.ReadFrameAsync(pipe, bootstrap.HmacKey, cancellationToken).ConfigureAwait(false);
            switch (message.BodyCase)
            {
                case IpcPayload.BodyOneofCase.HeartbeatPing:
                    EnqueueHeartbeatAck(message);
                    break;
                case IpcPayload.BodyOneofCase.GracefulStop:
                    throw new GracefulStopRequestedException(message.GracefulStop.DeadlineMs, message.GracefulStop.Reason);
                default:
                    if (onBusinessMessage is not null)
                    {
                        await onBusinessMessage(message, cancellationToken).ConfigureAwait(false);
                    }

                    break;
            }
        }
    }

    private async Task WriterLoopAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        await foreach (IpcPayload payload in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await IpcFrameTransport.WriteFrameAsync(pipe, payload, bootstrap.HmacKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnqueueHeartbeatAck(IpcPayload pingEnvelope)
    {
        IpcPayload ack = NewEnvelope(correlationId: pingEnvelope.MessageId);
        ack.HeartbeatAck = new HeartbeatAck
        {
            Sequence = pingEnvelope.HeartbeatPing.Sequence,
            DiagnosticState = DiagnosticState,
        };
        EnqueueOutbound(ack);
    }
}

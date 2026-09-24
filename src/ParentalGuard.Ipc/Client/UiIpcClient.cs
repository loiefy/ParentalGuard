using System.IO.Pipes;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Ipc.Client;

/// <summary>
/// Client IPC cho <c>ParentalGuard.UI</c> (Architecture/03-ipc-communication.md mục 5.3,
/// Architecture/10-ui-architecture.md mục 3.2, ADR-118/119/120) — KHÁC <see cref="IpcChildClient"/>
/// (dùng chung cho Vision/Overlay) ở 2 điểm nền tảng: (1) không bootstrap khoá HMAC qua handle kế
/// thừa <see cref="ChildIpcBootstrap"/> — <c>UI</c> không phải tiến trình con của <c>Service</c>
/// (ADR-118); (2) khung khoá 2 giai đoạn — <c>Hello</c>/<c>HelloAck</c> đầu tiên ký bằng khoá hằng số
/// 32-byte-zero (ADR-82), mọi frame sau ký bằng <c>session_key</c> ephemeral nhận được trong chính
/// <c>HelloAck</c> đó. Không có Reader/Writer loop song song (ADR-119) — kênh <c>UI</c> vốn
/// request/response thuần, không có push từ <c>Service</c> ngoài lúc handshake.
/// </summary>
public sealed class UiIpcClient(string pipeName = UiIpcClient.DefaultPipeName)
{
    public const string DefaultPipeName = "ParentalGuard.Svc.UI";

    /// <summary>
    /// Khoá ký đúng 2 frame <c>Hello</c>/<c>HelloAck</c> đầu tiên, TRƯỚC KHI có session_key (03 mục
    /// 5.3, ADR-82) — KHÔNG phải bí mật (hard-code cả 2 phía): xác thực danh tính thật của UI đã
    /// hoàn tất ở tầng ACL + chữ ký code-signing (03 mục 4.2) trước khi Service đọc byte đầu tiên.
    /// </summary>
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    private const int _connectTimeoutMs = 3000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IpcMessageIdGenerator _messageIds = new();

    private NamedPipeClientStream? _pipe;
    private byte[]? _sessionKey;

    /// <summary><c>false</c> ngay sau khi 1 request ném lỗi pipe/framing (mục 3.2) — không tự hồi phục ngầm.</summary>
    public bool IsConnected => _pipe is not null && _sessionKey is not null;

    /// <summary>Tạo envelope mới với đúng <c>sender=UI</c>/<c>message_id</c> tăng dần theo phiên kết nối hiện tại.</summary>
    public IpcPayload NewEnvelope(ulong correlationId = 0) => IpcEnvelope.NewEnvelope(ProcessType.Ui, _messageIds.Next(), correlationId);

    /// <summary>
    /// 1 lần connect (mục 3.2 — KHÔNG lặp vô hạn như <see cref="IpcChildClient"/>: hành động
    /// user-driven, cần phản hồi nhanh cho UX). Timeout 3s; ném <see cref="UiIpcConnectionException"/>
    /// nếu thất bại — caller (Facade/ViewModel) tự quyết định hiển thị lỗi + nút "Thử lại" (mục 9).
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false); // dọn phiên cũ (nếu có) trước khi mở phiên mới — tránh leak pipe handle.

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(_connectTimeoutMs, cancellationToken).ConfigureAwait(false);
            byte[] sessionKey = await HandshakeAsync(pipe, cancellationToken).ConfigureAwait(false);
            _pipe = pipe;
            _sessionKey = sessionKey;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or IpcFrameViolationException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new UiIpcConnectionException("Không thể kết nối tới ParentalGuard Service.", ex);
        }
    }

    /// <summary>Mục 5.3/ADR-82 — Hello ký khoá hằng số → nhận session_key từ HelloAck.</summary>
    private async Task<byte[]> HandshakeAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        IpcPayload hello = NewEnvelope();
        hello.Hello = new Hello
        {
            ProcessType = ProcessType.Ui,
            Pid = (uint)Environment.ProcessId,
            ExecutablePath = Environment.ProcessPath ?? string.Empty,
            ProtocolVersion = IpcProtocol.CurrentVersion,
        };
        await IpcFrameTransport.WriteFrameAsync(pipe, hello, _unsignedHelloKey, cancellationToken).ConfigureAwait(false);

        IpcPayload response = await IpcFrameTransport.ReadFrameAsync(pipe, _unsignedHelloKey, cancellationToken).ConfigureAwait(false);
        if (response.BodyCase != IpcPayload.BodyOneofCase.HelloAck || !response.HelloAck.Accepted)
        {
            throw new IOException("Handshake rejected by server.");
        }

        return response.HelloAck.SessionKey.ToByteArray();
    }

    /// <summary>
    /// Ghi request (ký session_key hiện tại) → đọc frame kế tiếp khớp <c>correlation_id</c> → trả về
    /// qua <paramref name="selectResponse"/> (mục 3.2). <see cref="SemaphoreSlim"/> bọc quanh toàn bộ
    /// hàm (ADR-119) — 2 lời gọi đồng thời từ 2 Facade khác nhau không interleave byte trên cùng pipe.
    /// </summary>
    public async Task<TResp> SendRequestAsync<TResp>(IpcPayload request, Func<IpcPayload, TResp> selectResponse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(selectResponse);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pipe is null || _sessionKey is null)
            {
                throw new UiIpcConnectionException("Chưa kết nối tới ParentalGuard Service.");
            }

            await IpcFrameTransport.WriteFrameAsync(_pipe, request, _sessionKey, cancellationToken).ConfigureAwait(false);
            IpcPayload response = await IpcFrameTransport.ReadFrameAsync(_pipe, _sessionKey, cancellationToken).ConfigureAwait(false);
            if (response.CorrelationId != request.MessageId)
            {
                throw new IpcFrameViolationException("Response correlation_id does not match request.");
            }

            return selectResponse(response);
        }
        catch (Exception ex) when (ex is IOException or IpcFrameViolationException)
        {
            // Pipe vỡ giữa chừng hoặc vi phạm framing — không retry ngầm (mục 3.2), đóng phiên hiện tại.
            await DisconnectCoreAsync().ConfigureAwait(false);
            throw new UiIpcConnectionException("Mất kết nối tới ParentalGuard Service.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Đóng pipe chủ động (gọi lúc App thoát, hoặc trước khi mở phiên kết nối mới).</summary>
    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DisconnectCoreAsync()
    {
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }

        _sessionKey = null;
    }
}

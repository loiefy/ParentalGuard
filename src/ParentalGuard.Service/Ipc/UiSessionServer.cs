using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Config;
using ParentalGuard.Service.Dashboard;
using ParentalGuard.Service.Data;
using ParentalGuard.Service.Pause;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Pipe server <c>ParentalGuard.Svc.UI</c> (Architecture/03 mục 2.1/4/5.3) — khác hẳn
/// <see cref="ChildProcessSupervisor"/> (Vision/Overlay): không spawn process (UI tự mở), không
/// heartbeat định kỳ (request/response theo nhu cầu), khoá HMAC ephemeral thương lượng ngay trong
/// phiên kết nối (ADR-19) thay vì bootstrap persistent qua anonymous pipe. Business logic uỷ quyền
/// cho <see cref="AuthCoordinator"/> (domain Password/Auth, field 80-91),
/// <see cref="PauseCoordinator"/> (domain Pause/Resume, field 92-97, Đợt 5),
/// <see cref="ConfigCoordinator"/> (domain Cài đặt, field 148-153, Đợt 7) hoặc
/// <see cref="AuditLogCoordinator"/> (domain Lịch sử, field 142/146-147/154-155, Đợt 7-8) theo whitelist message
/// của pipe này (Architecture/03 mục 3.1a) — lớp này chỉ là transport glue + định tuyến.
/// </summary>
public sealed class UiSessionServer(
    string pipeName,
    string expectedExecutablePath,
    AuthCoordinator authCoordinator,
    PauseCoordinator pauseCoordinator,
    ConfigCoordinator configCoordinator,
    AuditLogCoordinator auditLogCoordinator,
    DashboardCoordinator dashboardCoordinator,
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
            catch (Exception ex) when (ex is IOException or IpcFrameViolationException or InvalidOperationException or Win32Exception or ConfigLoadException)
            {
                // Đúng bảng xử lý lỗi mục 6 (`03-ipc-communication.md`): đóng kết nối, KHÔNG phản
                // hồi lỗi, tạo pipe instance mới ngay ở vòng lặp kế tiếp. ConfigLoadException thêm
                // vào đây làm lưới an toàn tầng ngoài (defense in depth) — các coordinator domain
                // (Pause/Auth) đã tự bắt exception này ở nơi phát sinh; nếu 1 exception vẫn lọt ra
                // tới đây, vẫn phải đóng kết nối hiện tại thay vì để crash toàn bộ vòng lặp
                // <c>RunLoopAsync</c> (Task.Run — không ai observe), khiến MỌI kết nối UI sau đó
                // treo vĩnh viễn tới khi Service restart.
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
        // Audit fix 2026-09-28 (xem AuditLogCoordinator class doc): gate "đã qua view_audit_log" phải
        // sống trong ĐÚNG 1 kết nối pipe này — 1 instance mới mỗi lần RunConnectionAsync bắt đầu, không
        // bao giờ chia sẻ giữa 2 kết nối, tự giải phóng khi vòng lặp dưới đây kết thúc.
        var auditViewSession = new AuditLogViewSession();
        while (!token.IsCancellationRequested)
        {
            IpcPayload request = await IpcFrameTransport.ReadFrameAsync(pipe, sessionKey, token).ConfigureAwait(false);
            IpcPayload response = await DispatchAsync(request, auditViewSession, token).ConfigureAwait(false);
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

    /// <summary>
    /// 4 domain cùng chia sẻ pipe <c>UI</c> (Architecture/03 mục 3.1a): Pause/Resume/Status đi qua
    /// <see cref="PauseCoordinator"/>, Cài đặt (`ConfigQuery`/`ConfigUpdateRequest`/
    /// `RemoveWhitelistEntryRequest`) đi qua <see cref="ConfigCoordinator"/>, Lịch sử
    /// (`AuditLogQuery`/`MarkFalsePositiveRequest`) đi qua <see cref="AuditLogCoordinator"/>, còn lại
    /// (Password/Auth) đi qua <see cref="AuthCoordinator"/>.
    /// </summary>
    private Task<IpcPayload> DispatchAsync(IpcPayload request, AuditLogViewSession auditViewSession, CancellationToken token) => request.BodyCase switch
    {
        IpcPayload.BodyOneofCase.PauseMonitoringReq or
        IpcPayload.BodyOneofCase.ResumeMonitoringReq or
        IpcPayload.BodyOneofCase.PauseStatusQuery or
        IpcPayload.BodyOneofCase.AckPauseAnomalyReq => pauseCoordinator.HandleAsync(request, token),
        IpcPayload.BodyOneofCase.ConfigQuery or
        IpcPayload.BodyOneofCase.ConfigUpdateReq or
        IpcPayload.BodyOneofCase.RemoveWhitelistReq => configCoordinator.HandleAsync(request, token),
        IpcPayload.BodyOneofCase.AuditLogQuery or
        IpcPayload.BodyOneofCase.MarkFalsePositiveReq or
        IpcPayload.BodyOneofCase.VerifyAuditChainReq => auditLogCoordinator.HandleAsync(request, auditViewSession, token),
        IpcPayload.BodyOneofCase.DashboardStatusQuery or
        IpcPayload.BodyOneofCase.AuditChartQuery => dashboardCoordinator.HandleAsync(request, token),
        _ => authCoordinator.HandleAsync(request, token),
    };

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

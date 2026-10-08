using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Bug real-hardware 2026-10-08: đăng xuất → đăng nhập lại, Vision/Overlay không được dựng lại cho session mới. Vòng lặp cũ bị
/// huỷ lúc pipe còn chờ tiến trình con connect → gửi GracefulStop lên pipe chưa kết nối ném InvalidOperationException, lỗi
/// lan ngược làm hỏng việc chuyển session.
/// </summary>
public sealed class SessionSwitchRegressionTests : IDisposable
{
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-ss-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task GracefulStop_OnPipeNotYetConnected_DoesNotThrow()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var supervisor = new ChildProcessSupervisor(
            ProcessType.Vision, "unused", "unused.exe", TimeSpan.FromSeconds(2), null, new byte[32], auditLog, NullLogger.Instance);
        using var pipe = new NamedPipeServerStream($"pg-test-{Guid.NewGuid():N}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        Exception? thrown = await Record.ExceptionAsync(() => supervisor.SendGracefulStopAsync(pipe, "shutdown"));

        Assert.Null(thrown);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_auditLogPath);
        }
        catch (IOException)
        {
        }
    }
}

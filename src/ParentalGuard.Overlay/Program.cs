using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Overlay.Rendering;

namespace ParentalGuard.Overlay;

// Đợt 1 (BE-023a/BE-033) + Đợt 2 (Architecture/07-overlay-architecture.md): overlay blur + vùng
// loại trừ 3 lớp (FE-016) + chế độ gộp (BE-088/089) + icon trạng thái multi-monitor (FE-020-022).
// Tiến trình này chỉ được Service spawn qua ChildProcessLauncher (bootstrap handle truyền qua
// STD_INPUT_HANDLE — Architecture/03 mục 5.2).
internal static class Program
{
    // ADR-61: bắt buộc, gọi sớm nhất trước khi tạo bất kỳ Form nào — điều kiện để GetDpiForWindow
    // trả đúng giá trị per-monitor và WinForms tự scale đúng trên máy nhiều màn hình DPI khác nhau.
    private const int _dpiAwarenessContextPerMonitorAwareV2 = -4;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // Log chẩn đoán runtime (Đợt 9) — xem ghi chú đầy đủ ở ParentalGuard.Vision/Program.cs. TẮT theo
    // mặc định qua [Conditional], bật bằng -p:ParentalGuardDiagnosticLog=true.
    [Conditional("PARENTALGUARD_DIAGNOSTIC_LOG")]
    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText(@"C:\PGDebugLog\parentalguard-overlay-debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
        }
    }

    [STAThread]
    private static int Main()
    {
        DebugLog("Overlay Main() bắt đầu.");
        SetProcessDpiAwarenessContext(new IntPtr(_dpiAwarenessContextPerMonitorAwareV2));

        ChildIpcBootstrap bootstrap;
        try
        {
            DebugLog("Trước ReadFromInheritedStdHandle.");
            bootstrap = ChildIpcBootstrap.ReadFromInheritedStdHandle();
            DebugLog($"Bootstrap OK — pipeName={bootstrap.PipeName}, protocolVersion={bootstrap.ProtocolVersion}.");
        }
        catch (Exception ex)
        {
            DebugLog("Bootstrap FAILED:\n" + ex);
            Console.Error.WriteLine($"Bootstrap failed: {ex.Message}");
            return 1;
        }

        var client = new IpcChildClient(ProcessType.Overlay, bootstrap);
        using var cts = new CancellationTokenSource();

        // Real-hardware fix (Đợt 9, đồng bộ với Vision): connect pipe NGAY, trước bất kỳ bước khởi
        // tạo nào khác — tránh chi phí cold-start (kể cả JIT/first-run overhead nhẹ của WinForms)
        // ăn vào ngân sách connect sau spawn của Service (Architecture/03 mục 4.1, ADR-147/148).
        DebugLog("Trước client.ConnectAsync (connect pipe sớm).");
        client.ConnectAsync(cts.Token).GetAwaiter().GetResult();
        DebugLog("client.ConnectAsync xong — đã connect + handshake thành công.");

        // Handle Windows message loop tạo ngay (không cần Show()) để Invoke() từ Thread IPC hoạt
        // động được ngay cả trước khi Application.Run() bắt đầu bơm message.
        DebugLog("Trước new OverlayCoordinator.");
        var coordinator = new OverlayCoordinator(request => SendForceClose(client, request), update => SendIconPosition(client, update));
        _ = coordinator.Handle;
        DebugLog("Coordinator OK — trước Task.Run RunIpcAsync + Application.Run.");

        Task ipcTask = Task.Run(() => RunIpcAsync(client, coordinator, cts.Token), CancellationToken.None);

        Application.Run();

        DebugLog("Application.Run() trả về (app đã đóng).");
        cts.Cancel();
        try
        {
            ipcTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }

        return 0;
    }

    private static async Task RunIpcAsync(IpcChildClient client, OverlayCoordinator coordinator, CancellationToken cancellationToken)
    {
        DebugLog("RunIpcAsync bắt đầu — trước client.RunForeverAsync.");
        try
        {
            await client.RunForeverAsync(
                onBusinessMessage: (message, _) => HandleBusinessMessageAsync(message, coordinator),
                cancellationToken,
                onDisconnected: coordinator.ApplyDisconnected).ConfigureAwait(false);
            DebugLog("RunForeverAsync trả về bình thường.");
        }
        catch (GracefulStopRequestedException)
        {
            DebugLog("GracefulStopRequestedException.");
        }
        catch (Exception ex)
        {
            DebugLog("RunForeverAsync ném exception KHÔNG lường trước:\n" + ex);
            throw;
        }
        finally
        {
            coordinator.Invoke(Application.Exit);
        }
    }

    private static Task HandleBusinessMessageAsync(IpcPayload message, OverlayCoordinator coordinator)
    {
        switch (message.BodyCase)
        {
            case IpcPayload.BodyOneofCase.OverlayRects:
                coordinator.ApplyOverlayList(message.OverlayRects);
                break;
            case IpcPayload.BodyOneofCase.MonitoringStatus:
                coordinator.ApplyMonitoringStatus(message.MonitoringStatus);
                break;
            case IpcPayload.BodyOneofCase.IconLayoutSync:
                coordinator.ApplyIconLayoutSync(message.IconLayoutSync);
                break;
            case IpcPayload.BodyOneofCase.ShowToast:
                // BE-061b: UI Toast thật là Đợt 6 (Architecture/09, chưa viết) — Đợt 0/1 chỉ đảm bảo nhận không throw.
                ShowToastCommand toast = message.ShowToast;
                Console.WriteLine($"[ShowToastCommand] severity={toast.Severity} reason={toast.ReasonCode} text={toast.Text}");
                break;
        }

        return Task.CompletedTask;
    }

    private static void SendForceClose(IpcChildClient client, ForceCloseRequest request)
    {
        IpcPayload payload = client.NewEnvelope();
        payload.ForceClose = request;
        client.EnqueueOutbound(payload);
    }

    private static void SendIconPosition(IpcChildClient client, IconPositionUpdate update)
    {
        IpcPayload payload = client.NewEnvelope();
        payload.IconPositionUpdate = update;
        client.EnqueueOutbound(payload);
    }
}

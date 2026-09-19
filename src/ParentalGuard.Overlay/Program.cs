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

    [STAThread]
    private static int Main()
    {
        SetProcessDpiAwarenessContext(new IntPtr(_dpiAwarenessContextPerMonitorAwareV2));

        ChildIpcBootstrap bootstrap;
        try
        {
            bootstrap = ChildIpcBootstrap.ReadFromInheritedStdHandle();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or EndOfStreamException)
        {
            Console.Error.WriteLine($"Bootstrap failed: {ex.Message}");
            return 1;
        }

        var client = new IpcChildClient(ProcessType.Overlay, bootstrap);
        using var cts = new CancellationTokenSource();

        // Handle Windows message loop tạo ngay (không cần Show()) để Invoke() từ Thread IPC hoạt
        // động được ngay cả trước khi Application.Run() bắt đầu bơm message.
        var coordinator = new OverlayCoordinator(request => SendForceClose(client, request), update => SendIconPosition(client, update));
        _ = coordinator.Handle;

        Task ipcTask = Task.Run(() => RunIpcAsync(client, coordinator, cts.Token), CancellationToken.None);

        Application.Run();

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
        try
        {
            await client.RunForeverAsync(
                onBusinessMessage: (message, _) => HandleBusinessMessageAsync(message, coordinator),
                cancellationToken,
                onDisconnected: coordinator.ApplyDisconnected).ConfigureAwait(false);
        }
        catch (GracefulStopRequestedException)
        {
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

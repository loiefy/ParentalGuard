using System.Threading;
using System.Windows.Forms;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Uninstaller.Forms;

namespace ParentalGuard.Uninstaller;

// ANTI-020 (Architecture/09-anti-tamper-architecture.md mục 5) — manifest requireAdministrator
// (app.manifest) là lớp gate #1 (OS, trước khi bất kỳ dòng code nào chạy); modal mật khẩu
// ParentalGuard dưới đây là lớp gate #2 (do app kiểm soát).
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var connection = new UninstallPipeClient();
        var cleanup = new LocalCleanup();
        var flow = new UninstallFlow(connection, cleanup);
        var appContext = new ApplicationContext();

        UninstallFlowResult? result = null;
        _ = RunFlowAsync();

        Application.Run(appContext);

        ShowFinalMessage(result, cleanup.ServiceExitTimedOut);

        async Task RunFlowAsync()
        {
            try
            {
                result = await flow.RunAsync(PromptPasswordAsync, ConfirmProceedAsync, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(true);
                appContext.ExitThread();
            }
        }
    }

    private static Task<byte[]?> PromptPasswordAsync(AuthVerifyResponse? previousAttempt, CancellationToken cancellationToken)
    {
        using var form = new PasswordPromptForm(previousAttempt);
        return Task.FromResult(form.ShowDialog() == DialogResult.OK ? form.GetPasswordUtf8Bytes() : null);
    }

    private static Task<bool?> ConfirmProceedAsync(CancellationToken cancellationToken)
    {
        using var form = new ConfirmUninstallForm();
        return Task.FromResult(form.ShowDialog() == DialogResult.OK ? (bool?)form.KeepAuditLog : null);
    }

    private static void ShowFinalMessage(UninstallFlowResult? result, bool serviceExitTimedOut)
    {
        if (result is null || result.Outcome == UninstallFlowOutcome.CancelledByUser)
        {
            return; // Huỷ — không gỡ gì cả, không cần thông báo thêm (mục 5.6).
        }

        string message = result.Outcome switch
        {
            UninstallFlowOutcome.ServiceUnreachable =>
                "Không kết nối được ParentalGuard Service. Không có gì bị gỡ cài đặt.",
            UninstallFlowOutcome.ConnectionLost =>
                "Mất kết nối tới ParentalGuard Service giữa chừng. Không có gì bị gỡ cài đặt — vui lòng thử lại.",
            UninstallFlowOutcome.ExecutionFailed =>
                $"Gỡ cài đặt không thành công ({result.ServiceResult}). Vui lòng thử lại hoặc liên hệ hỗ trợ.",
            UninstallFlowOutcome.Completed when serviceExitTimedOut =>
                "Gỡ cài đặt có thể chưa hoàn tất — vui lòng khởi động lại máy và thử lại.",
            UninstallFlowOutcome.Completed =>
                "Đã gỡ cài đặt ParentalGuard. Khởi động lại máy để dọn dẹp hoàn toàn."
                + (result.AuditLogCopyPath is null ? string.Empty : $"\n\nNhật ký hoạt động đã lưu tại: {result.AuditLogCopyPath}"),
            _ => "Đã hoàn tất.",
        };

        MessageBox.Show(message, "Gỡ cài đặt ParentalGuard", MessageBoxButtons.OK,
            result.Outcome == UninstallFlowOutcome.Completed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace ParentalGuard.Ipc.Tamper;

public enum PeerRecoveryOutcome
{
    Started,
    RecreatedThenStarted,
    Failed,
}

public sealed record PeerRecoveryResult(PeerRecoveryOutcome Outcome, string ActionTaken);

/// <summary>
/// Trình tự khôi phục peer khi 1 bên (<c>Service</c>/<c>Watchdog</c>) phát hiện bên kia mất tích
/// (Architecture/09-anti-tamper-architecture.md mục 3.4, ADR-88) — đối xứng cho cả 2 chiều, dùng
/// CHUNG code (bên gọi truyền đúng tên/đường dẫn của PEER, không phải của chính mình).
/// </summary>
public static class PeerServiceRecovery
{
    private static readonly TimeSpan _gracefulStopTimeout = TimeSpan.FromSeconds(5);

    public static async Task<PeerRecoveryResult> RecoverAsync(
        string peerServiceName,
        string peerDisplayName,
        string peerExecutableFileName,
        string[] startArgs,
        CancellationToken cancellationToken)
    {
        TryStopGraceful(peerServiceName);
        ForceKillIfStillRunning(peerExecutableFileName);

        if (TryStart(peerServiceName, startArgs, out bool serviceMissing))
        {
            return new PeerRecoveryResult(PeerRecoveryOutcome.Started, "scm_start");
        }

        if (!serviceMissing)
        {
            return new PeerRecoveryResult(PeerRecoveryOutcome.Failed, "start_failed");
        }

        // ANTI-010 điểm 3: registry/service definition bị xoá hoàn toàn — tự đăng ký lại với tham
        // số tối thiểu hard-code (mục 3.4 bước 4), đường dẫn suy ra RUNTIME (không hard-code tuyệt đối).
        string binaryPath = ResolvePeerBinaryPath(peerExecutableFileName);
        ScResult createResult = await ScProcessRunner.RunAsync(
            BuildCreateArguments(peerServiceName, peerDisplayName, binaryPath), cancellationToken).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            return new PeerRecoveryResult(PeerRecoveryOutcome.Failed, $"sc_create_failed_exit_{createResult.ExitCode}");
        }

        if (TryStart(peerServiceName, startArgs, out _))
        {
            return new PeerRecoveryResult(PeerRecoveryOutcome.RecreatedThenStarted, "recreated_registration_then_start");
        }

        return new PeerRecoveryResult(PeerRecoveryOutcome.Failed, "start_failed_after_recreate");
    }

    /// <summary>Mục 3.4 bước 4: <c>&lt;thư mục cài đặt của chính process đang chạy&gt;\&lt;tên exe của peer&gt;</c> — 2 exe luôn cùng thư mục cài đặt.</summary>
    internal static string ResolvePeerBinaryPath(string peerExecutableFileName)
    {
        string? ownDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(ownDirectory))
        {
            throw new InvalidOperationException("Could not resolve own install directory from Environment.ProcessPath.");
        }

        return Path.Combine(ownDirectory, peerExecutableFileName);
    }

    internal static string BuildCreateArguments(string serviceName, string displayName, string binaryPath) =>
        $"create {serviceName} binPath= \"{binaryPath}\" start= auto obj= LocalSystem DisplayName= \"{displayName}\"";

    private static void TryStopGraceful(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                return;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, _gracefulStopTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException)
        {
            // Không tồn tại / đã Stopped / không phản hồi kịp trong 5s — bước force-kill kế tiếp xử lý nốt.
        }
    }

    private static void ForceKillIfStillRunning(string executableFileName)
    {
        string processName = Path.GetFileNameWithoutExtension(executableFileName);
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Trả về true nếu Start thành công (hoặc peer đã Running sẵn). <paramref name="serviceMissing"/>=true khi peer chưa từng đăng ký (mục 3.4 bước 4).</summary>
    private static bool TryStart(string serviceName, string[] startArgs, out bool serviceMissing)
    {
        serviceMissing = false;
        try
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status == ServiceControllerStatus.Running)
            {
                return true;
            }

            controller.Start(startArgs);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 1060 })
        {
            // ERROR_SERVICE_DOES_NOT_EXIST — registry key đã bị xoá hoàn toàn.
            serviceMissing = true;
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }
}

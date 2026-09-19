using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;
using ParentalGuard.Ipc.Tamper;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Configuration;
using ParentalGuard.Service.Ipc;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Tamper;

/// <summary>
/// Xử lý <c>UninstallExecuteRequest</c> (ANTI-020, Architecture/09-anti-tamper-architecture.md mục
/// 5.5, ADR-96) — phần việc <c>Service</c> (SYSTEM) đảm nhiệm: <c>%ProgramData%</c>, registry Service
/// keys, WFP, dừng <c>Watchdog</c> ĐÚNG THỨ TỰ trước khi tự thoát (ADR-95). Phần <c>%ProgramFiles%</c>
/// + tự xoá do chính <c>ParentalGuard.Uninstaller.exe</c> đảm nhiệm sau khi nhận SUCCESS (ADR-98).
/// </summary>
public sealed class UninstallCoordinator(
    AuthCoordinator authCoordinator,
    AuditLogWriter auditLog,
    WfpVisionBlocker wfpBlocker,
    WatchdogSessionServer watchdogSessionServer,
    ILogger logger)
{
    private readonly IpcMessageIdGenerator _messageIds = new();

    // Interlocked.Exchange không có overload bool — dùng int, đọc/ghi qua ConsumeShutdownRequested/ExecuteAsync.
    private int _shutdownRequestedInt;

    public async Task<IpcPayload> HandleAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        UninstallExecuteRequest req = request.UninstallExecuteReq;
        IpcPayload response = NewResponse(request);

        byte[] tokenBytes = CredentialBytes.UnsafeGetBuffer(req.ActionToken);
        bool tokenValid;
        try
        {
            tokenValid = await authCoordinator.TryConsumeActionTokenAsync(tokenBytes, "uninstall", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CredentialBytes.Zero(tokenBytes);
        }

        if (!tokenValid)
        {
            response.UninstallExecuteResp = new UninstallExecuteResponse { Result = UninstallResult.InvalidToken };
            return response;
        }

        response.UninstallExecuteResp = await ExecuteAsync(req.KeepAuditLog, cancellationToken).ConfigureAwait(false);
        return response;
    }

    /// <summary>Gọi ngay sau khi response đã ghi xong pipe (đúng nguyên tắc "đã gửi" — 08 mục 5.5 điểm 2, tái áp dụng ở đây cho bước 9).</summary>
    public bool ConsumeShutdownRequested() => Interlocked.Exchange(ref _shutdownRequestedInt, 0) == 1;

    private async Task<UninstallExecuteResponse> ExecuteAsync(bool keepAuditLog, CancellationToken cancellationToken)
    {
        bool anyStepFailed = false;

        // Chặn WatchdogSessionServer tự "khôi phục" Watchdog khi pipe vỡ do CHÍNH bước 4 dưới đây gây
        // ra (không phải tấn công) — PHẢI đặt trước bất kỳ bước nào có thể làm Watchdog ngắt kết nối.
        watchdogSessionServer.SuppressRecovery();

        await auditLog.AppendAsync("UninstallInitiated", new { keep_audit_log = keepAuditLog }, CancellationToken.None).ConfigureAwait(false);

        string? auditLogCopyPath = null;
        if (keepAuditLog)
        {
            auditLogCopyPath = DesktopAuditLogExporter.TryExport(InstallPaths.AuditLogPath);
            if (auditLogCopyPath is null)
            {
                anyStepFailed = true;
                logger.LogWarning("Uninstall: audit.log copy to Desktop failed — keeping original in place (step 6 skip).");
            }
        }

        bool wfpRemoved = TryStep("RemoveWfpFilters", () => wfpBlocker.Remove());
        anyStepFailed |= !wfpRemoved;
        if (wfpRemoved)
        {
            // Architecture/04-data-architecture.md mục 5.1 (Đợt 4 amendment) — event_type riêng cho bước 3.
            await auditLog.AppendAsync("WFPFiltersRemoved", new { }, CancellationToken.None).ConfigureAwait(false);
        }

        anyStepFailed |= !TryStopWatchdog();
        anyStepFailed |= !await TryDeleteServiceRegistrationAsync(InstallPaths.WatchdogServiceName, cancellationToken).ConfigureAwait(false);
        anyStepFailed |= !TryStep("DeleteProgramData", () => DeleteProgramData(keepAuditLog: keepAuditLog && auditLogCopyPath is not null));
        anyStepFailed |= !await TryDeleteServiceRegistrationAsync(InstallPaths.ServiceName, cancellationToken).ConfigureAwait(false);

        _shutdownRequestedInt = 1;
        if (anyStepFailed)
        {
            // Chỉ 4 event_type ANTI-020 mới đã chốt ở Architecture/04 mục 5.1 (Đợt 4 amendment) —
            // không phát minh thêm event "success" riêng; kết quả SUCCESS đã truyền qua response IPC.
            await auditLog.AppendAsync("UninstallPartialFailure", new { }, CancellationToken.None).ConfigureAwait(false);
        }

        return new UninstallExecuteResponse
        {
            Result = anyStepFailed ? UninstallResult.PartialFailure : UninstallResult.Success,
            AuditLogCopyPath = auditLogCopyPath ?? string.Empty,
        };
    }

    private bool TryStep(string stepName, Action step)
    {
        try
        {
            step();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Uninstall step '{Step}' failed — continuing best-effort (mục 5.5, no rollback).", stepName);
            return false;
        }
    }

    /// <summary>Mục 5.5 bước 4 (ADR-95) — PHẢI hoàn tất (Watchdog Stopped hẳn) trước khi Service tự thoát ở bước 9.</summary>
    private static bool TryStopWatchdog()
    {
        try
        {
            using var controller = new System.ServiceProcess.ServiceController(InstallPaths.WatchdogServiceName);
            if (controller.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
            {
                controller.Stop();
                controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }

            return true;
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            ForceKillWatchdogProcess();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Không tồn tại — coi như đã "dừng" (idempotent, mục 5.5 preamble).
            return true;
        }
    }

    private static void ForceKillWatchdogProcess()
    {
        string processName = Path.GetFileNameWithoutExtension(InstallPaths.WatchdogExecutablePath);
        foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(processName))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static async Task<bool> TryDeleteServiceRegistrationAsync(string serviceName, CancellationToken cancellationToken)
    {
        ScResult result = await ScProcessRunner.RunAsync($"delete {serviceName}", cancellationToken).ConfigureAwait(false);
        // exit code 1060 = ERROR_SERVICE_DOES_NOT_EXIST — idempotent-OK (đã xoá từ trước).
        return result.Succeeded || result.ExitCode == 1060;
    }

    /// <summary>Mục 5.5 bước 6 — config.db(+wal/shm), auth.dat, audit.log (TRỪ khi copy Desktop thất bại).</summary>
    private static void DeleteProgramData(bool keepAuditLog)
    {
        DeleteIfExists(InstallPaths.ConfigDbPath);
        DeleteIfExists(InstallPaths.ConfigDbPath + "-wal");
        DeleteIfExists(InstallPaths.ConfigDbPath + "-shm");
        DeleteIfExists(InstallPaths.AuthDatPath);
        if (!keepAuditLog)
        {
            DeleteIfExists(InstallPaths.AuditLogPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);
}

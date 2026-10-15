using ParentalGuard.Uninstaller.Interop;

namespace ParentalGuard.Uninstaller;

/// <summary>
/// Mục 5.5 bước 10-13 (Architecture/09-anti-tamper-architecture.md) — phần <c>Uninstaller.exe</c>
/// (Administrator) đảm nhiệm SAU KHI đã nhận <c>UninstallExecuteResponse{SUCCESS}</c> từ Service
/// (bất biến an toàn ADR-98, thực thi ở <see cref="UninstallFlow"/> — lớp này KHÔNG tự kiểm tra lại
/// điều kiện đó, tin tưởng caller).
/// </summary>
public sealed class LocalCleanup : ILocalCleanup
{
    private static readonly TimeSpan _waitForServiceExitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Set true nếu bước 10 timeout — <see cref="Program"/> dùng để hiển thị đúng cảnh báo mục 5.5.</summary>
    public bool ServiceExitTimedOut { get; private set; }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (!await WaitForServiceExitAsync(cancellationToken).ConfigureAwait(false))
        {
            ServiceExitTimedOut = true;
            return; // mục 5.5 bước 10: timeout — không tiếp tục 11-13, tránh xoá file Service.exe đang khoá.
        }

        StopRemainingProcesses();
        DeleteProgramFilesExecutables();
        DeleteModelFiles();
        DeleteUninstallRegistryKey();
        SelfDelete();
    }

    /// <summary>Dashboard, icon khay (và Vision/Overlay nếu chưa kịp thoát theo Service) còn giữ file → dừng trước khi xoá thư mục.</summary>
    private static void StopRemainingProcesses()
    {
        foreach (string name in new[] { "ParentalGuard.UI", "ParentalGuard.Tray", "ParentalGuard.Vision", "ParentalGuard.Overlay" })
        {
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
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
    }

    private static async Task<bool> WaitForServiceExitAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_waitForServiceExitTimeout);
        try
        {
            while (!timeoutCts.IsCancellationRequested)
            {
                if (System.Diagnostics.Process.GetProcessesByName("ParentalGuard.Service").Length == 0)
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), timeoutCts.Token).ConfigureAwait(false);
            }

            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static void DeleteProgramFilesExecutables()
    {
        foreach (string exeName in UninstallerPaths.ExecutablesToDelete)
        {
            DeleteIfExistsBestEffort(Path.Combine(UninstallerPaths.ProgramFilesDir, exeName));
        }
    }

    private static void DeleteModelFiles()
    {
        if (!Directory.Exists(UninstallerPaths.ModelsDir))
        {
            return;
        }

        foreach (string onnxFile in Directory.EnumerateFiles(UninstallerPaths.ModelsDir, "*.onnx"))
        {
            DeleteIfExistsBestEffort(onnxFile);
        }
    }

    private static void DeleteUninstallRegistryKey()
    {
        try
        {
            RegistryDeleteInterop.RegDeleteTree(RegistryDeleteInterop.HKeyLocalMachine, UninstallerPaths.UninstallRegistryKeyPath);

            // FE-023a: icon khay tự chạy lúc đăng nhập — gỡ cài đặt thì bỏ luôn mục khởi động này.
            RegistryDeleteInterop.RegDeleteKeyValue(
                RegistryDeleteInterop.HKeyLocalMachine, UninstallerPaths.RunRegistryKeyPath, UninstallerPaths.TrayRunValueName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Mục 5.5 bước 13 — xoá chính <c>Uninstaller.exe</c> (đang chạy) và TOÀN BỘ thư mục cài đặt.
    /// Bug 2026-10-12 (chủ dự án báo): bản cũ hẹn Windows xoá lúc KHỞI ĐỘNG LẠI (MoveFileEx DelayUntilReboot) → (1) cài bản mới trước
    /// khi khởi động lại thì lần khởi động sau Windows xoá mất <c>Uninstaller.exe</c> của bản MỚI; (2) chỉ 5 file .exe bị xoá, hàng trăm
    /// DLL ở lại vĩnh viễn (thư mục không rỗng thì lệnh hẹn xoá thư mục thất bại). Nay: 1 tiến trình <c>cmd.exe</c> tách rời chờ vài giây
    /// cho Uninstaller thoát rồi <c>rmdir /s /q</c> thư mục cố định. Chỉ khi không tạo được tiến trình đó mới quay về cách hẹn khởi động lại.
    /// </summary>
    private static void SelfDelete()
    {
        string dir = UninstallerPaths.ProgramFilesDir;
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/d /c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{dir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.SystemDirectory, // không đứng trong thư mục sắp xoá
            };
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(startInfo);
            if (process is not null)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        string? selfPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(selfPath))
        {
            MoveFileExInterop.MoveFileEx(selfPath, null, MoveFileExInterop.MoveFileDelayUntilReboot);
        }

        MoveFileExInterop.MoveFileEx(dir, null, MoveFileExInterop.MoveFileDelayUntilReboot);
    }

    private static void DeleteIfExistsBestEffort(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort (mục 5.5 preamble) — 1 file bị khoá bởi tiến trình khác không chặn các bước còn lại.
        }
    }
}

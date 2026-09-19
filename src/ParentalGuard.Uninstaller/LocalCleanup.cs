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

        DeleteProgramFilesExecutables();
        DeleteModelFiles();
        DeleteUninstallRegistryKey();
        SelfDelete();
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Mục 5.5 bước 13 — không thể xoá ngay file thực thi đang chạy chính nó lẫn thư mục cha còn khoá.</summary>
    private static void SelfDelete()
    {
        string? selfPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(selfPath))
        {
            MoveFileExInterop.MoveFileEx(selfPath, null, MoveFileExInterop.MoveFileDelayUntilReboot);
        }

        MoveFileExInterop.MoveFileEx(UninstallerPaths.ProgramFilesDir, null, MoveFileExInterop.MoveFileDelayUntilReboot);
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

using System.Diagnostics;

namespace ParentalGuard.Ipc.Tamper;

/// <summary>
/// Spawn <c>sc.exe</c> (Architecture/09-anti-tamper-architecture.md ADR-88: "nhất quán thận trọng
/// dependency/native API... ưu tiên công cụ built-in đã kiểm chứng hàng thập kỷ thay vì tự quản lý
/// đúng struct/marshalling phức tạp của <c>CreateServiceW</c> qua P/Invoke"). Dùng cho cả SCM Recovery
/// Options (mục 3.5) lẫn tái đăng ký service khi registry bị xoá (mục 3.4 bước 4).
/// </summary>
public static class ScProcessRunner
{
    private static readonly string _scExePath = Path.Combine(Environment.SystemDirectory, "sc.exe");

    public static async Task<ScResult> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_scExePath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ScResult(process.ExitCode, stdout, stderr);
    }
}

public sealed record ScResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

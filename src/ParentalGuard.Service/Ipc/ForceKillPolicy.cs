namespace ParentalGuard.Service.Ipc;

/// <summary>Thông tin tối thiểu về 1 tiến trình để quyết định có được buộc đóng hay không (tách khỏi <see cref="System.Diagnostics.Process"/> để test).</summary>
public sealed record ProcessFacts(int ProcessId, int SessionId, string ExecutableName);

public enum ForceKillDecision
{
    Allowed,
    NoRecentManualClose,
    ProcessGone,
    SessionZero,
    ProcessNameMismatch,
    ProtectedProcess,
}

/// <summary>
/// `BE-034d` (2026-10-07): điều kiện an toàn trước khi <c>Service</c> (SYSTEM) kết thúc tiến trình sở hữu cửa sổ vi phạm sau khi
/// "Tắt nội dung" không đóng được cửa sổ. Hàm thuần — không đụng OS.
/// </summary>
public static class ForceKillPolicy
{
    /// <summary>Yêu cầu buộc đóng chỉ hợp lệ trong khoảng này sau lần bấm "Tắt nội dung" tương ứng (Overlay gửi sau 3 giây).</summary>
    public static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(30);

    /// <summary>Không bao giờ buộc đóng — tiến trình lõi Windows/vỏ giao diện (đóng là treo/mất cả desktop) và chính ParentalGuard.</summary>
    public static readonly IReadOnlySet<string> ProtectedExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "dwm.exe", "csrss.exe", "winlogon.exe", "wininit.exe", "services.exe", "lsass.exe", "smss.exe",
        "svchost.exe", "sihost.exe", "fontdrvhost.exe", "ctfmon.exe", "taskhostw.exe", "RuntimeBroker.exe",
        "ApplicationFrameHost.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe", "SearchHost.exe",
        "TextInputHost.exe", "LogonUI.exe", "System", "Registry", "conhost.exe", "WindowsTerminal.exe",
    };

    /// <param name="recordedProcessName">Tên tiến trình Vision ghi nhận cho cửa sổ lúc che (null nếu không có lần bấm "Tắt nội dung" gần đây).</param>
    /// <param name="closedAgo">Thời gian từ lần bấm "Tắt nội dung" tới lúc nhận yêu cầu buộc đóng.</param>
    /// <param name="process">Tiến trình theo PID Overlay gửi lên — null nếu đã thoát.</param>
    public static ForceKillDecision Decide(string? recordedProcessName, TimeSpan closedAgo, ProcessFacts? process)
    {
        if (recordedProcessName is null || closedAgo < TimeSpan.Zero || closedAgo > RequestWindow)
        {
            return ForceKillDecision.NoRecentManualClose;
        }

        if (process is null)
        {
            return ForceKillDecision.ProcessGone;
        }

        if (process.SessionId == 0)
        {
            return ForceKillDecision.SessionZero;
        }

        if (IsProtected(process.ExecutableName) || process.ExecutableName.StartsWith("ParentalGuard", StringComparison.OrdinalIgnoreCase))
        {
            return ForceKillDecision.ProtectedProcess;
        }

        // PID Overlay gửi phải đúng là tiến trình Vision đã thấy sở hữu cửa sổ vi phạm (chống dùng PID tuỳ ý để giết tiến trình khác).
        return string.Equals(Normalize(recordedProcessName), Normalize(process.ExecutableName), StringComparison.OrdinalIgnoreCase)
            ? ForceKillDecision.Allowed
            : ForceKillDecision.ProcessNameMismatch;
    }

    private static bool IsProtected(string executableName) => ProtectedExecutables.Contains(Normalize(executableName)) || ProtectedExecutables.Contains(executableName);

    private static string Normalize(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
}

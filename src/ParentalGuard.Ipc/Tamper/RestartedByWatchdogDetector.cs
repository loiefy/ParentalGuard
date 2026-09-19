namespace ParentalGuard.Ipc.Tamper;

/// <summary>
/// Cờ startup arg dùng chung 2 chiều (Architecture/09-anti-tamper-architecture.md mục 3.4 bước 3) —
/// hàm thuần, dùng bởi cả <c>Service</c> và <c>Watchdog</c> Worker để phát hiện chính mình vừa được
/// bên kia khởi động lại (mục 3.6).
/// </summary>
public static class RestartedByWatchdogDetector
{
    public const string Flag = "--restarted-by-watchdog";

    public static bool WasRestartedByWatchdog(IEnumerable<string> args) => args.Contains(Flag);
}

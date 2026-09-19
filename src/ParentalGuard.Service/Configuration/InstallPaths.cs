namespace ParentalGuard.Service.Configuration;

/// <summary>
/// Layout file/thư mục trên đĩa (Architecture/04-data-architecture.md mục 2,
/// Architecture/06-security-architecture.md mục 4). Đợt 0 chưa có installer (Đợt 9) —
/// <c>Service</c> tự tạo thư mục/ACL tương đương ở các đường dẫn cài đặt chính thức này
/// (ADR-37), độc lập với việc binary đang chạy hiện tại nằm ở đâu.
/// </summary>
public static class InstallPaths
{
    public static readonly string ProgramFilesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ParentalGuard");

    public static readonly string ProgramDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ParentalGuard");

    public static readonly string ModelsDir = Path.Combine(ProgramFilesDir, "models");

    public static readonly string VisionExecutablePath = Path.Combine(ProgramFilesDir, "ParentalGuard.Vision.exe");
    public static readonly string OverlayExecutablePath = Path.Combine(ProgramFilesDir, "ParentalGuard.Overlay.exe");
    public static readonly string UiExecutablePath = Path.Combine(ProgramFilesDir, "ParentalGuard.UI.exe");
    public static readonly string WatchdogExecutablePath = Path.Combine(ProgramFilesDir, "ParentalGuard.Watchdog.exe");
    public static readonly string UninstallerExecutablePath = Path.Combine(ProgramFilesDir, "ParentalGuard.Uninstaller.exe");

    public static readonly string ConfigDbPath = Path.Combine(ProgramDataDir, "config.db");
    public static readonly string AuthDatPath = Path.Combine(ProgramDataDir, "auth.dat");
    public static readonly string AuditLogPath = Path.Combine(ProgramDataDir, "audit.log");

    // Đợt 4 (ANTI-010/011/031) — không có "\" mở đầu: RegOpenKeyEx nhận đường dẫn tương đối HKLM.
    public const string ServiceRegistryKeyPath = @"SYSTEM\CurrentControlSet\Services\ParentalGuardService";
    public const string WatchdogRegistryKeyPath = @"SYSTEM\CurrentControlSet\Services\ParentalGuardWatchdog";

    public const string ServiceName = "ParentalGuardService";
    public const string WatchdogServiceName = "ParentalGuardWatchdog";
    public const string ServiceDisplayName = "ParentalGuard Service";
    public const string WatchdogDisplayName = "ParentalGuard Watchdog";
}

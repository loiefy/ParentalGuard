namespace ParentalGuard.Watchdog;

/// <summary>
/// Hằng số đường dẫn/tên cần cho <c>Watchdog</c> — CỐ Ý KHÔNG tham chiếu
/// <c>ParentalGuard.Service.Configuration.InstallPaths</c> (trùng vài hằng số) để không kéo theo
/// toàn bộ dependency của project <c>Service</c> (SQLite/Argon2/DPAPI) vào <c>Watchdog</c>, đúng
/// nguyên tắc tối giản ADR-86 (Architecture/09-anti-tamper-architecture.md mục 3.1).
/// </summary>
internal static class WatchdogPaths
{
    public static readonly string ProgramFilesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ParentalGuard");

    public static readonly string ServiceExecutablePath = Path.Combine(ProgramFilesDir, "ParentalGuard.Service.exe");

    public const string ServiceName = "ParentalGuardService";
    public const string ServiceDisplayName = "ParentalGuard Service";
    public const string WatchdogServiceName = "ParentalGuardWatchdog";

    public const string ServiceRegistryKeyPath = @"SYSTEM\CurrentControlSet\Services\ParentalGuardService";
    public const string WatchdogRegistryKeyPath = @"SYSTEM\CurrentControlSet\Services\ParentalGuardWatchdog";
}

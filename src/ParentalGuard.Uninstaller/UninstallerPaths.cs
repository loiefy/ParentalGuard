namespace ParentalGuard.Uninstaller;

/// <summary>
/// Hằng số đường dẫn/tên cho <c>Uninstaller</c> — CỐ Ý không tham chiếu project <c>Service</c> (tránh
/// kéo theo SQLite/Argon2/DPAPI chỉ để lấy vài hằng số đường dẫn, cùng tinh thần <c>WatchdogPaths</c>).
/// </summary>
internal static class UninstallerPaths
{
    public static readonly string ProgramFilesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ParentalGuard");

    public static readonly string ModelsDir = Path.Combine(ProgramFilesDir, "models");

    public static readonly IReadOnlyList<string> ExecutablesToDelete =
    [
        "ParentalGuard.Service.exe",
        "ParentalGuard.Vision.exe",
        "ParentalGuard.Overlay.exe",
        "ParentalGuard.UI.exe",
        "ParentalGuard.Tray.exe",
        "ParentalGuard.Watchdog.exe",
    ];

    public const string ServiceName = "ParentalGuardService";

    /// <summary>`FE-023a`: trình cài đặt ghi giá trị này để icon khay tự chạy lúc đăng nhập.</summary>
    public const string RunRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public const string TrayRunValueName = "ParentalGuardTray";

    public const string UninstallRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard";
}

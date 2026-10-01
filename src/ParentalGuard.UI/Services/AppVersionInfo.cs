using System.Reflection;

namespace ParentalGuard.UI.Services;

/// <summary>
/// FE-007 (2026-10-01): phiên bản hiển thị cho người dùng — lấy từ cấu hình build (<c>VersionPrefix</c>/<c>Version</c>
/// trong <c>src/Directory.Build.props</c> hoặc tham số <c>-p:Version=</c> lúc build), bản Debug ghi kèm "(Debug)".
/// </summary>
public static class AppVersionInfo
{
    public static string DisplayVersion { get; } = Compute();

    private static string Compute()
    {
        Assembly assembly = typeof(AppVersionInfo).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
#if DEBUG
        return version + " (Debug)";
#else
        return version;
#endif
    }
}

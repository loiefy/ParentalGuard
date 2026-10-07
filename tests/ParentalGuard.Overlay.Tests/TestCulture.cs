using System.Globalization;
using System.Runtime.CompilerServices;

namespace ParentalGuard.Overlay.Tests;

/// <summary>
/// `FE-063a` (2026-10-07): app luôn khởi động bằng tiếng Việt (không theo ngôn ngữ Windows) — test cũng chạy bằng tiếng Việt
/// để kết quả không phụ thuộc ngôn ngữ của máy chạy test, kể cả khi đã có bản dịch en/fr/es/pt/zh-Hans.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseVietnamese()
    {
        CultureInfo vi = CultureInfo.GetCultureInfo("vi");
        CultureInfo.DefaultThreadCurrentUICulture = vi;
        CultureInfo.CurrentUICulture = vi;
    }
}

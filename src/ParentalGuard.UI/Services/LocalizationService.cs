using System.Globalization;
using System.Resources;

namespace ParentalGuard.UI.Services;

/// <summary>
/// Architecture/10-ui-architecture.md mục 7/ADR-124 — wrapper mỏng quanh <see cref="ResourceManager"/>,
/// tái dùng đúng pattern <c>ParentalGuard.Overlay.Resources.OverlayStrings</c> (không dùng
/// <c>.resw</c>/<c>ResourceLoader</c> — <c>UI</c> unpackaged, không có package identity cho PRI
/// pipeline). <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> tự chọn resource file
/// phù hợp — sẵn sàng thêm <c>UiStrings.&lt;culture&gt;.resx</c> ở Phase 2 (`FE-063`) không cần sửa code.
/// </summary>
public static class LocalizationService
{
    private static readonly ResourceManager _resourceManager = new("ParentalGuard.UI.Resources.UiStrings", typeof(LocalizationService).Assembly);

    /// <summary>
    /// `FE-063a` (sửa 2026-10-08): ngôn ngữ hiển thị giữ TẠI ĐÂY, không dựa vào <see cref="CultureInfo.CurrentUICulture"/> — giá trị
    /// đó đi theo từng luồng async (ExecutionContext), đặt trong 1 hàm async chỉ có hiệu lực trong hàm đó: bản trước chỉ menu
    /// (dựng trong hàm đó) đổi ngôn ngữ, các trang dựng sau vẫn tiếng Việt. Mặc định tiếng Việt.
    /// </summary>
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("vi");

    public static CultureInfo Culture => _culture;

    public static void SetLanguage(string code)
    {
        try
        {
            _culture = CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            _culture = CultureInfo.GetCultureInfo("vi");
        }

        CultureInfo.DefaultThreadCurrentUICulture = _culture;
    }

    public static string Get(string key) => _resourceManager.GetString(key, _culture) ?? key;

    public static string GetFormatted(string key, params object[] args) => string.Format(Get(key), args);

    /// <summary>Mã ngôn ngữ của bộ resource đang thực sự được dùng (khoá <c>LanguageCode</c> trong từng file `.resx`, `FE-064`).</summary>
    public static string CurrentLanguageCode => Get("LanguageCode");
}

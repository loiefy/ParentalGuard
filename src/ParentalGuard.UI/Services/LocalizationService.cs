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

    public static string Get(string key) => _resourceManager.GetString(key) ?? key;

    public static string GetFormatted(string key, params object[] args) => string.Format(Get(key), args);
}

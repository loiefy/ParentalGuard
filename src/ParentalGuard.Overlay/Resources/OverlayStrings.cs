using System.Resources;

namespace ParentalGuard.Overlay.Resources;

/// <summary>
/// `FE-022`/`FE-060`: tooltip icon trạng thái dựng từ resource ngôn ngữ (`OverlayStrings.resx`),
/// không hardcode chuỗi — hạ tầng sẵn sàng đa ngôn ngữ (thêm <c>OverlayStrings.&lt;culture&gt;.resx</c>
/// khi cần, <see cref="ResourceManager"/> tự chọn theo <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>).
/// </summary>
internal static class OverlayStrings
{
    private static readonly ResourceManager _resourceManager = new("ParentalGuard.Overlay.Resources.OverlayStrings", typeof(OverlayStrings).Assembly);

    internal static string IconTooltipActive => Get("IconTooltipActive");

    internal static string IconTooltipError => Get("IconTooltipError");

    internal static string IconTooltipPaused(string countdown) => string.Format(Get("IconTooltipPausedCountdownFormat"), countdown);

    /// <summary>`FE-016g`/`FE-016h`: đếm ngược trực quan bắt buộc (mm:ss — overlay thường 60s, gộp 30s).</summary>
    internal static string AutoTimeoutCountdown(int remainingSeconds) => string.Format(Get("AutoTimeoutCountdownFormat"), remainingSeconds / 60, remainingSeconds % 60);

    /// <summary>`FE-012`/ADR-110: thông điệp phụ huynh tuỳ biến, rỗng → câu mặc định cục bộ (`FE-062`).</summary>
    internal static string BlockedMessage(string? overrideText) => string.IsNullOrWhiteSpace(overrideText) ? Get("DefaultBlockedMessage") : overrideText;

    internal static string CloseButtonLabel => Get("CloseButtonLabel");

    /// <summary>`FE-016i`: tooltip nút bánh răng trên overlay.</summary>
    internal static string OpenDashboardTooltip => Get("OpenDashboardTooltip");

    private static System.Globalization.CultureInfo? _culture;

    /// <summary>`FE-063a`: ngôn ngữ do Service đẩy xuống (<c>LanguageUpdate</c>) — null = theo culture của luồng (mặc định).</summary>
    internal static void SetLanguage(System.Globalization.CultureInfo culture) => _culture = culture;

    private static string Get(string name) => _resourceManager.GetString(name, _culture) ?? name;
}

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

    private static string Get(string name) => _resourceManager.GetString(name) ?? name;
}

using System.Globalization;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.ViewModels;

/// <summary>1 lựa chọn ngôn ngữ ở `S4` — <see cref="IsAvailable"/>=false khi chưa có bản dịch (`FE-064`).</summary>
public sealed record LanguageOption(string Code, string DisplayName, bool IsAvailable);

/// <summary>
/// `FE-064` (Architecture/10 mục 6.8): danh sách ngôn ngữ hiển thị song ngữ — tên theo ngôn ngữ đang dùng + tên
/// tiếng Anh trong ngoặc; ngôn ngữ đang dùng là tiếng Anh thì chỉ hiện tiếng Anh. Thuần dữ liệu, test không cần WinUI.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>Thứ tự hiển thị — danh sách chủ dự án yêu cầu (Việt, Anh, Pháp, Tây Ban Nha, Bồ Đào Nha, Trung Quốc).</summary>
    public static readonly IReadOnlyList<string> Codes = ["vi", "en", "fr", "es", "pt", "zh-Hans"];

    /// <summary>Ngôn ngữ đã có file resource dịch đầy đủ — mở rộng khi bổ sung bản dịch (`FE-063`).</summary>
    public static readonly IReadOnlySet<string> AvailableCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "vi" };

    public static bool IsEnglish(string currentCode) => currentCode.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Ngôn ngữ / Language" hoặc chỉ "Language" khi đang dùng tiếng Anh.</summary>
    public static string BuildHeader(string currentCode, string localHeader, string englishHeader) =>
        IsEnglish(currentCode) ? englishHeader : $"{localHeader} / {englishHeader}";

    public static IReadOnlyList<LanguageOption> Build(
        string currentCode,
        Func<string, string> localName,
        string comingSoonLocal,
        string comingSoonEnglish)
    {
        bool english = IsEnglish(currentCode);
        var options = new List<LanguageOption>(Codes.Count);
        foreach (string code in Codes)
        {
            string englishName = CultureInfo.GetCultureInfo(code).EnglishName;
            string name = english ? englishName : $"{localName(code)} ({englishName})";
            bool available = AvailableCodes.Contains(code);
            if (!available)
            {
                name += english ? $" — {comingSoonEnglish}" : $" — {comingSoonLocal} / {comingSoonEnglish}";
            }

            options.Add(new LanguageOption(code, name, available));
        }

        return options;
    }

    /// <summary>Bản dùng resource hiện hành (khoá <c>LanguageName_*</c>).</summary>
    public static IReadOnlyList<LanguageOption> BuildFromResources() => Build(
        LocalizationService.CurrentLanguageCode,
        code => LocalizationService.Get("LanguageName_" + code.Replace('-', '_')),
        LocalizationService.Get("LanguageComingSoon"),
        LocalizationService.Get("LanguageComingSoonEnglish"));
}

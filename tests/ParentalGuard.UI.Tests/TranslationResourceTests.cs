using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>`FE-063a` (2026-10-07): 6 ngôn ngữ — mỗi bản dịch trả đúng mã ngôn ngữ và giữ nguyên các placeholder định dạng.</summary>
public sealed partial class TranslationResourceTests
{
    private static readonly ResourceManager _resources = new("ParentalGuard.UI.Resources.UiStrings", typeof(LocalizationService).Assembly);

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex PlaceholderRegex();

    public static TheoryData<string> Languages => [.. LanguageCatalog.Codes];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EachLanguage_HasOwnLanguageCode_AndMatchingPlaceholders(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);

        Assert.Equal(code, _resources.GetString("LanguageCode", culture));

        ResourceSet neutral = _resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
        foreach (DictionaryEntry entry in neutral)
        {
            string key = (string)entry.Key;
            string original = (string)entry.Value!;
            string translated = _resources.GetString(key, culture)!;
            Assert.True(
                Placeholders(original).SetEquals(Placeholders(translated)),
                $"{code}/{key}: placeholder khác bản gốc");
            _ = string.Format(CultureInfo.InvariantCulture, translated, 1, 2, 3); // không ném FormatException
        }
    }

    [Fact]
    public void AllSixLanguagesSelectable()
    {
        Assert.All(LanguageCatalog.Codes, code => Assert.Contains(code, LanguageCatalog.AvailableCodes));
    }

    private static HashSet<string> Placeholders(string value) => [.. PlaceholderRegex().Matches(value).Select(m => m.Groups[1].Value)];
}

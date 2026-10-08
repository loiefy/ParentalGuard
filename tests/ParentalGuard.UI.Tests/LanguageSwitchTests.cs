using System.Globalization;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Tests;

[CollectionDefinition(nameof(LanguageSwitchCollection), DisableParallelization = true)]
public sealed class LanguageSwitchCollection;

/// <summary>
/// Bug real-hardware 2026-10-08 (`FE-063a`): đổi ngôn ngữ chỉ đổi menu, các trang khác vẫn tiếng Việt — ngôn ngữ từng đặt qua
/// <see cref="CultureInfo.CurrentUICulture"/> bên trong 1 hàm async, giá trị này đi theo luồng async nên mất ở các sự kiện sau.
/// </summary>
[Collection(nameof(LanguageSwitchCollection))]
public sealed class LanguageSwitchTests
{
    [Fact]
    public async Task LanguageSetInsideAsyncFlow_AppliesEverywhereAfterwards()
    {
        try
        {
            await SwitchInsideAsyncMethod("en");

            // Ngoài luồng async đã đặt ngôn ngữ (vd timer, sự kiện Loaded của trang) — vẫn phải là tiếng Anh.
            string fromOtherFlow = await Task.Run(() => LocalizationService.Get("NavSettings"));
            Assert.Equal("Settings", LocalizationService.Get("NavSettings"));
            Assert.Equal("Settings", fromOtherFlow);
            Assert.Equal("en", LocalizationService.CurrentLanguageCode);
        }
        finally
        {
            LocalizationService.SetLanguage("vi");
        }

        Assert.Equal("Cài đặt", LocalizationService.Get("NavSettings"));
    }

    private static async Task SwitchInsideAsyncMethod(string code)
    {
        await Task.Yield();
        LocalizationService.SetLanguage(code);
    }
}

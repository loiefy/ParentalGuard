using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>`FE-012a` — danh sách ký tự cho phép/cấm trong thông điệp overlay tự cấu hình (`Specification/03-frontend-ui-spec.md`).</summary>
public sealed class OverlayMessageValidationTests
{
    [Theory]
    [InlineData("Nội dung nhạy cảm được phát hiện, hãy thoát nội dung để bảo vệ chính bạn.")] // câu mặc định (FE-012)
    [InlineData("ABC abc 123")]
    [InlineData("Cau hoi? Cam on! Ro rang: co (dung) \"vay\" 'nhe' - het.")]
    public void IsValid_AllowedCharactersOnly_ReturnsTrue(string text) => Assert.True(OverlayMessageValidation.IsValid(text));

    [Theory]
    [InlineData("Xin chao @everyone")]
    [InlineData("50% giam gia")]
    [InlineData("a & b")]
    [InlineData("a_b")]
    [InlineData("a/b")]
    [InlineData("<script>")]
    [InlineData("emoji 😀 test")]
    [InlineData("tab\tchar")]
    public void IsValid_ForbiddenCharacters_ReturnsFalse(string text) => Assert.False(OverlayMessageValidation.IsValid(text));

    [Fact]
    public void IsValid_ExceedsMaxLength_ReturnsFalse() => Assert.False(OverlayMessageValidation.IsValid(new string('a', 256)));

    [Fact]
    public void IsValid_AtMaxLength_ReturnsTrue() => Assert.True(OverlayMessageValidation.IsValid(new string('a', 255)));

    [Fact]
    public void IsValid_VietnameseDiacritics_ReturnsTrue() => Assert.True(OverlayMessageValidation.IsValid("Tiếng Việt có dấu: ệ ố ạ ư ơ đ"));
}

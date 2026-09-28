using ParentalGuard.Service.Config;

namespace ParentalGuard.Service.Tests;

/// <summary>`FE-012`/`FE-012a` (`Specification/03-frontend-ui-spec.md` v0.6.0).</summary>
public class OverlayMessageValidatorTests
{
    [Fact]
    public void Validate_DefaultOverlayMessage_ReturnsValid()
    {
        OverlayMessageValidationResult result = OverlayMessageValidator.Validate(
            "Nội dung nhạy cảm được phát hiện, hãy thoát nội dung để bảo vệ chính bạn.");

        Assert.Equal(OverlayMessageValidationResult.Valid, result);
    }

    [Fact]
    public void Validate_EmptyMessage_ReturnsValid()
    {
        Assert.Equal(OverlayMessageValidationResult.Valid, OverlayMessageValidator.Validate(""));
    }

    [Fact]
    public void Validate_MessageOver255Chars_ReturnsTooLong()
    {
        string message = new('a', 256);

        Assert.Equal(OverlayMessageValidationResult.TooLong, OverlayMessageValidator.Validate(message));
    }

    [Fact]
    public void Validate_Message255Chars_ReturnsValid()
    {
        string message = new('a', 255);

        Assert.Equal(OverlayMessageValidationResult.Valid, OverlayMessageValidator.Validate(message));
    }

    [Theory]
    [InlineData("Cảnh báo!")]
    [InlineData("Giờ: 12:30, đúng không?")]
    [InlineData("Đây là (ví dụ) - \"trích dẫn\"")]
    public void Validate_AllowedPunctuationAndVietnameseDiacritics_ReturnsValid(string message)
    {
        Assert.Equal(OverlayMessageValidationResult.Valid, OverlayMessageValidator.Validate(message));
    }

    [Theory]
    [InlineData("Giá $100")]
    [InlineData("50% off")]
    [InlineData("a@b.com")]
    [InlineData("<script>")]
    [InlineData("emoji 😀 ở đây")]
    public void Validate_ForbiddenSymbolsOrEmoji_ReturnsInvalidCharacters(string message)
    {
        Assert.Equal(OverlayMessageValidationResult.InvalidCharacters, OverlayMessageValidator.Validate(message));
    }

    [Fact]
    public void Validate_ControlCharacter_ReturnsInvalidCharacters()
    {
        Assert.Equal(OverlayMessageValidationResult.InvalidCharacters, OverlayMessageValidator.Validate("hello\tworld"));
    }
}

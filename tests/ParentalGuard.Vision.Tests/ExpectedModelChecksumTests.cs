using System.Text.RegularExpressions;
using ParentalGuard.Vision.ModelIntegrity;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// `MISC-090` (Architecture/05-image-pipeline-architecture.md mục 7, ADR-46) — guard định dạng đơn
/// giản chống lỗi đánh máy khi cập nhật hằng số (64 ký tự hex thường, đúng độ dài SHA-256). Giá trị
/// THẬT phải khớp <c>sha256sum models/nsfw_model.onnx</c> của đúng bản model đóng gói cùng release —
/// không verify lại bằng cách đọc file thật ở đây (tránh test phụ thuộc đường dẫn repo checkout,
/// brittleness không cần thiết cho 1 hằng số build-time).
/// </summary>
public class ExpectedModelChecksumTests
{
    [Fact]
    public void Sha256Hex_Is64LowercaseHexCharacters()
    {
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), ExpectedModelChecksum.Sha256Hex);
    }
}

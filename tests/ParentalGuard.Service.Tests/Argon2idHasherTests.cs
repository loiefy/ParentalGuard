using System.Diagnostics;
using System.Text;
using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Tests;

/// <summary>PWD-011/012, Architecture/08 mục 4 — Argon2id hash/verify roundtrip + PHC format.</summary>
public class Argon2idHasherTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Hash_ThenVerify_SamePassword_ReturnsTrue()
    {
        byte[] password = Utf8("CorrectHorseBatteryStaple");
        string phc = Argon2idHasher.Hash(password, Argon2Params.Official);

        Assert.True(Argon2idHasher.Verify(password, phc));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        string phc = Argon2idHasher.Hash(Utf8("correct-password"), Argon2Params.Official);

        Assert.False(Argon2idHasher.Verify(Utf8("wrong-password"), phc));
    }

    [Fact]
    public void Hash_TwoCallsSamePassword_ProduceDifferentSaltAndPhcString()
    {
        byte[] password = Utf8("same-password");
        string phc1 = Argon2idHasher.Hash(password, Argon2Params.Official);
        string phc2 = Argon2idHasher.Hash(password, Argon2Params.Official);

        Assert.NotEqual(phc1, phc2); // PWD-012: salt ngẫu nhiên riêng mỗi lần hash
        Assert.True(Argon2idHasher.Verify(password, phc1));
        Assert.True(Argon2idHasher.Verify(password, phc2));
    }

    [Fact]
    public void Hash_ProducesExpectedPhcStructure()
    {
        string phc = Argon2idHasher.Hash(Utf8("x"), Argon2Params.Official);

        Assert.StartsWith("$argon2id$v=19$m=32768,t=2,p=2$", phc, StringComparison.Ordinal);
        Assert.Equal(6, phc.Split('$').Length);
    }

    [Fact]
    public void Verify_TamperedHashSegment_ReturnsFalse()
    {
        string phc = Argon2idHasher.Hash(Utf8("tamper-me"), Argon2Params.Official);
        string[] parts = phc.Split('$');
        // Lật ký tự ĐẦU (không phải cuối) của segment hash — ký tự cuối cùng của base64 không
        // padding có thể chỉ mang vài bit đệm bị bỏ qua lúc decode (tuỳ vị trí chia nhóm 3
        // byte/4 ký tự), có rủi ro flaky nếu lật đúng bit không ảnh hưởng giá trị byte giải mã.
        // Ký tự đầu luôn nằm trọn trong nhóm đầy đủ đầu tiên — lật chắc chắn đổi byte đã decode.
        parts[5] = (parts[5][0] == 'A' ? 'B' : 'A') + parts[5][1..];
        string tampered = string.Join('$', parts);

        Assert.False(Argon2idHasher.Verify(Utf8("tamper-me"), tampered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("argon2id$v=19$m=32768,t=2,p=2$c2FsdA$aGFzaA")] // thiếu '$' dẫn đầu
    [InlineData("$argon2i$v=19$m=32768,t=2,p=2$c2FsdA$aGFzaA")] // sai "argon2id"
    [InlineData("$argon2id$v=18$m=32768,t=2,p=2$c2FsdA$aGFzaA")] // sai version
    [InlineData("$argon2id$v=19$m=32768,t=2$c2FsdA$aGFzaA")] // thiếu tham số p
    [InlineData("$argon2id$v=19$m=abc,t=2,p=2$c2FsdA$aGFzaA")] // m không phải số
    [InlineData("$argon2id$v=19$m=32768,t=2,p=2$not!!base64$aGFzaA")] // salt không phải base64 hợp lệ
    public void Verify_MalformedPhc_ThrowsFormatException(string malformedPhc)
    {
        Assert.Throws<FormatException>(() => Argon2idHasher.Verify(Utf8("anything"), malformedPhc));
    }

    /// <summary>
    /// Benchmark thực tế (không phải assertion cứng — chỉ để ghi lại số liệu theo yêu cầu
    /// Architecture/08 mục 4.2 "feature-dev vẫn benchmark... xác nhận nằm trong ngân sách UX").
    /// Ngân sách UX mục tiêu 300-1500ms trên máy tầm trung; assert nới lỏng &lt;5000ms chỉ để bắt
    /// lỗi cấu hình tham số sai nghiêm trọng (vd nhầm đơn vị KB/MB), không phải test hiệu năng chặt.
    /// </summary>
    [Fact]
    public void Hash_OfficialParams_CompletesWithinGenerousBudget()
    {
        var stopwatch = Stopwatch.StartNew();
        Argon2idHasher.Hash(Utf8("benchmark-password"), Argon2Params.Official);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Argon2id m=32768,t=2,p=2 took {stopwatch.ElapsedMilliseconds}ms — investigate before shipping.");
    }
}

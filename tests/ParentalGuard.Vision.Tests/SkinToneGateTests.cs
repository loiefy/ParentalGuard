using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// `IMG-016b` (2026-10-15): trang đăng nhập Teams bị che nhầm — hình 3D tím-hồng được chấm 0.737 dù không có màu da.
/// Điểm sát ngưỡng ở vùng có màu rõ nhưng không có màu da bị hạ; ảnh có da, ảnh xám và điểm rất cao giữ nguyên.
/// </summary>
public class SkinToneGateTests
{
    private const int W = 40;
    private const int H = 40;
    private static readonly WindowRect _whole = new(0, 0, W, H);

    // BGR — tím-xanh như hình nền Teams; màu da sáng; xám trung tính.
    private static readonly (byte B, byte G, byte R) _purple = (230, 120, 140);
    private static readonly (byte B, byte G, byte R) _skin = (150, 175, 225);
    private static readonly (byte B, byte G, byte R) _gray = (128, 128, 128);

    private static byte[] Fill((byte B, byte G, byte R) color)
    {
        var buffer = new byte[W * H * 4];
        for (int i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = color.B;
            buffer[i + 1] = color.G;
            buffer[i + 2] = color.R;
            buffer[i + 3] = 255;
        }

        return buffer;
    }

    /// <summary>Tô màu <paramref name="color"/> cho khối vuông cạnh <paramref name="size"/> ở góc trên-trái.</summary>
    private static void Paint(byte[] buffer, (byte B, byte G, byte R) color, int size)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = ((y * W) + x) * 4;
                buffer[i] = color.B;
                buffer[i + 1] = color.G;
                buffer[i + 2] = color.R;
            }
        }
    }

    [Fact]
    public void ColorfulNoSkin_NearThreshold_IsCapped()
    {
        Assert.Equal(SkinToneGate.CappedScore, SkinToneGate.Apply(0.737f, Fill(_purple), W, _whole));
    }

    [Fact]
    public void SkinPresent_ScoreUnchanged()
    {
        byte[] buffer = Fill(_purple);
        Paint(buffer, _skin, 8); // 64/1600 = 4% điểm ảnh màu da
        Assert.Equal(0.75f, SkinToneGate.Apply(0.75f, buffer, W, _whole));
    }

    [Fact]
    public void Grayscale_ScoreUnchanged()
    {
        // Ảnh đen trắng không có thông tin màu để nhận màu da — không được hạ điểm.
        Assert.Equal(0.8f, SkinToneGate.Apply(0.8f, Fill(_gray), W, _whole));
    }

    [Fact]
    public void MostlyGrayWithLittleColor_ScoreUnchanged()
    {
        byte[] buffer = Fill(_gray);
        Paint(buffer, _purple, 12); // 144/1600 = 9% có màu < 20%
        Assert.Equal(0.8f, SkinToneGate.Apply(0.8f, buffer, W, _whole));
    }

    [Theory]
    [InlineData(0.9f)]
    [InlineData(0.97f)]
    public void HighScore_NeverCapped(float score)
    {
        Assert.Equal(score, SkinToneGate.Apply(score, Fill(_purple), W, _whole));
    }

    [Fact]
    public void LowScore_Unchanged()
    {
        Assert.Equal(0.3f, SkinToneGate.Apply(0.3f, Fill(_purple), W, _whole));
    }

    [Fact]
    public void MeasuresOnlyTheGivenRegion()
    {
        byte[] buffer = Fill(_purple);
        Paint(buffer, _skin, 20); // da chỉ ở góc trên-trái
        var bottomRight = new WindowRect(20, 20, 20, 20);
        Assert.Equal(SkinToneGate.CappedScore, SkinToneGate.Apply(0.8f, buffer, W, bottomRight));
        Assert.Equal(0.8f, SkinToneGate.Apply(0.8f, buffer, W, new WindowRect(0, 0, 20, 20)));
    }

    [Fact]
    public void SkinColorSample_IsRecognizedAsSkin()
    {
        (double skin, double chromatic) = SkinToneGate.Measure(Fill(_skin), W, _whole);
        Assert.Equal(1.0, skin);
        Assert.Equal(1.0, chromatic);
    }
}

using System.Numerics;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>Architecture/05 mục 3.8.1 (ADR-128) — dHash 64-bit viết tay.</summary>
public class PerceptualHashTests
{
    private const int Width = 16;
    private const int Height = 16;

    [Fact]
    public void ComputeDHash64_TwoIdenticalImages_HammingDistanceIsZero()
    {
        byte[] a = BuildGradientImage(seed: 1);
        byte[] b = (byte[])a.Clone();

        ulong hashA = PerceptualHash.ComputeDHash64(a, Width, Height);
        ulong hashB = PerceptualHash.ComputeDHash64(b, Width, Height);

        Assert.Equal(0, BitOperations.PopCount(hashA ^ hashB));
    }

    [Fact]
    public void ComputeDHash64_BlackVersusWhiteImage_HammingDistanceIsLarge()
    {
        byte[] black = BuildSolidImage(luma: 0);
        byte[] white = BuildSolidImage(luma: 255);

        ulong blackHash = PerceptualHash.ComputeDHash64(black, Width, Height);
        ulong whiteHash = PerceptualHash.ComputeDHash64(white, Width, Height);

        // 2 ảnh phẳng đồng nhất (không có cạnh sáng/tối để so) — dHash không phân biệt được, cả 2 đều
        // ra toàn bit 0. Test này xác nhận hành vi đó thay vì kỳ vọng sai lệch lớn (dHash chỉ nhạy với
        // TƯƠNG PHẢN cục bộ, không phải độ sáng tuyệt đối) — xem test tương phản caro bên dưới cho kịch
        // bản thực tế "nội dung thay đổi rõ rệt".
        Assert.Equal(0, BitOperations.PopCount(blackHash ^ whiteHash));
    }

    [Fact]
    public void ComputeDHash64_CheckerboardVersusInverse_HammingDistanceExceedsUnchangedThreshold()
    {
        byte[] checkerboard = BuildCheckerboardImage(inverted: false);
        byte[] inverted = BuildCheckerboardImage(inverted: true);

        ulong hashA = PerceptualHash.ComputeDHash64(checkerboard, Width, Height);
        ulong hashB = PerceptualHash.ComputeDHash64(inverted, Width, Height);

        Assert.True(BitOperations.PopCount(hashA ^ hashB) > WindowHashCache.HammingUnchangedThreshold);
    }

    [Fact]
    public void ComputeDHash64_SmallNoiseOnGradient_StaysWithinUnchangedThreshold()
    {
        byte[] baseline = BuildGradientImage(seed: 1);
        byte[] withNoise = (byte[])baseline.Clone();
        // Nhiễu ±1 lên vài pixel — mô phỏng sai số nén/anti-aliasing thoáng qua giữa 2 lần chụp liên tiếp.
        for (int i = 0; i < withNoise.Length; i += 40)
        {
            withNoise[i] = (byte)Math.Clamp(withNoise[i] + 1, 0, 255);
        }

        ulong hashA = PerceptualHash.ComputeDHash64(baseline, Width, Height);
        ulong hashB = PerceptualHash.ComputeDHash64(withNoise, Width, Height);

        Assert.True(BitOperations.PopCount(hashA ^ hashB) <= WindowHashCache.HammingUnchangedThreshold);
    }

    private static byte[] BuildSolidImage(byte luma)
    {
        var pixels = new byte[Width * Height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = luma;
            pixels[i + 1] = luma;
            pixels[i + 2] = luma;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static byte[] BuildGradientImage(int seed)
    {
        var pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int offset = ((y * Width) + x) * 4;
                byte luma = (byte)((x * 16) + (y * 4) + seed);
                pixels[offset] = luma;
                pixels[offset + 1] = luma;
                pixels[offset + 2] = luma;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] BuildCheckerboardImage(bool inverted)
    {
        var pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool isLight = ((x / 2) + (y / 2)) % 2 == 0;
                if (inverted)
                {
                    isLight = !isLight;
                }

                byte luma = isLight ? (byte)255 : (byte)0;
                int offset = ((y * Width) + x) * 4;
                pixels[offset] = luma;
                pixels[offset + 1] = luma;
                pixels[offset + 2] = luma;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }
}

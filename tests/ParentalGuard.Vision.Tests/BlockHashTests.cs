using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Bug real-hardware 2026-10-06 (video chủ dự án): ảnh băng chuyền trong 1 vùng của cửa sổ Edge đổi nhưng dHash 72 điểm
/// không nhận ra → điểm 91% của ảnh trước "kẹt" sang ảnh sau.
/// </summary>
public class BlockHashTests
{
    private const int W = 960;
    private const int H = 600;

    /// <summary>Nền tối có chữ/khối giả lập trang web + 1 vùng "ảnh" (băng chuyền) ở 1/4 dưới-phải.</summary>
    private static byte[] Page(int imageSeed)
    {
        byte[] px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                byte v = (byte)(((x / 40) + (y / 30)) % 2 == 0 ? 20 : 30);
                if (x >= 600 && x < 900 && y >= 400 && y < 560)
                {
                    v = (byte)((((x * 7) + (y * 13) + (imageSeed * 97)) % 256 + (imageSeed * 60)) % 256);
                }

                int o = ((y * W) + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = v;
                px[o + 3] = 255;
            }
        }

        return px;
    }

    [Fact]
    public void BlockHash_DetectsChangeConfinedToSmallRegion()
    {
        Span<ulong> a = stackalloc ulong[PerceptualHash.BlockHashWords];
        Span<ulong> b = stackalloc ulong[PerceptualHash.BlockHashWords];
        PerceptualHash.ComputeBlockDHash(Page(1), W, H, a);
        PerceptualHash.ComputeBlockDHash(Page(2), W, H, b);

        int distance = 0;
        for (int i = 0; i < a.Length; i++)
        {
            distance += System.Numerics.BitOperations.PopCount(a[i] ^ b[i]);
        }

        Assert.True(distance > WindowHashCache.BlockHashUnchangedThreshold, $"distance={distance}");
    }

    [Fact]
    public void BlockHash_IdenticalContent_ZeroDistance()
    {
        Span<ulong> a = stackalloc ulong[PerceptualHash.BlockHashWords];
        Span<ulong> b = stackalloc ulong[PerceptualHash.BlockHashWords];
        PerceptualHash.ComputeBlockDHash(Page(3), W, H, a);
        PerceptualHash.ComputeBlockDHash(Page(3), W, H, b);

        Assert.True(a.SequenceEqual(b));
    }

    [Fact]
    public void Cache_ForcesReclassificationAfterMaxConsecutiveReuse()
    {
        var cache = new WindowHashCache();
        IntPtr hwnd = new(7);
        ulong[] hash = new ulong[PerceptualHash.BlockHashWords];
        Assert.True(cache.ResolveContentChanged(hwnd, hash, out _));

        for (int i = 0; i < WindowHashCache.MaxConsecutiveReuse; i++)
        {
            Assert.False(cache.ResolveContentChanged(hwnd, hash, out _));
        }

        Assert.True(cache.ResolveContentChanged(hwnd, hash, out _));
    }
}

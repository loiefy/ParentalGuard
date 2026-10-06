using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>`IMG-016a`: vùng con thứ 6 = vùng video/ảnh trong cửa sổ (chuyển động, hoặc vùng "giống ảnh chụp").</summary>
public class ContentRegionDetectorTests
{
    private const int W = 990;
    private const int H = 640;

    /// <summary>Trang web giả: nền tối phẳng + vài dòng chữ xám; vùng "video" nhiều màu tại (330..660, 160..600).</summary>
    private static byte[] Page(int seed, bool withVideo = true)
    {
        byte[] px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int o = ((y * W) + x) * 4;
                byte gray = (byte)(y % 40 < 3 && x % 9 < 6 ? 200 : 25); // "chữ" xám
                px[o] = px[o + 1] = px[o + 2] = gray;
                if (withVideo && x >= 330 && x < 660 && y >= 160 && y < 600)
                {
                    px[o] = (byte)((x * 3 + seed * 50) % 256);
                    px[o + 1] = (byte)((y * 5 + seed * 30) % 256);
                    px[o + 2] = (byte)(((x + y) * 2 + seed * 70) % 256);
                }

                px[o + 3] = 255;
            }
        }

        return px;
    }

    private static ulong[] Hash(byte[] px)
    {
        var h = new ulong[PerceptualHash.BlockHashWords];
        PerceptualHash.ComputeBlockDHash(px, W, H, h);
        return h;
    }

    private static void AssertCoversVideo(WindowRect r)
    {
        Assert.True(r.X <= 340 && r.Y <= 170 && r.X + r.Width >= 650 && r.Y + r.Height >= 590, $"region={r}");
        Assert.True(r.Width < W * 0.6 && r.Height < H * 0.9, $"region too large={r}");
    }

    [Fact]
    public void Motion_FindsChangingVideoArea()
    {
        byte[] a = Page(1);
        byte[] b = Page(2);

        WindowRect? region = ContentRegionDetector.FromMotion(Hash(a), Hash(b), W, H);

        Assert.NotNull(region);
        AssertCoversVideo(ContentRegionDetector.Expand(region.Value, W, H));
    }

    [Fact]
    public void PhotoCells_FindsColourfulArea_IgnoresGrayText()
    {
        WindowRect? region = ContentRegionDetector.FromPhotoCells(Page(1), W, H);

        Assert.NotNull(region);
        AssertCoversVideo(ContentRegionDetector.Expand(region.Value, W, H));
    }

    [Fact]
    public void PlainTextPage_NoRegion()
    {
        byte[] page = Page(1, withVideo: false);

        Assert.Null(ContentRegionDetector.Detect(page, W, H, Hash(page), Hash(page)));
    }

    [Fact]
    public void NoMotion_FallsBackToPhotoCells()
    {
        byte[] page = Page(3);
        ulong[] h = Hash(page);

        WindowRect? region = ContentRegionDetector.Detect(page, W, H, h, h);

        Assert.NotNull(region);
        AssertCoversVideo(region.Value);
    }
}

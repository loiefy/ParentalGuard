using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Bug real-hardware 2026-10-06: Desktop Duplication là ảnh màn hình đã ghép — phần cửa sổ khác đè lên phải bị tô đen
/// trước khi phân loại, nếu không điểm (và overlay) bị gán nhầm cho cửa sổ bên dưới.
/// </summary>
public class OcclusionMaskTests
{
    [Fact]
    public void ToCropLocal_ClipsToCropAndTranslates_DropsNonIntersecting()
    {
        var crop = new WindowRect(-1000, 700, 900, 600);
        WindowRect[] occluders = [new(-1100, 650, 400, 200), new(2000, 0, 100, 100)];

        IReadOnlyList<WindowRect> local = OcclusionMask.ToCropLocal(crop, occluders);

        Assert.Equal([new WindowRect(0, 0, 300, 150)], local);
    }

    [Fact]
    public void VisibleFraction_NoMasks_IsOne_FullyCovered_IsZero_HalfCovered_IsHalf()
    {
        Assert.Equal(1.0, OcclusionMask.VisibleFraction(100, 100, []));
        Assert.Equal(0.0, OcclusionMask.VisibleFraction(100, 100, [new WindowRect(0, 0, 100, 100)]));
        Assert.Equal(0.5, OcclusionMask.VisibleFraction(100, 100, [new WindowRect(0, 0, 50, 100)]), 2);
    }

    [Fact]
    public void Apply_BlackensOnlyMaskedPixels()
    {
        byte[] bgra = Enumerable.Repeat((byte)200, 4 * 4 * 4).ToArray();

        OcclusionMask.Apply(bgra, 4, 4, [new WindowRect(1, 1, 2, 2), new WindowRect(3, 3, 10, 10)]);

        static bool IsBlack(byte[] b, int x, int y) => b.AsSpan(((y * 4) + x) * 4, 4).IndexOfAnyExcept((byte)0) < 0;
        Assert.True(IsBlack(bgra, 1, 1));
        Assert.True(IsBlack(bgra, 2, 2));
        Assert.True(IsBlack(bgra, 3, 3));
        Assert.False(IsBlack(bgra, 0, 0));
        Assert.False(IsBlack(bgra, 3, 0));
    }

    [Fact]
    public void OccludersAbove_ReturnsShownWindowsAboveTargetOnly()
    {
        IntPtr top = new(1), hidden = new(2), target = new(3), below = new(4);
        WindowSnapshot[] z =
        [
            new(top, new WindowRect(0, 0, 10, 10), IsShown: true, IsMonitorable: false),
            new(hidden, new WindowRect(0, 0, 50, 50), IsShown: false, IsMonitorable: true),
            new(target, new WindowRect(0, 0, 100, 100), IsShown: true, IsMonitorable: true),
            new(below, new WindowRect(0, 0, 200, 200), IsShown: true, IsMonitorable: true),
        ];

        Assert.Equal([new WindowRect(0, 0, 10, 10)], CandidateWindowSelector.OccludersAbove(target, z));
    }
}

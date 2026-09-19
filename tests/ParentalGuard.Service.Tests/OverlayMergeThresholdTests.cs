using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

public class OverlayMergeThresholdTests
{
    [Theory]
    [InlineData(10, false)] // BE-088: "vượt quá 10" — đúng 10 KHÔNG kích hoạt
    [InlineData(11, true)]
    [InlineData(20, true)]
    public void ShouldBeMerged_WhenNotCurrentlyMerged_UsesEnterThreshold(int activeCount, bool expected) =>
        Assert.Equal(expected, OverlayMergeThreshold.ShouldBeMerged(currentlyMerged: false, activeCount));

    [Theory]
    [InlineData(8, false)] // ADR-57: ngưỡng RA — đúng 8 KHÔNG còn gộp nữa
    [InlineData(9, true)]
    [InlineData(11, true)]
    public void ShouldBeMerged_WhenCurrentlyMerged_UsesExitThreshold(int activeCount, bool expected) =>
        Assert.Equal(expected, OverlayMergeThreshold.ShouldBeMerged(currentlyMerged: true, activeCount));

    [Fact]
    public void ShouldBeMerged_HysteresisBand_DoesNotFlapAt9()
    {
        // Dao động quanh biên 9 (giữa 8 và 10): 1 khi đã gộp phải GIỮ NGUYÊN gộp (ADR-57 — tránh flapping).
        Assert.True(OverlayMergeThreshold.ShouldBeMerged(currentlyMerged: true, activeCount: 9));
        Assert.False(OverlayMergeThreshold.ShouldBeMerged(currentlyMerged: false, activeCount: 9));
    }
}

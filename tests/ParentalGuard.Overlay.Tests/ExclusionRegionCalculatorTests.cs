using System.Drawing;
using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Tests;

/// <summary>`FE-016c` (Architecture/07-overlay-architecture.md mục 2.3/2.4).</summary>
public class ExclusionRegionCalculatorTests
{
    [Fact]
    public void FallbackRect_At100PercentDpi_Is160x40AnchoredTopRight()
    {
        var windowRect = new Rectangle(100, 200, 1000, 800); // Right=1100, Top=200

        Rectangle fallback = ExclusionRegionCalculator.FallbackRect(windowRect, dpiScale: 1.0);

        Assert.Equal(new Rectangle(1100 - 160, 200, 160, 40), fallback); // FE-016j
    }

    [Fact]
    public void FallbackRect_At150PercentDpi_ScalesWidthHeightAndAnchor()
    {
        var windowRect = new Rectangle(0, 0, 1000, 800);

        Rectangle fallback = ExclusionRegionCalculator.FallbackRect(windowRect, dpiScale: 1.5);

        Assert.Equal(240, fallback.Width); // 160 * 1.5
        Assert.Equal(60, fallback.Height); // 40 * 1.5
        Assert.Equal(1000 - 240, fallback.X);
        Assert.Equal(0, fallback.Y);
    }

    [Fact]
    public void PaddedRect_Adds16HorizontalAnd6VerticalPadding()
    {
        var windowRect = new Rectangle(0, 0, 1000, 800);
        var uiaButtonRect = new Rectangle(900, 100, 40, 30); // đủ xa biên trên để padding không bị Intersect cắt

        Rectangle padded = ExclusionRegionCalculator.PaddedRect(uiaButtonRect, windowRect, dpiScale: 1.0);

        Assert.Equal(new Rectangle(900 - 16, 100 - 6, 40 + 32, 30 + 12), padded); // FE-016j
    }

    [Fact]
    public void PaddedRect_ClampsToWindowBounds_WhenButtonNearEdge()
    {
        var windowRect = new Rectangle(0, 0, 1000, 800);
        var uiaButtonRect = new Rectangle(985, 5, 15, 15); // gần góc trên-phải, padding sẽ tràn ra ngoài windowRect

        Rectangle padded = ExclusionRegionCalculator.PaddedRect(uiaButtonRect, windowRect, dpiScale: 1.0);

        Assert.True(padded.Right <= windowRect.Right);
        Assert.True(padded.Top >= windowRect.Top);
    }
}

using System.Drawing;
using ParentalGuard.Overlay.Icons;

namespace ParentalGuard.Overlay.Tests;

/// <summary>
/// Bug real-hardware 2026-10-01 (2 màn hình khác DPI): icon màn hình phụ phình 160px và tràn ra ngoài mép
/// vì vị trí mặc định tính theo cỡ cố định 40px. `FE-020`: icon luôn nằm trọn trong work area.
/// </summary>
public class StatusIconPlacementTests
{
    // Màn hình phụ thật trên máy test: nằm bên trái, lệch xuống dưới màn hình chính.
    private static readonly Rectangle _secondaryWorkArea = new(-3840, 812, 3840, 2064);

    [Theory]
    [InlineData(40, 1.0)]
    [InlineData(80, 2.0)]
    [InlineData(160, 4.0)]
    public void DefaultLocation_BottomRight_FullyInsideWorkArea_AtAnyDpi(int iconSize, double dpiScale)
    {
        Point location = StatusIconForm.DefaultLocation(_secondaryWorkArea, iconSize, dpiScale);

        Assert.True(location.X >= _secondaryWorkArea.Left && location.X + iconSize <= _secondaryWorkArea.Right);
        Assert.True(location.Y >= _secondaryWorkArea.Top && location.Y + iconSize <= _secondaryWorkArea.Bottom);
        Assert.Equal(_secondaryWorkArea.Right - iconSize - (int)Math.Round(8 * dpiScale), location.X);
    }

    [Fact]
    public void ClampInto_SavedPositionOffScreen_PulledInsideWorkArea()
    {
        Point clamped = StatusIconForm.ClampInto(new Point(500, 5000), new Size(80, 80), _secondaryWorkArea);

        Assert.Equal(new Point(_secondaryWorkArea.Right - 80, _secondaryWorkArea.Bottom - 80), clamped);
    }

    [Fact]
    public void ClampInto_PositionAlreadyInside_Unchanged()
    {
        var inside = new Point(-2000, 1500);

        Assert.Equal(inside, StatusIconForm.ClampInto(inside, new Size(80, 80), _secondaryWorkArea));
    }
}

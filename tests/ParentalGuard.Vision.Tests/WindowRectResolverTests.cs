using ParentalGuard.Vision.Capture;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Regression bug 2026-09-30 — crop dùng thẳng toạ độ virtual desktop: màn hình phụ đặt bên trái
/// (toạ độ âm) hoặc cửa sổ lấn mép tạo box không hợp lệ, CopySubresourceRegion không copy gì.
/// </summary>
public class WindowRectResolverTests
{
    private static readonly WindowRect _primary = new(0, 0, 2880, 1920);
    private static readonly WindowRect _secondaryLeft = new(-1920, 406, 1920, 1080);

    [Fact]
    public void ToOutputLocalCrop_WindowOnSecondaryMonitorWithNegativeCoordinates_TranslatesToTextureOrigin()
    {
        WindowRect? crop = WindowRectResolver.ToOutputLocalCrop(new WindowRect(-1800, 500, 800, 600), _secondaryLeft);

        Assert.Equal(new WindowRect(120, 94, 800, 600), crop);
    }

    [Fact]
    public void ToOutputLocalCrop_WindowFullyInsidePrimaryAtOrigin_IsUnchanged()
    {
        var window = new WindowRect(100, 50, 1200, 900);

        Assert.Equal(window, WindowRectResolver.ToOutputLocalCrop(window, _primary));
    }

    [Fact]
    public void ToOutputLocalCrop_WindowOverhangingEdges_IsClippedToOutput()
    {
        WindowRect? crop = WindowRectResolver.ToOutputLocalCrop(new WindowRect(-50, 1800, 600, 400), _primary);

        Assert.Equal(new WindowRect(0, 1800, 550, 120), crop);
    }

    [Fact]
    public void ToOutputLocalCrop_WindowSpanningTwoMonitors_KeepsOnlyPartOnRequestedOutput()
    {
        var window = new WindowRect(-400, 600, 1000, 300);

        Assert.Equal(new WindowRect(1520, 194, 400, 300), WindowRectResolver.ToOutputLocalCrop(window, _secondaryLeft));
        Assert.Equal(new WindowRect(0, 600, 600, 300), WindowRectResolver.ToOutputLocalCrop(window, _primary));
    }

    [Fact]
    public void ToOutputLocalCrop_WindowNotOnOutput_ReturnsNull()
    {
        Assert.Null(WindowRectResolver.ToOutputLocalCrop(new WindowRect(100, 100, 200, 200), _secondaryLeft));
    }
}

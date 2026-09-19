using System.Drawing;
using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Tests;

/// <summary>Lớp 1 heuristic (`FE-016c` mục 2.2, ADR-55).</summary>
public class CloseButtonMatcherTests
{
    private static readonly Rectangle _windowRect = new(0, 0, 1000, 800); // top-right anchor = (1000, 0)

    [Fact]
    public void FindBestMatch_NoCandidates_ReturnsNull()
    {
        Assert.Null(CloseButtonMatcher.FindBestMatch([], _windowRect));
    }

    [Fact]
    public void FindBestMatch_NoneMatchHeuristic_ReturnsNull()
    {
        var candidates = new[]
        {
            new CloseButtonCandidate("MinimizeButton", "Minimize", "button", new Rectangle(900, 0, 30, 30)),
            new CloseButtonCandidate("MaximizeButton", "Maximize", "button", new Rectangle(930, 0, 30, 30)),
        };

        Assert.Null(CloseButtonMatcher.FindBestMatch(candidates, _windowRect));
    }

    [Fact]
    public void FindBestMatch_ExactAutomationIdMatch_IsSelected()
    {
        var candidates = new[] { new CloseButtonCandidate("CloseButton", "", "", new Rectangle(960, 0, 30, 30)) };

        CloseButtonCandidate? match = CloseButtonMatcher.FindBestMatch(candidates, _windowRect);

        Assert.NotNull(match);
        Assert.Equal("CloseButton", match.Value.AutomationId);
    }

    [Theory]
    [InlineData("Close")]
    [InlineData("close window")]
    [InlineData("Đóng")]
    [InlineData("đóng cửa sổ")]
    public void FindBestMatch_NameContainsCloseKeyword_CaseAndAccentSensitiveButMatches(string name)
    {
        var candidates = new[] { new CloseButtonCandidate(string.Empty, name, string.Empty, new Rectangle(960, 0, 30, 30)) };

        Assert.NotNull(CloseButtonMatcher.FindBestMatch(candidates, _windowRect));
    }

    [Fact]
    public void FindBestMatch_MultipleMatches_PicksClosestToTopRightCorner()
    {
        var farCandidate = new CloseButtonCandidate("", "Close tab", "", new Rectangle(100, 700, 30, 30)); // gần góc dưới-trái
        var nearCandidate = new CloseButtonCandidate("", "Close window", "", new Rectangle(960, 0, 30, 30)); // gần góc trên-phải
        var candidates = new[] { farCandidate, nearCandidate };

        CloseButtonCandidate? match = CloseButtonMatcher.FindBestMatch(candidates, _windowRect);

        Assert.Equal(nearCandidate.BoundingRectangle, match!.Value.BoundingRectangle);
    }
}

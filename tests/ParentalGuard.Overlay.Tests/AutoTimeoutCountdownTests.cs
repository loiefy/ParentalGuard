using ParentalGuard.Overlay.Resources;

namespace ParentalGuard.Overlay.Tests;

/// <summary>`FE-016g` (v0.9.1)/`BE-089b` — pure format function, không cần message loop WinForms.</summary>
public class AutoTimeoutCountdownTests
{
    [Fact]
    public void AutoTimeoutCountdown_At30Seconds_FormatsWithLeadingZero()
    {
        string text = OverlayStrings.AutoTimeoutCountdown(30);

        Assert.Equal("Tự động đóng sau: 00:30", text);
    }

    [Fact]
    public void AutoTimeoutCountdown_At5Seconds_PadsSingleDigit()
    {
        string text = OverlayStrings.AutoTimeoutCountdown(5);

        Assert.Equal("Tự động đóng sau: 00:05", text);
    }

    [Fact]
    public void AutoTimeoutCountdown_At0Seconds_ShowsZero()
    {
        string text = OverlayStrings.AutoTimeoutCountdown(0);

        Assert.Equal("Tự động đóng sau: 00:00", text);
    }
}

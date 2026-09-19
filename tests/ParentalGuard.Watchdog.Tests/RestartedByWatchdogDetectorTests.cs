using ParentalGuard.Ipc.Tamper;

namespace ParentalGuard.Watchdog.Tests;

/// <summary>Mục 3.4 bước 3/3.6 — cờ startup arg dùng chung cả `Service` lẫn `Watchdog` Worker.</summary>
public class RestartedByWatchdogDetectorTests
{
    [Fact]
    public void WasRestartedByWatchdog_WhenFlagPresent_ReturnsTrue()
    {
        Assert.True(RestartedByWatchdogDetector.WasRestartedByWatchdog(["--restarted-by-watchdog"]));
    }

    [Fact]
    public void WasRestartedByWatchdog_WhenFlagAbsent_ReturnsFalse()
    {
        Assert.False(RestartedByWatchdogDetector.WasRestartedByWatchdog([]));
        Assert.False(RestartedByWatchdogDetector.WasRestartedByWatchdog(["--other-flag"]));
    }
}

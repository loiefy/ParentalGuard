using ParentalGuard.Tray;

namespace ParentalGuard.Tray.Tests;

/// <summary>`FE-023a`: click liên tục vào icon khay chỉ mở Dashboard tối đa 1 lần/2 giây.</summary>
public sealed class LaunchThrottleTests
{
    private long _nowMs = 1_000_000;

    private LaunchThrottle CreateThrottle() => new(TimeSpan.FromSeconds(2), () => _nowMs);

    [Fact]
    public void FirstClick_Allowed()
    {
        Assert.True(CreateThrottle().TryAcquire());
    }

    [Fact]
    public void DoubleClick_OnlyFirstOpens()
    {
        LaunchThrottle throttle = CreateThrottle();
        Assert.True(throttle.TryAcquire());
        _nowMs += 300;
        Assert.False(throttle.TryAcquire());
    }

    [Fact]
    public void ClickAfterInterval_AllowedAgain()
    {
        LaunchThrottle throttle = CreateThrottle();
        Assert.True(throttle.TryAcquire());
        _nowMs += 1_999;
        Assert.False(throttle.TryAcquire());
        _nowMs += 1;
        Assert.True(throttle.TryAcquire());
    }

    [Fact]
    public void RejectedClick_DoesNotExtendWindow()
    {
        LaunchThrottle throttle = CreateThrottle();
        Assert.True(throttle.TryAcquire());
        _nowMs += 1_500;
        Assert.False(throttle.TryAcquire());
        _nowMs += 500;
        Assert.True(throttle.TryAcquire());
    }
}

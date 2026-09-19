using ParentalGuard.Service.Tamper;

namespace ParentalGuard.Service.Tests;

/// <summary>ANTI-061 (Architecture/09 mục 6.2, ADR-100) — giữ ERROR liên tục hết cửa sổ T, chỉ 1 lần callback(true) mỗi đợt.</summary>
public class AttackBannerTimerTests
{
    [Fact]
    public void Arm_FirstCall_ReturnsTrueAndNotifiesActive()
    {
        var notifications = new List<bool>();
        using var timer = new AttackBannerTimer(TimeSpan.FromSeconds(30), notifications.Add);

        bool justActivated = timer.Arm();

        Assert.True(justActivated);
        Assert.Equal([true], notifications);
    }

    [Fact]
    public void Arm_CalledAgainWhileActive_ReturnsFalseAndDoesNotRenotify()
    {
        var notifications = new List<bool>();
        using var timer = new AttackBannerTimer(TimeSpan.FromSeconds(30), notifications.Add);

        timer.Arm();
        bool secondCall = timer.Arm();

        Assert.False(secondCall);
        Assert.Equal([true], notifications);
    }

    [Fact]
    public async Task Arm_AfterQuietWindowElapses_AutoDeactivatesAndNotifiesFalse()
    {
        var notifications = new List<bool>();
        using var timer = new AttackBannerTimer(TimeSpan.FromMilliseconds(300), notifications.Add);

        timer.Arm();
        await WaitUntilAsync(() => notifications.Count >= 2, TimeSpan.FromSeconds(5));

        Assert.Equal([true, false], notifications);
    }

    [Fact]
    public async Task Arm_RearmedBeforeWindowElapses_ExtendsWindowInsteadOfDeactivating()
    {
        var notifications = new List<bool>();
        using var timer = new AttackBannerTimer(TimeSpan.FromSeconds(2), notifications.Add);

        timer.Arm();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        timer.Arm(); // sự kiện mới trong cửa sổ — (re)khởi động đếm ngược, KHÔNG tắt banner.
        await Task.Delay(TimeSpan.FromSeconds(1)); // < 2s tính từ lần Arm() thứ 2 — chưa tới lúc timeout thật sự.

        Assert.Equal([true], notifications); // vẫn đang active.
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < timeout)
        {
            await Task.Delay(20);
        }
    }
}

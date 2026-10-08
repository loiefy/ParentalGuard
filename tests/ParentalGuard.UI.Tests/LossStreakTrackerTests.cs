using ParentalGuard.UI.Game;

namespace ParentalGuard.UI.Tests;

/// <summary>`PAUSE-049` (2026-10-08): thua liên tiếp 5–10 lần (ngẫu nhiên) thì gợi ý "Hay là không tạm dừng nữa?" + Donate.</summary>
public sealed class LossStreakTrackerTests
{
    [Fact]
    public void SuggestsStop_ExactlyAtRandomThreshold_ThenRestarts()
    {
        var thresholds = new Queue<int>([7, 5, 9]); // ngưỡng mới được bốc ngay sau mỗi lần gợi ý
        var tracker = new LossStreakTracker((min, maxExclusive) =>
        {
            Assert.Equal((5, 11), (min, maxExclusive));
            return thresholds.Dequeue();
        });

        bool[] first = [.. Enumerable.Range(0, 7).Select(_ => tracker.RecordLoss())];
        Assert.Equal([false, false, false, false, false, false, true], first);

        bool[] second = [.. Enumerable.Range(0, 5).Select(_ => tracker.RecordLoss())];
        Assert.Equal([false, false, false, false, true], second);
    }

    [Fact]
    public void Win_ResetsStreak()
    {
        var tracker = new LossStreakTracker((_, _) => 5);
        for (int i = 0; i < 4; i++)
        {
            Assert.False(tracker.RecordLoss());
        }

        tracker.RecordWin();

        for (int i = 0; i < 4; i++)
        {
            Assert.False(tracker.RecordLoss());
        }

        Assert.True(tracker.RecordLoss());
    }

    [Fact]
    public void RealRandomThreshold_IsBetween5And10()
    {
        for (int run = 0; run < 50; run++)
        {
            var tracker = new LossStreakTracker();
            int losses = 1;
            while (!tracker.RecordLoss())
            {
                losses++;
            }

            Assert.InRange(losses, 5, 10);
        }
    }
}

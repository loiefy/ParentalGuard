using ParentalGuard.UI.Game;

namespace ParentalGuard.UI.Tests;

/// <summary>`PAUSE-045a`/`PAUSE-046a` (2026-10-08) — luật trò chơi nhảy vượt rào: tăng tốc dần, nhảy theo tốc độ, rào dày dần.</summary>
public sealed class HurdleGameEngineTests
{
    private const double C = HurdleGameEngine.ContactHalfSpan;

    /// <summary>Gom rào thành cụm (2 rào cách nhau &lt; 2 m phải qua bằng 1 cú nhảy).</summary>
    private static List<(double First, double Last)> Groups(IReadOnlyList<double> hurdles)
    {
        var groups = new List<(double, double)>();
        foreach (double h in hurdles)
        {
            if (groups.Count > 0 && h - groups[^1].Item2 < 2.0)
            {
                groups[^1] = (groups[^1].Item1, h);
            }
            else
            {
                groups.Add((h, h));
            }
        }

        return groups;
    }

    /// <summary>"Người chơi mẫu": bật nhảy ở giữa cửa sổ thời điểm hợp lệ của cụm rào kế tiếp.</summary>
    private static void PlayPerfectly(HurdleGameEngine engine, double frameSeconds = 1.0 / 60)
    {
        List<(double First, double Last)> groups = Groups(engine.Hurdles);
        int next = 0;
        engine.Press();
        while (engine.State == HurdleGameState.Running)
        {
            while (next < groups.Count && groups[next].Last + C < engine.Distance)
            {
                next++;
            }

            if (next < groups.Count && !engine.IsAirborne)
            {
                JumpProfile jump = HurdleGameEngine.ProfileAt(engine.Speed);
                double earliest = groups[next].Last + C - jump.ClearStart - jump.ClearLength;
                double latest = groups[next].First - C - jump.ClearStart;
                if (engine.Distance >= (earliest + latest) / 2)
                {
                    engine.Press();
                }
            }

            engine.Advance(frameSeconds);
        }
    }

    [Theory]
    [InlineData(1000u, 1ul)]
    [InlineData(1000u, 42ul)]
    [InlineData(2000u, 42ul)]
    [InlineData(3000u, 7ul)]
    public void PerfectPlayer_Wins_AtEveryDifficulty(uint meters, ulong seed)
    {
        var engine = new HurdleGameEngine(meters, seed);

        PlayPerfectly(engine);

        Assert.Equal(HurdleGameState.Won, engine.State);
        Assert.Equal(meters, engine.Distance);
        Assert.InRange(engine.ElapsedSeconds, HurdleGameEngine.SecondsToRun(meters) - 1, HurdleGameEngine.SecondsToRun(meters) + 1);
    }

    [Fact]
    public void Pace_From10To4MinPerKm_ReachedAt500m_1000mTakesFiveAndHalfMinutes()
    {
        Assert.Equal(600, HurdleGameEngine.PaceAt(0));
        Assert.Equal(420, HurdleGameEngine.PaceAt(250), 6);
        Assert.Equal(240, HurdleGameEngine.PaceAt(500));
        Assert.Equal(240, HurdleGameEngine.PaceAt(2500));
        Assert.Equal(210, HurdleGameEngine.SecondsToRun(500), 6);
        Assert.Equal(330, HurdleGameEngine.SecondsToRun(1000), 6);
        Assert.True(HurdleGameEngine.SecondsToRun(1000) >= 180); // vẫn ≥ 3 phút (PAUSE-045)
    }

    [Fact]
    public void FasterRunning_JumpsLongerAndHangsLonger()
    {
        JumpProfile slow = HurdleGameEngine.ProfileAt(HurdleGameEngine.SpeedAt(0));
        JumpProfile fast = HurdleGameEngine.ProfileAt(HurdleGameEngine.SpeedAt(600));

        Assert.True(fast.AirTime > slow.AirTime);
        Assert.True(fast.Length > slow.Length * 3);
    }

    [Theory]
    [InlineData(1000u)]
    [InlineData(2000u)]
    [InlineData(3000u)]
    public void EveryGroup_IsJumpable_WithReactionTimeAfterLanding(uint meters)
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 99, 12345 })
        {
            List<(double First, double Last)> groups = Groups(new HurdleGameEngine(meters, seed).Hurdles);
            for (int i = 0; i < groups.Count; i++)
            {
                double speed = HurdleGameEngine.SpeedAt(groups[i].First);
                JumpProfile jump = HurdleGameEngine.ProfileAt(speed);
                double window = jump.ClearLength - (groups[i].Last - groups[i].First) - (2 * C);
                Assert.True(window >= speed * HurdleGameEngine.MinTimingWindowSeconds - 1e-9, $"seed {seed} cụm {i}: cửa sổ {window:F2} m");

                if (i + 1 < groups.Count)
                {
                    // Kể cả bật nhảy muộn nhất ở cụm này, vẫn còn thời gian phản xạ trước lần nhảy muộn nhất ở cụm sau.
                    double landing = groups[i].First - C - jump.ClearStart + jump.Length;
                    JumpProfile nextJump = HurdleGameEngine.ProfileAt(HurdleGameEngine.SpeedAt(groups[i + 1].First));
                    double nextLatest = groups[i + 1].First - C - nextJump.ClearStart;
                    Assert.True(nextLatest - landing >= speed * HurdleGameEngine.MinReactionSeconds - 1e-9, $"seed {seed} cụm {i}");
                }
            }
        }
    }

    [Fact]
    public void HurdlesGetDenserFurtherAlong_AndHarderIsDenser()
    {
        double Density(HurdleGameEngine e, double from, double to) => e.Hurdles.Count(h => h >= from && h < to) / (to - from);

        var easy = new HurdleGameEngine(1000, 5);
        var hard = new HurdleGameEngine(3000, 5);

        Assert.True(Density(hard, 2000, 3000) > Density(hard, 0, 1000));
        Assert.True(Density(hard, 600, 1000) > Density(easy, 600, 1000));
        Assert.Contains(Groups(hard.Hurdles), g => g.Last > g.First); // có cặp rào sát nhau
    }

    [Fact]
    public void NeverJumping_LosesAtFirstHurdle()
    {
        var engine = new HurdleGameEngine(1000, seed: 7);
        engine.Press();

        for (int i = 0; i < 30; i++)
        {
            engine.Advance(1);
        }

        Assert.Equal(HurdleGameState.Lost, engine.State);
        Assert.InRange(engine.Distance, HurdleGameEngine.FirstHurdleAt - 1, HurdleGameEngine.FirstHurdleAt);
    }

    [Fact]
    public void LongFrameStall_DoesNotTunnelThroughHurdle()
    {
        var engine = new HurdleGameEngine(1000, seed: 7);
        engine.Press();

        engine.Advance(5);   // ~8,5 m
        engine.Advance(5);   // ~17 m
        Assert.True(engine.Distance < HurdleGameEngine.FirstHurdleAt - 5);
        engine.Advance(5);   // 1 khung hình giật 5 giây vắt qua rào đầu tiên ở 25 m

        Assert.Equal(HurdleGameState.Lost, engine.State);
    }

    [Fact]
    public void Ready_DoesNotMove_UntilPressed_AndNoDoubleJump()
    {
        var engine = new HurdleGameEngine(1000, seed: 1);
        engine.Advance(5);
        Assert.Equal(0, engine.Distance);

        Assert.True(engine.Press()); // bắt đầu
        Assert.True(engine.Press()); // nhảy
        engine.Advance(0.1);
        Assert.False(engine.Press()); // đang trên không
    }
}

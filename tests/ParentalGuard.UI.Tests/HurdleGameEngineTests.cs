using ParentalGuard.UI.Game;

namespace ParentalGuard.UI.Tests;

/// <summary>`PAUSE-045b`/`PAUSE-046b` (2026-10-08) — luật trò chơi: tăng tốc dần (pace 6 → 2), nhảy theo tốc độ, rào và hố dày dần.</summary>
public sealed class HurdleGameEngineTests
{
    /// <summary>Gom chướng ngại thành cụm qua bằng 1 cú nhảy: hố đứng riêng; rào cách nhau &lt; 2 m chung 1 cụm.</summary>
    private static List<(ObstacleKind Kind, double First, double Last)> Groups(IReadOnlyList<Obstacle> obstacles)
    {
        var groups = new List<(ObstacleKind Kind, double First, double Last)>();
        foreach (Obstacle o in obstacles)
        {
            if (o.Kind == ObstacleKind.Hurdle && groups.Count > 0 && groups[^1].Kind == ObstacleKind.Hurdle && o.Start - groups[^1].Last < 2.0)
            {
                groups[^1] = (ObstacleKind.Hurdle, groups[^1].First, o.Start);
            }
            else
            {
                groups.Add((o.Kind, o.Start, o.End));
            }
        }

        return groups;
    }

    /// <summary>"Người chơi mẫu": bật nhảy ở giữa cửa sổ thời điểm hợp lệ của cụm kế tiếp.</summary>
    private static void PlayPerfectly(HurdleGameEngine engine, double frameSeconds = 1.0 / 60)
    {
        var groups = Groups(engine.Obstacles);
        int next = 0;
        engine.Press();
        while (engine.State == HurdleGameState.Running)
        {
            while (next < groups.Count && groups[next].Last < engine.Distance)
            {
                next++;
            }

            if (next < groups.Count && !engine.IsAirborne)
            {
                (double earliest, double latest) = HurdleGameEngine.TakeoffWindow(groups[next].Kind, groups[next].First, groups[next].Last, HurdleGameEngine.ProfileAt(engine.Speed));
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
    [InlineData(1000u, 777ul)]
    [InlineData(2000u, 42ul)]
    [InlineData(3000u, 7ul)]
    public void PerfectPlayer_Wins_AtEveryDifficulty(uint meters, ulong seed)
    {
        var engine = new HurdleGameEngine(meters, seed);

        PlayPerfectly(engine);

        Assert.True(engine.State == HurdleGameState.Won, $"thua vì {engine.LostTo} ở mét {engine.Distance:F1}");
        Assert.InRange(engine.ElapsedSeconds, HurdleGameEngine.SecondsToRun(meters) - 1, HurdleGameEngine.SecondsToRun(meters) + 1);
    }

    [Fact]
    public void Pace_From6To2MinPerKm_ReachedAt500m_1000mTakesThreeMinutes()
    {
        Assert.Equal(360, HurdleGameEngine.PaceAt(0));
        Assert.Equal(240, HurdleGameEngine.PaceAt(250), 6);
        Assert.Equal(120, HurdleGameEngine.PaceAt(500));
        Assert.Equal(120, HurdleGameEngine.PaceAt(2500));
        Assert.Equal(120, HurdleGameEngine.SecondsToRun(500), 6);
        Assert.Equal(180, HurdleGameEngine.SecondsToRun(1000), 6);
        Assert.Equal(420, HurdleGameEngine.SecondsToRun(3000), 6);
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
            var groups = Groups(new HurdleGameEngine(meters, seed).Obstacles);
            for (int i = 0; i < groups.Count; i++)
            {
                double speed = HurdleGameEngine.SpeedAt(groups[i].First);
                JumpProfile jump = HurdleGameEngine.ProfileAt(speed);
                (double earliest, double latest) = HurdleGameEngine.TakeoffWindow(groups[i].Kind, groups[i].First, groups[i].Last, jump);
                Assert.True(latest - earliest >= (speed * HurdleGameEngine.MinTimingWindowSeconds) - 1e-9, $"seed {seed} cụm {i} ({groups[i].Kind}): cửa sổ {latest - earliest:F2} m");

                if (i + 1 < groups.Count)
                {
                    // Bật nhảy muộn nhất ở cụm này vẫn còn thời gian phản xạ trước lần bật nhảy muộn nhất ở cụm sau.
                    double landing = latest + jump.Length;
                    JumpProfile nextJump = HurdleGameEngine.ProfileAt(HurdleGameEngine.SpeedAt(groups[i + 1].First));
                    double nextLatest = HurdleGameEngine.TakeoffWindow(groups[i + 1].Kind, groups[i + 1].First, groups[i + 1].Last, nextJump).Latest;
                    Assert.True(nextLatest - landing >= (speed * HurdleGameEngine.MinReactionSeconds) - 1e-9, $"seed {seed} cụm {i}");
                }
            }
        }
    }

    [Fact]
    public void Obstacles_DenserFurtherAlong_HarderDenser_MixHurdlesPitsAndClusters()
    {
        static double Density(HurdleGameEngine e, double from, double to) => e.Obstacles.Count(o => o.Start >= from && o.Start < to) / (to - from);

        var easy = new HurdleGameEngine(1000, 5);
        var hard = new HurdleGameEngine(3000, 5);

        Assert.True(Density(hard, 2000, 3000) > Density(hard, 0, 1000));
        Assert.True(Density(hard, 0, 1000) > Density(easy, 0, 1000));
        Assert.Contains(hard.Obstacles, o => o.Kind == ObstacleKind.Pit);
        Assert.Contains(Groups(hard.Obstacles), g => g.Kind == ObstacleKind.Hurdle && g.Last > g.First);
        // Mật độ theo thời gian ≥ 2 lần bản trước (104 rào / 330 giây ≈ 0,315 chướng ngại/giây).
        double perSecond = easy.Obstacles.Count / HurdleGameEngine.SecondsToRun(1000);
        Assert.True(perSecond >= 2 * 104 / 330.0, $"1000 m chỉ có {perSecond:F2} chướng ngại/giây");
    }

    [Fact]
    public void NeverJumping_LosesAtFirstObstacle()
    {
        var engine = new HurdleGameEngine(1000, seed: 7);
        engine.Press();

        for (int i = 0; i < 30; i++)
        {
            engine.Advance(1);
        }

        Assert.Equal(HurdleGameState.Lost, engine.State);
        Assert.InRange(engine.Distance, HurdleGameEngine.FirstObstacleAt - 1, HurdleGameEngine.FirstObstacleAt + 0.5);
    }

    [Fact]
    public void LandingInsidePit_Loses_AsPit()
    {
        var engine = new HurdleGameEngine(3000, seed: 5);
        Obstacle pit = engine.Obstacles.First(o => o.Kind == ObstacleKind.Pit);
        engine.Press();

        // Nhảy qua mọi chướng ngại trước hố bằng "người chơi mẫu", rồi đứng yên dưới đất đi vào hố.
        var groups = Groups(engine.Obstacles).Where(g => g.Last < pit.Start).ToList();
        int next = 0;
        while (engine.State == HurdleGameState.Running && engine.Distance < pit.End)
        {
            while (next < groups.Count && groups[next].Last < engine.Distance)
            {
                next++;
            }

            if (next < groups.Count && !engine.IsAirborne)
            {
                (double earliest, double latest) = HurdleGameEngine.TakeoffWindow(groups[next].Kind, groups[next].First, groups[next].Last, HurdleGameEngine.ProfileAt(engine.Speed));
                if (engine.Distance >= (earliest + latest) / 2)
                {
                    engine.Press();
                }
            }

            engine.Advance(1.0 / 60);
        }

        Assert.Equal(HurdleGameState.Lost, engine.State);
        Assert.Equal(ObstacleKind.Pit, engine.LostTo);
    }

    [Fact]
    public void LongFrameStall_DoesNotTunnelThroughObstacle()
    {
        var engine = new HurdleGameEngine(1000, seed: 7);
        engine.Press();

        engine.Advance(5);   // ~14 m
        Assert.True(engine.Distance < HurdleGameEngine.FirstObstacleAt - 5);
        engine.Advance(5);   // 1 khung hình giật 5 giây vắt qua chướng ngại đầu tiên ở 25 m

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

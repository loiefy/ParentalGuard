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
    [InlineData(800u, 1ul)]
    [InlineData(800u, 42ul)]
    [InlineData(800u, 777ul)]
    [InlineData(1600u, 42ul)]
    [InlineData(2000u, 7ul)]
    [InlineData(2000u, 99ul)]
    public void PerfectPlayer_Wins_AtEveryDifficulty(uint meters, ulong seed)
    {
        var engine = new HurdleGameEngine(meters, seed);

        PlayPerfectly(engine);

        Assert.True(engine.State == HurdleGameState.Won, $"thua vì {engine.LostTo} ở mét {engine.Distance:F1}");
        Assert.InRange(engine.ElapsedSeconds, HurdleGameEngine.SecondsToRun(meters) - 1, HurdleGameEngine.SecondsToRun(meters) + 1);
    }

    [Fact]
    public void Pace_From6To3MinPerKm_ReachedAt500m_800mOverThreeMinutes()
    {
        Assert.Equal(360, HurdleGameEngine.PaceAt(0));
        Assert.Equal(270, HurdleGameEngine.PaceAt(250), 6);
        Assert.Equal(180, HurdleGameEngine.PaceAt(500));
        Assert.Equal(180, HurdleGameEngine.PaceAt(2000));
        Assert.Equal(135, HurdleGameEngine.SecondsToRun(500), 6);
        Assert.Equal(189, HurdleGameEngine.SecondsToRun(800), 6);
        Assert.Equal(405, HurdleGameEngine.SecondsToRun(2000), 6);
    }

    [Fact]
    public void FasterRunning_JumpsLongerAndHangsLonger()
    {
        JumpProfile slow = HurdleGameEngine.ProfileAt(HurdleGameEngine.SpeedAt(0));
        JumpProfile fast = HurdleGameEngine.ProfileAt(HurdleGameEngine.SpeedAt(600));

        Assert.True(fast.AirTime > slow.AirTime);
        Assert.True(fast.Length > slow.Length * 2); // pace 6 → 3: tốc độ gấp đôi, bay lâu hơn ⇒ xa ~2,7 lần
    }

    [Theory]
    [InlineData(800u)]
    [InlineData(1600u)]
    [InlineData(2000u)]
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

        var easy = new HurdleGameEngine(800, 5);
        var hard = new HurdleGameEngine(2000, 5);

        Assert.True(Density(hard, 1200, 2000) > Density(hard, 0, 800));
        // Khó dày hơn Dễ — so trung bình nhiều seed (1 seed riêng lẻ ở đoạn đầu chạy chậm có thể dao động ngẫu nhiên).
        double hardAverage = Enumerable.Range(1, 10).Average(s => Density(new HurdleGameEngine(2000, (ulong)s), 0, 800));
        double easyAverage = Enumerable.Range(1, 10).Average(s => Density(new HurdleGameEngine(800, (ulong)s), 0, 800));
        Assert.True(hardAverage > easyAverage, $"khó {hardAverage:F3} / dễ {easyAverage:F3}");
        Assert.Contains(hard.Obstacles, o => o.Kind == ObstacleKind.Pit);
        Assert.Contains(Groups(hard.Obstacles), g => g.Kind == ObstacleKind.Hurdle && g.Last > g.First);
        // Mật độ theo thời gian ≥ 2 lần bản trước (104 rào / 330 giây ≈ 0,315 chướng ngại/giây).
        double perSecond = easy.Obstacles.Count / HurdleGameEngine.SecondsToRun(800);
        Assert.True(perSecond >= 2 * 104 / 330.0, $"800 m chỉ có {perSecond:F2} chướng ngại/giây");
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
        var engine = new HurdleGameEngine(2000, seed: 5);
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

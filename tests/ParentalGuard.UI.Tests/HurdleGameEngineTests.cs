using ParentalGuard.UI.Game;

namespace ParentalGuard.UI.Tests;

/// <summary>`PAUSE-045`/`PAUSE-046` (2026-10-08) — luật trò chơi nhảy vượt rào.</summary>
public sealed class HurdleGameEngineTests
{
    /// <summary>"Người chơi hoàn hảo": nhảy khi rào kế tiếp còn cách 2,2 m (giữa cửa sổ thời điểm nhảy hợp lệ).</summary>
    private static void PlayPerfectly(HurdleGameEngine engine, double frameSeconds = 1.0 / 60)
    {
        engine.Press();
        while (engine.State == HurdleGameState.Running)
        {
            double next = engine.Hurdles.FirstOrDefault(h => h > engine.Distance, double.MaxValue);
            if (!engine.IsAirborne && next - engine.Distance is > 1.9 and < 2.5)
            {
                engine.Press();
            }

            engine.Advance(frameSeconds);
        }
    }

    [Theory]
    [InlineData(1000u)]
    [InlineData(2000u)]
    [InlineData(3000u)]
    public void PerfectPlayer_Wins_And1000MetersTakesAtLeastThreeMinutes(uint meters)
    {
        var engine = new HurdleGameEngine(meters, seed: 42);

        PlayPerfectly(engine);

        Assert.Equal(HurdleGameState.Won, engine.State);
        Assert.Equal(meters, engine.Distance);
        Assert.True(engine.ElapsedSeconds >= 180 * (meters / 1000.0), $"{engine.ElapsedSeconds}s");
    }

    [Fact]
    public void NeverJumping_LosesAtFirstHurdle()
    {
        var engine = new HurdleGameEngine(1000, seed: 7);
        engine.Press();

        for (int i = 0; i < 10; i++)
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

        engine.Advance(4);   // 22 m
        engine.Advance(3);   // 1 khung hình giật 3 giây: 22 → 38,5 m, đi qua rào đầu tiên ở 30 m

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

    [Fact]
    public void HarderDifficulty_HasDenserHurdles_AllGapsJumpable()
    {
        foreach (uint meters in new uint[] { 1000, 2000, 3000 })
        {
            var engine = new HurdleGameEngine(meters, seed: 99);
            double[] gaps = [.. engine.Hurdles.Zip(engine.Hurdles.Skip(1), (a, b) => b - a)];
            (double min, double max) = HurdleGameEngine.GapRange(meters);
            Assert.All(gaps, g => Assert.InRange(g, min, max));
        }

        Assert.True(HurdleGameEngine.GapRange(3000).Max < HurdleGameEngine.GapRange(1000).Min + 4);
    }
}

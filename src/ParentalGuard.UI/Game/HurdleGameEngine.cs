namespace ParentalGuard.UI.Game;

public enum HurdleGameState
{
    /// <summary>Chờ người chơi nhấn Space/chuột trái để bắt đầu.</summary>
    Ready,
    Running,
    Won,
    Lost,
}

/// <summary>
/// `PAUSE-045`/`PAUSE-046` (2026-10-08) — luật trò chơi nhảy vượt rào, tách khỏi giao diện để test được.
/// <list type="bullet">
/// <item>Nhân vật chạy với tốc độ CỐ ĐỊNH <see cref="SpeedMetersPerSecond"/> cho mọi độ khó; quãng đường tính theo thời gian thực (không
/// theo số khung hình) ⇒ 1000 m luôn mất ≥ 181 giây — không thể chạy nhanh hơn bằng cách tăng tốc khung hình.</item>
/// <item>Độ khó = quãng đường (1000/2000/3000 m); quãng đường dài hơn thì rào cũng dày hơn.</item>
/// <item>Vật lý tính theo bước nhỏ cố định (<see cref="StepSeconds"/>) — khung hình bị giật vẫn không "xuyên" qua rào.</item>
/// </list>
/// Đơn vị: mét và giây.
/// </summary>
public sealed class HurdleGameEngine
{
    public const double SpeedMetersPerSecond = 5.5;
    public const double Gravity = 20.0;
    public const double JumpVelocity = 8.0;
    public const double HurdleHeight = 0.9;
    public const double HurdleHalfWidth = 0.2;
    public const double RunnerHalfWidth = 0.25;
    public const double FirstHurdleAt = 30.0;
    public const double StepSeconds = 1.0 / 240;

    private readonly List<double> _hurdles = [];
    private double _verticalVelocity;
    private int _nextHurdleIndex;

    public HurdleGameEngine(uint targetMeters, ulong seed)
    {
        TargetMeters = targetMeters;
        (double minGap, double maxGap) = GapRange(targetMeters);
        ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        double position = FirstHurdleAt;
        while (position < targetMeters - 5)
        {
            _hurdles.Add(position);
            position += minGap + (NextUnit(ref state) * (maxGap - minGap));
        }
    }

    public uint TargetMeters { get; }

    public HurdleGameState State { get; private set; } = HurdleGameState.Ready;

    public double Distance { get; private set; }

    /// <summary>Độ cao chân nhân vật so với mặt đường.</summary>
    public double RunnerHeight { get; private set; }

    public double ElapsedSeconds { get; private set; }

    public bool IsAirborne => RunnerHeight > 0 || _verticalVelocity > 0;

    public IReadOnlyList<double> Hurdles => _hurdles;

    /// <summary>Khoảng cách giữa 2 rào (m): khoảng trống nhỏ nhất 8 m vẫn đủ chỗ tiếp đất rồi nhảy tiếp (nhảy 1 lần đi được 4,4 m).</summary>
    public static (double Min, double Max) GapRange(uint targetMeters) => targetMeters switch
    {
        >= 3000 => (8.0, 16.0),
        >= 2000 => (10.0, 19.0),
        _ => (13.0, 22.0),
    };

    /// <summary>Space/chuột trái: lúc chờ thì bắt đầu chạy, lúc đang chạy thì nhảy (chỉ khi đang chạm đất — không nhảy 2 lần trên không).</summary>
    public bool Press()
    {
        switch (State)
        {
            case HurdleGameState.Ready:
                State = HurdleGameState.Running;
                return true;
            case HurdleGameState.Running when !IsAirborne:
                _verticalVelocity = JumpVelocity;
                return true;
            default:
                return false;
        }
    }

    public void Advance(double seconds)
    {
        if (State != HurdleGameState.Running || seconds <= 0)
        {
            return;
        }

        double remaining = Math.Min(seconds, 5.0); // máy treo lâu: vẫn mô phỏng đủ (nhân vật đứng đất → vấp rào), không giới hạn ngầm
        while (remaining > 0 && State == HurdleGameState.Running)
        {
            double dt = Math.Min(StepSeconds, remaining);
            remaining -= dt;
            Step(dt);
        }
    }

    private void Step(double dt)
    {
        ElapsedSeconds += dt;
        Distance += SpeedMetersPerSecond * dt;

        if (IsAirborne)
        {
            RunnerHeight += _verticalVelocity * dt;
            _verticalVelocity -= Gravity * dt;
            if (RunnerHeight <= 0)
            {
                RunnerHeight = 0;
                _verticalVelocity = 0;
            }
        }

        while (_nextHurdleIndex < _hurdles.Count && _hurdles[_nextHurdleIndex] + HurdleHalfWidth < Distance - RunnerHalfWidth)
        {
            _nextHurdleIndex++;
        }

        if (_nextHurdleIndex < _hurdles.Count
            && Math.Abs(_hurdles[_nextHurdleIndex] - Distance) < HurdleHalfWidth + RunnerHalfWidth
            && RunnerHeight < HurdleHeight)
        {
            State = HurdleGameState.Lost;
            return;
        }

        if (Distance >= TargetMeters)
        {
            Distance = TargetMeters;
            State = HurdleGameState.Won;
        }
    }

    /// <summary>xorshift64* — chỉ để rải rào (không phải bảo mật); tránh <see cref="Random"/> theo quy tắc phân tích CA5394.</summary>
    private static double NextUnit(ref ulong state)
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return ((state * 0x2545F4914F6CDD1DUL) >> 11) * (1.0 / (1UL << 53));
    }
}

namespace ParentalGuard.UI.Game;

public enum HurdleGameState
{
    /// <summary>Chờ người chơi nhấn Space/chuột trái để bắt đầu.</summary>
    Ready,
    Running,
    Won,
    Lost,
}

/// <summary>Thông số 1 cú nhảy ở 1 tốc độ — tính sẵn để sinh rào và để "người chơi mẫu" trong test biết lúc nào nhảy.</summary>
/// <param name="AirTime">Thời gian trên không (giây).</param>
/// <param name="Peak">Độ cao đỉnh (m).</param>
/// <param name="ClearStart">Quãng đường từ điểm bật nhảy tới lúc chân vượt độ cao rào (m).</param>
/// <param name="ClearLength">Quãng đường chân ở trên độ cao rào (m).</param>
/// <param name="Length">Quãng đường cả cú nhảy (m).</param>
public readonly record struct JumpProfile(double AirTime, double Peak, double ClearStart, double ClearLength, double Length);

/// <summary>
/// `PAUSE-045a`/`PAUSE-046a` (2026-10-08, chủ dự án chỉnh) — luật trò chơi nhảy vượt rào, tách khỏi giao diện để test được.
/// <list type="bullet">
/// <item>Tốc độ tăng dần theo quãng đường: pace 10 phút/km lúc xuất phát giảm đều tới 4 phút/km ở mét 500, sau đó giữ 4 phút/km.
/// Tốc độ chỉ phụ thuộc quãng đường (không phụ thuộc khung hình) ⇒ 1000 m luôn mất 330 giây (5 phút 30 giây).</item>
/// <item>Chạy càng nhanh, cú nhảy càng lâu và càng xa (bay 0,9 → 1,2 giây; xa ~1,5 → 5 m).</item>
/// <item>Rào dày dần theo quãng đường (độ khó cao dày hơn); cặp rào sát nhau chỉ xuất hiện khi 1 cú nhảy ở tốc độ đó qua được cả 2
/// với cửa sổ thời điểm nhảy ≥ <see cref="MinTimingWindowSeconds"/>; sau mỗi lần tiếp đất luôn còn ≥ <see cref="MinReactionSeconds"/>.</item>
/// <item>Vật lý tính theo bước nhỏ cố định (<see cref="StepSeconds"/>) — khung hình bị giật vẫn không "xuyên" qua rào.</item>
/// </list>
/// Đơn vị: mét và giây.
/// </summary>
public sealed class HurdleGameEngine
{
    public const double StartPaceSecondsPerKm = 600;
    public const double EndPaceSecondsPerKm = 240;
    public const double PaceRampMeters = 500;

    public const double HurdleHeight = 0.9;
    public const double HurdleHalfWidth = 0.1;
    public const double RunnerHalfWidth = 0.2;
    public const double FirstHurdleAt = 25.0;
    public const double StepSeconds = 1.0 / 240;
    public const double MinTimingWindowSeconds = 0.22;
    public const double MinReactionSeconds = 0.4;

    /// <summary>Khoảng cách hiệu dụng (2 nửa bề rộng) mà chân phải ở trên độ cao rào.</summary>
    public const double ContactHalfSpan = HurdleHalfWidth + RunnerHalfWidth;

    private readonly List<double> _hurdles = [];
    private double _verticalVelocity;
    private double _gravity;
    private int _nextHurdleIndex;

    public HurdleGameEngine(uint targetMeters, ulong seed)
    {
        TargetMeters = targetMeters;
        GenerateHurdles(targetMeters, seed == 0 ? 0x9E3779B97F4A7C15UL : seed);
    }

    public uint TargetMeters { get; }

    public HurdleGameState State { get; private set; } = HurdleGameState.Ready;

    public double Distance { get; private set; }

    /// <summary>Độ cao chân nhân vật so với mặt đường.</summary>
    public double RunnerHeight { get; private set; }

    public double ElapsedSeconds { get; private set; }

    public bool IsAirborne => RunnerHeight > 0 || _verticalVelocity > 0;

    public double Speed => SpeedAt(Distance);

    /// <summary>Pace hiện tại (giây/km).</summary>
    public double PaceSecondsPerKm => 1000 / Speed;

    public IReadOnlyList<double> Hurdles => _hurdles;

    /// <summary>Pace (giây/km) tại quãng đường <paramref name="meters"/>: 600 → 240 tuyến tính trong 500 m đầu.</summary>
    public static double PaceAt(double meters) =>
        meters >= PaceRampMeters
            ? EndPaceSecondsPerKm
            : StartPaceSecondsPerKm - ((StartPaceSecondsPerKm - EndPaceSecondsPerKm) * meters / PaceRampMeters);

    public static double SpeedAt(double meters) => 1000 / PaceAt(meters);

    /// <summary>Thời gian (giây) để chạy hết <paramref name="meters"/> — tích phân pace; 1000 m = 330 giây.</summary>
    public static double SecondsToRun(double meters)
    {
        double ramp = Math.Min(meters, PaceRampMeters);
        double slope = (StartPaceSecondsPerKm - EndPaceSecondsPerKm) / PaceRampMeters / 1000;
        double rampSeconds = (StartPaceSecondsPerKm / 1000 * ramp) - (slope * ramp * ramp / 2);
        return rampSeconds + (Math.Max(0, meters - PaceRampMeters) * EndPaceSecondsPerKm / 1000);
    }

    /// <summary>Cú nhảy ở tốc độ <paramref name="speed"/>: chậm nhất bay 0,9 giây cao 1,8 m; nhanh nhất bay 1,2 giây cao 2,0 m.</summary>
    public static JumpProfile ProfileAt(double speed)
    {
        double min = 1000 / StartPaceSecondsPerKm;
        double max = 1000 / EndPaceSecondsPerKm;
        double t = Math.Clamp((speed - min) / (max - min), 0, 1);
        double airTime = 0.9 + (0.3 * t);
        double peak = 1.8 + (0.2 * t);
        double clearFraction = Math.Sqrt(1 - (HurdleHeight / peak)); // phần thời gian trên không mà chân cao hơn rào
        return new JumpProfile(
            airTime,
            peak,
            ClearStart: speed * airTime * (1 - clearFraction) / 2,
            ClearLength: speed * airTime * clearFraction,
            Length: speed * airTime);
    }

    /// <summary>Space/chuột trái: lúc chờ thì bắt đầu chạy, lúc đang chạy thì nhảy (chỉ khi đang chạm đất — không nhảy 2 lần trên không).</summary>
    public bool Press()
    {
        switch (State)
        {
            case HurdleGameState.Ready:
                State = HurdleGameState.Running;
                return true;
            case HurdleGameState.Running when !IsAirborne:
                JumpProfile jump = ProfileAt(Speed);
                _gravity = 8 * jump.Peak / (jump.AirTime * jump.AirTime);
                _verticalVelocity = 4 * jump.Peak / jump.AirTime;
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

        double remaining = Math.Min(seconds, 5.0);
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
        Distance += SpeedAt(Distance) * dt;

        if (IsAirborne)
        {
            RunnerHeight += _verticalVelocity * dt;
            _verticalVelocity -= _gravity * dt;
            if (RunnerHeight <= 0)
            {
                RunnerHeight = 0;
                _verticalVelocity = 0;
            }
        }

        while (_nextHurdleIndex < _hurdles.Count && _hurdles[_nextHurdleIndex] + ContactHalfSpan < Distance)
        {
            _nextHurdleIndex++;
        }

        if (_nextHurdleIndex < _hurdles.Count
            && Math.Abs(_hurdles[_nextHurdleIndex] - Distance) < ContactHalfSpan
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

    /// <summary>
    /// Rải rào theo "cụm" (1 rào, hoặc 2 rào sát nhau qua được bằng 1 cú nhảy). Khoảng cách giữa 2 cụm = phần bắt buộc (đủ tiếp đất
    /// + phản xạ ở tốc độ tại đó) + phần ngẫu nhiên co dần theo quãng đường và độ khó.
    /// </summary>
    private void GenerateHurdles(uint targetMeters, ulong state)
    {
        double difficulty = targetMeters >= 3000 ? 0.7 : targetMeters >= 2000 ? 0.85 : 1.0;
        double position = FirstHurdleAt;
        while (position < targetMeters - 8)
        {
            double speed = SpeedAt(position);
            JumpProfile jump = ProfileAt(speed);
            double progress = Math.Min(1, position / 2500);

            // Cặp rào: xác suất tăng theo quãng đường, chỉ khi 1 cú nhảy qua được cả 2 với cửa sổ thời điểm đủ rộng.
            double pairSpacing = 0.9 + (NextUnit(ref state) * 0.7);
            bool pair = NextUnit(ref state) < 0.35 * progress
                && jump.ClearLength - pairSpacing - (2 * ContactHalfSpan) >= speed * MinTimingWindowSeconds;

            _hurdles.Add(position);
            double groupEnd = position;
            if (pair && position + pairSpacing < targetMeters - 8)
            {
                groupEnd = position + pairSpacing;
                _hurdles.Add(groupEnd);
            }

            double required = jump.Length + (speed * MinReactionSeconds) + (2 * ContactHalfSpan) + jump.ClearStart;
            // Phần ngẫu nhiên tính theo GIÂY (× tốc độ) để lúc đầu chạy chậm rào cũng không thưa: 0,6–2,4 giây → 0,05–0,6 giây.
            double extraMaxSeconds = (2.4 - (1.8 * progress)) * difficulty;
            double extraMinSeconds = (0.6 - (0.55 * progress)) * difficulty;
            position = groupEnd + required + (speed * (extraMinSeconds + (NextUnit(ref state) * (extraMaxSeconds - extraMinSeconds))));
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

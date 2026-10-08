namespace ParentalGuard.UI.Game;

public enum HurdleGameState
{
    /// <summary>Chờ người chơi nhấn Space/chuột trái để bắt đầu.</summary>
    Ready,
    Running,
    Won,
    Lost,
}

public enum ObstacleKind
{
    /// <summary>Rào: chân phải cao hơn <see cref="HurdleGameEngine.HurdleHeight"/> khi đi ngang qua.</summary>
    Hurdle,

    /// <summary>Hố: chạm đất trong lòng hố là rơi xuống — phải bay qua trọn bề rộng hố.</summary>
    Pit,
}

/// <summary>1 chướng ngại. Rào: <see cref="Start"/> = <see cref="End"/> = tâm rào. Hố: từ mép <see cref="Start"/> tới mép <see cref="End"/>.</summary>
public readonly record struct Obstacle(ObstacleKind Kind, double Start, double End);

/// <summary>Thông số 1 cú nhảy ở 1 tốc độ — tính sẵn để sinh chướng ngại và để "người chơi mẫu" trong test biết lúc nào nhảy.</summary>
/// <param name="AirTime">Thời gian trên không (giây).</param>
/// <param name="Peak">Độ cao đỉnh (m).</param>
/// <param name="ClearStart">Quãng đường từ điểm bật nhảy tới lúc chân vượt độ cao rào (m).</param>
/// <param name="ClearLength">Quãng đường chân ở trên độ cao rào (m).</param>
/// <param name="Length">Quãng đường cả cú nhảy (m).</param>
public readonly record struct JumpProfile(double AirTime, double Peak, double ClearStart, double ClearLength, double Length);

/// <summary>
/// `PAUSE-045b`/`PAUSE-046b` (2026-10-08, chủ dự án chỉnh lần 2) — luật trò chơi nhảy vượt rào, tách khỏi giao diện để test được.
/// <list type="bullet">
/// <item>Tốc độ tăng dần theo quãng đường: pace 6 phút/km lúc xuất phát giảm đều tới 2 phút/km ở mét 500, sau đó giữ 2 phút/km.
/// Tốc độ chỉ phụ thuộc quãng đường (không phụ thuộc khung hình) ⇒ 1000 m luôn mất đúng 180 giây (3 phút).</item>
/// <item>Chạy càng nhanh, cú nhảy càng lâu và càng xa (bay 0,9 → 1,2 giây; xa ~3 → 10 m).</item>
/// <item>Chướng ngại theo "cụm": 1–5 rào sát nhau qua được bằng 1 cú nhảy, hoặc 1 hố; hố xen kẽ ngẫu nhiên với rào. Cụm dày dần
/// theo quãng đường (độ khó cao dày hơn). Mọi cụm luôn có cửa sổ thời điểm nhảy ≥ <see cref="MinTimingWindowSeconds"/>, và sau mỗi lần
/// tiếp đất còn ≥ <see cref="MinReactionSeconds"/> trước lần nhảy muộn nhất của cụm sau.</item>
/// <item>Vật lý tính theo bước nhỏ cố định (<see cref="StepSeconds"/>) — khung hình bị giật vẫn không "xuyên" qua chướng ngại.</item>
/// </list>
/// Đơn vị: mét và giây.
/// </summary>
public sealed class HurdleGameEngine
{
    public const double StartPaceSecondsPerKm = 360;
    public const double EndPaceSecondsPerKm = 120;
    public const double PaceRampMeters = 500;

    public const double HurdleHeight = 0.9;
    public const double HurdleHalfWidth = 0.1;
    public const double RunnerHalfWidth = 0.2;
    public const double FirstObstacleAt = 25.0;
    public const double StepSeconds = 1.0 / 240;
    public const double MinTimingWindowSeconds = 0.22;
    public const double MinReactionSeconds = 0.35;

    /// <summary>Khoảng cách hiệu dụng (2 nửa bề rộng) mà chân phải ở trên độ cao rào.</summary>
    public const double ContactHalfSpan = HurdleHalfWidth + RunnerHalfWidth;

    /// <summary>Mép hố được "tha" bấy nhiêu: chạm đất sát mép (trong khoảng này) chưa tính là rơi.</summary>
    public const double PitEdgeTolerance = 0.15;

    public const double MinPitWidth = 1.0;
    public const double MaxPitWidth = 2.6;

    private readonly List<Obstacle> _obstacles = [];
    private double _verticalVelocity;
    private double _gravity;
    private int _nextObstacleIndex;

    public HurdleGameEngine(uint targetMeters, ulong seed)
    {
        TargetMeters = targetMeters;
        Generate(targetMeters, seed == 0 ? 0x9E3779B97F4A7C15UL : seed);
    }

    public uint TargetMeters { get; }

    public HurdleGameState State { get; private set; } = HurdleGameState.Ready;

    /// <summary>Thua vì loại chướng ngại nào (chỉ có nghĩa khi <see cref="State"/> = Lost).</summary>
    public ObstacleKind LostTo { get; private set; }

    public double Distance { get; private set; }

    /// <summary>Độ cao chân nhân vật so với mặt đường.</summary>
    public double RunnerHeight { get; private set; }

    public double ElapsedSeconds { get; private set; }

    public bool IsAirborne => RunnerHeight > 0 || _verticalVelocity > 0;

    public double Speed => SpeedAt(Distance);

    /// <summary>Pace hiện tại (giây/km).</summary>
    public double PaceSecondsPerKm => 1000 / Speed;

    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    /// <summary>Pace (giây/km) tại quãng đường <paramref name="meters"/>: 360 → 120 tuyến tính trong 500 m đầu.</summary>
    public static double PaceAt(double meters) =>
        meters >= PaceRampMeters
            ? EndPaceSecondsPerKm
            : StartPaceSecondsPerKm - ((StartPaceSecondsPerKm - EndPaceSecondsPerKm) * meters / PaceRampMeters);

    public static double SpeedAt(double meters) => 1000 / PaceAt(meters);

    /// <summary>Thời gian (giây) để chạy hết <paramref name="meters"/> — tích phân pace; 1000 m = 180 giây.</summary>
    public static double SecondsToRun(double meters)
    {
        double ramp = Math.Min(meters, PaceRampMeters);
        double slope = (StartPaceSecondsPerKm - EndPaceSecondsPerKm) / PaceRampMeters / 1000;
        double rampSeconds = (StartPaceSecondsPerKm / 1000 * ramp) - (slope * ramp * ramp / 2);
        return rampSeconds + (Math.Max(0, meters - PaceRampMeters) * EndPaceSecondsPerKm / 1000);
    }

    /// <summary>Cú nhảy ở tốc độ <paramref name="speed"/>: chậm nhất bay 0,9 giây cao 1,4 m; nhanh nhất bay 1,2 giây cao 1,6 m.</summary>
    public static JumpProfile ProfileAt(double speed)
    {
        double min = 1000 / StartPaceSecondsPerKm;
        double max = 1000 / EndPaceSecondsPerKm;
        double t = Math.Clamp((speed - min) / (max - min), 0, 1);
        double airTime = 0.9 + (0.3 * t);
        double peak = 1.4 + (0.2 * t); // chủ dự án yêu cầu nhảy thấp hơn (2026-10-08): 1,8–2,0 → 1,4–1,6 m
        double clearFraction = Math.Sqrt(1 - (HurdleHeight / peak)); // phần thời gian trên không mà chân cao hơn rào
        return new JumpProfile(
            airTime,
            peak,
            ClearStart: speed * airTime * (1 - clearFraction) / 2,
            ClearLength: speed * airTime * clearFraction,
            Length: speed * airTime);
    }

    /// <summary>Khoảng vị trí bật nhảy hợp lệ để qua 1 cụm (cụm rào từ <paramref name="first"/> tới <paramref name="last"/>, hoặc 1 hố).</summary>
    public static (double Earliest, double Latest) TakeoffWindow(ObstacleKind kind, double first, double last, JumpProfile jump) => kind switch
    {
        ObstacleKind.Pit => (last - PitEdgeTolerance - jump.Length, first + PitEdgeTolerance),
        _ => (last + ContactHalfSpan - jump.ClearStart - jump.ClearLength, first - ContactHalfSpan - jump.ClearStart),
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

        while (_nextObstacleIndex < _obstacles.Count && PassedBy(_obstacles[_nextObstacleIndex], Distance))
        {
            _nextObstacleIndex++;
        }

        if (_nextObstacleIndex < _obstacles.Count && Hits(_obstacles[_nextObstacleIndex]))
        {
            LostTo = _obstacles[_nextObstacleIndex].Kind;
            State = HurdleGameState.Lost;
            return;
        }

        if (Distance >= TargetMeters)
        {
            Distance = TargetMeters;
            State = HurdleGameState.Won;
        }
    }

    private static bool PassedBy(Obstacle o, double distance) => o.Kind == ObstacleKind.Pit
        ? o.End - PitEdgeTolerance <= distance
        : o.End + ContactHalfSpan < distance;

    private bool Hits(Obstacle o) => o.Kind == ObstacleKind.Pit
        ? !IsAirborne && Distance > o.Start + PitEdgeTolerance && Distance < o.End - PitEdgeTolerance
        : Math.Abs(o.Start - Distance) < ContactHalfSpan && RunnerHeight < HurdleHeight;

    /// <summary>
    /// Rải chướng ngại theo cụm. Vị trí cụm sau được tính từ tình huống XẤU NHẤT của cụm trước (bật nhảy muộn nhất → tiếp đất xa
    /// nhất), cộng thời gian phản xạ, rồi cộng phần ngẫu nhiên (theo giây × tốc độ) co dần theo quãng đường và độ khó.
    /// </summary>
    private void Generate(uint targetMeters, ulong state)
    {
        double difficulty = targetMeters >= 3000 ? 0.6 : targetMeters >= 2000 ? 0.8 : 1.0;
        double position = FirstObstacleAt;
        while (position < targetMeters - 15)
        {
            double speed = SpeedAt(position);
            JumpProfile jump = ProfileAt(speed);
            double progress = Math.Min(1, position / 2000);
            double minWindow = speed * MinTimingWindowSeconds;

            double latestTakeoff;
            double maxPitWidth = Math.Min(MaxPitWidth, jump.Length + (2 * PitEdgeTolerance) - minWindow);
            if (position > 60 && NextUnit(ref state) < 0.3 && maxPitWidth >= MinPitWidth)
            {
                double width = MinPitWidth + (NextUnit(ref state) * (maxPitWidth - MinPitWidth));
                _obstacles.Add(new Obstacle(ObstacleKind.Pit, position, position + width));
                latestTakeoff = TakeoffWindow(ObstacleKind.Pit, position, position + width, jump).Latest;
            }
            else
            {
                // Cụm rào: thêm rào kế tiếp (cách 0,9–1,6 m) khi 1 cú nhảy vẫn qua được cả cụm (tối đa 5 rào — chỉ khả thi khi chạy
                // nhanh, cú nhảy dài); xác suất thêm tăng theo quãng đường.
                double last = position;
                _obstacles.Add(new Obstacle(ObstacleKind.Hurdle, position, position));
                for (int i = 1; i < 5; i++)
                {
                    double next = last + 0.9 + (NextUnit(ref state) * 0.7);
                    (double earliest, double latest) = TakeoffWindow(ObstacleKind.Hurdle, position, next, jump);
                    if (NextUnit(ref state) >= 0.65 + (0.3 * progress) || latest - earliest < minWindow || next > targetMeters - 15)
                    {
                        break;
                    }

                    _obstacles.Add(new Obstacle(ObstacleKind.Hurdle, next, next));
                    last = next;
                }

                latestTakeoff = TakeoffWindow(ObstacleKind.Hurdle, position, last, jump).Latest;
            }

            double landing = latestTakeoff + jump.Length;
            double extraSeconds = (0.05 + (NextUnit(ref state) * (0.55 - (0.45 * progress)))) * difficulty;
            double candidate = landing + (speed * MinReactionSeconds) + (speed * extraSeconds);

            // Cụm sau là rào thì cần thêm đoạn lấy đà (ContactHalfSpan + ClearStart ở tốc độ tại đó); là hố thì cần ít hơn — dùng
            // trường hợp cần nhiều hơn (rào) để vị trí hợp lệ cho cả 2 loại.
            JumpProfile nextJump = ProfileAt(SpeedAt(candidate + 5));
            position = candidate + ContactHalfSpan + nextJump.ClearStart;
        }
    }

    /// <summary>xorshift64* — chỉ để rải chướng ngại (không phải bảo mật); tránh <see cref="Random"/> theo quy tắc phân tích CA5394.</summary>
    private static double NextUnit(ref ulong state)
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return ((state * 0x2545F4914F6CDD1DUL) >> 11) * (1.0 / (1UL << 53));
    }
}

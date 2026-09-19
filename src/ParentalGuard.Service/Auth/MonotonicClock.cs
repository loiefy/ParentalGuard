namespace ParentalGuard.Service.Auth;

/// <summary>
/// "Monotonic anchor" chống bypass rate-limit qua đổi giờ hệ thống (`PWD-022`, Architecture/08 mục
/// 7.7, ADR-77): neo <c>(tick0, wall0)</c> tại lúc khởi tạo (Service <c>Starting</c>), mọi lần đọc
/// sau đó cộng dồn theo <see cref="Environment.TickCount64"/> thay vì tin thẳng đồng hồ hệ thống.
/// Miễn nhiễm với đổi giờ xảy ra SAU khi neo được tạo — rủi ro dư (đổi giờ trước khi Service khởi
/// động lại) đã ghi nhận minh bạch ở Architecture/08, không giải quyết thêm ở đây.
/// </summary>
/// <remarks>
/// Không <c>sealed</c>, <see cref="UtcNowUnixMs"/> là <c>virtual</c> — CHỈ để test-double
/// (<c>tests/.../FakeMonotonicClock</c>) cấy giá trị "hiện tại" giả điều khiển được, tránh
/// unit test hành vi hết hạn/rate-limit (mục 7.7) phải chờ đồng hồ máy thật trôi qua 15 giây/30
/// phút. Production code (<see cref="AuthCoordinator"/>) luôn dùng chính class này không kế thừa.
/// </remarks>
public class MonotonicClock
{
    private readonly long _tick0;
    private readonly long _wall0UnixMs;

    public MonotonicClock() : this(Environment.TickCount64, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
    {
    }

    /// <summary>Cấy neo tường minh — chủ yếu dùng để unit test công thức cộng dồn mà không phụ thuộc thời điểm chạy test thật.</summary>
    public MonotonicClock(long tick0, long wall0UnixMs)
    {
        _tick0 = tick0;
        _wall0UnixMs = wall0UnixMs;
    }

    public virtual long UtcNowUnixMs => _wall0UnixMs + (Environment.TickCount64 - _tick0);
}

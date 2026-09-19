using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Test double cho <see cref="MonotonicClock"/> — cho phép nhảy thời gian tuỳ ý (hết hạn
/// <c>PendingSetup</c> 30 phút, <c>action_token</c> 15 giây, delay rate-limit tới 30 phút) mà
/// không cần chờ đồng hồ máy thật trôi qua trong lúc test (Architecture/08 mục 7.1/7.2/7.7).
/// </summary>
internal sealed class FakeMonotonicClock() : MonotonicClock(0, 0)
{
    public long Now { get; set; } = 1_700_000_000_000L;

    public override long UtcNowUnixMs => Now;
}

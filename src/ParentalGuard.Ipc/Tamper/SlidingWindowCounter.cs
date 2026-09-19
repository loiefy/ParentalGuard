namespace ParentalGuard.Ipc.Tamper;

/// <summary>
/// Bộ đếm cửa sổ trượt dùng chung cho cả 2 phía <c>ANTI-060</c> (Architecture/09-anti-tamper-architecture.md
/// mục 6.1, ADR-99) — RAM-only, N=5 lần / T=30 phút (ĐÃ CHỐT chủ dự án 2026-09-20). Mỗi bên
/// (<c>Service</c>/<c>Watchdog</c>) giữ 1 instance ĐỘC LẬP — lớp này chỉ là công thức thuần, không tự
/// biết mình đang chạy ở process nào.
/// </summary>
public sealed class SlidingWindowCounter(int thresholdCount, TimeSpan window)
{
    private readonly object _sync = new();
    private readonly Queue<DateTimeOffset> _timestamps = new();

    /// <summary>Ghi nhận 1 sự kiện tại <paramref name="nowUtc"/> — trả về true nếu số sự kiện còn trong cửa sổ <c>window</c> đã đạt/vượt ngưỡng.</summary>
    public bool RecordEventAndCheckThreshold(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            _timestamps.Enqueue(nowUtc);
            PruneExpired(nowUtc);
            return _timestamps.Count >= thresholdCount;
        }
    }

    private void PruneExpired(DateTimeOffset nowUtc)
    {
        while (_timestamps.Count > 0 && nowUtc - _timestamps.Peek() > window)
        {
            _timestamps.Dequeue();
        }
    }
}

using ParentalGuard.Service.Pause;

namespace ParentalGuard.Service.Tests;

/// <summary>`PAUSE-021` (ĐÃ CHỐT v0.2.2, ngưỡng &gt; 5 lần/ngày) — đếm theo ngày lịch UTC từ <c>audit.log</c>.</summary>
public class PauseFrequencyGuardTests : IDisposable
{
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-freq-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task CountPauseActivatedTodayAsync_FileMissing_ReturnsZero()
    {
        (int count, _) = await PauseFrequencyGuard.CountPauseActivatedTodayAsync(_auditLogPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountPauseActivatedTodayAsync_CountsOnlyPauseActivatedOnSameUtcDay()
    {
        long today = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long stillToday = new DateTimeOffset(2026, 9, 20, 23, 59, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long yesterday = new DateTimeOffset(2026, 9, 19, 23, 59, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        await File.WriteAllLinesAsync(_auditLogPath,
        [
            Line(1, today, "PauseActivated"),
            Line(2, stillToday, "PauseActivated"),
            Line(3, yesterday, "PauseActivated"), // ngày khác — không tính
            Line(4, today, "PauseResumed"), // event_type khác — không tính
        ]);

        (int count, _) = await PauseFrequencyGuard.CountPauseActivatedTodayAsync(_auditLogPath, today, CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountPauseActivatedTodayAsync_ExceedsThreshold_When6OrMorePauseActivatedToday()
    {
        long today = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        string[] lines = Enumerable.Range(1, 6).Select(i => Line(i, today, "PauseActivated")).ToArray();
        await File.WriteAllLinesAsync(_auditLogPath, lines);

        (int count, _) = await PauseFrequencyGuard.CountPauseActivatedTodayAsync(_auditLogPath, today, CancellationToken.None);

        Assert.True(count > PauseFrequencyGuard.DailyThreshold);
    }

    [Fact]
    public async Task CountPauseActivatedTodayAsync_MalformedLine_IsSkippedNotThrown()
    {
        long today = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        await File.WriteAllLinesAsync(_auditLogPath, ["not-json-at-all", Line(1, today, "PauseActivated")]);

        (int count, _) = await PauseFrequencyGuard.CountPauseActivatedTodayAsync(_auditLogPath, today, CancellationToken.None);

        Assert.Equal(1, count);
    }

    private static string Line(long seq, long tsUnixMs, string eventType) =>
        $$"""{"seq": {{seq}}, "ts_unix_ms": {{tsUnixMs}}, "chain_id": "test", "event_type": "{{eventType}}", "detail": {}, "prev_hash": "0", "hash": "h{{seq}}"}""";

    public void Dispose() => File.Delete(_auditLogPath);
}

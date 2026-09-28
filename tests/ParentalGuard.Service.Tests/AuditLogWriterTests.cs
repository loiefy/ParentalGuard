using ParentalGuard.Service.Audit;

namespace ParentalGuard.Service.Tests;

public class AuditLogWriterTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"pg-audit-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task InitializeAsync_OnMissingFile_CreatesGenesisAndServiceStarted()
    {
        AuditLogWriter writer = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);

        Assert.True(File.Exists(_logPath));
        Assert.Equal(1, writer.Checkpoint.LastSeq);
        string[] lines = await File.ReadAllLinesAsync(_logPath);
        Assert.Single(lines);
        Assert.Contains("\"event_type\":\"ServiceStarted\"", lines[0]);
    }

    [Fact]
    public async Task AppendAsync_IncrementsSeqAndChainsHash()
    {
        AuditLogWriter writer = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        string firstHash = writer.Checkpoint.LastHash;

        await writer.AppendAsync("ConfigChanged", new { field = "risk_threshold" }, CancellationToken.None);

        Assert.Equal(2, writer.Checkpoint.LastSeq);
        Assert.NotEqual(firstHash, writer.Checkpoint.LastHash);
    }

    [Fact]
    public async Task Reinitialize_OnUntamperedFile_DoesNotAppendBrokenChainRecord()
    {
        AuditLogWriter first = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        await first.AppendAsync("MonitoringToggled", new { enabled = true }, CancellationToken.None);
        await first.AppendAsync("MonitoringToggled", new { enabled = false }, CancellationToken.None);

        AuditLogWriter second = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);

        string[] lines = await File.ReadAllLinesAsync(_logPath);
        Assert.DoesNotContain(lines, l => l.Contains("AuditChainBrokenDetected", StringComparison.Ordinal));
        Assert.Equal(first.Checkpoint.ChainId, second.Checkpoint.ChainId);
    }

    [Fact]
    public async Task Reinitialize_OnTamperedRecord_AppendsAuditChainBrokenDetectedWithNewChain()
    {
        AuditLogWriter first = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        await first.AppendAsync("MonitoringToggled", new { enabled = true }, CancellationToken.None);
        string originalChainId = first.Checkpoint.ChainId;

        string[] lines = await File.ReadAllLinesAsync(_logPath);
        lines[0] = lines[0].Replace("ServiceStarted", "ServiceStartedTampered", StringComparison.Ordinal);
        await File.WriteAllLinesAsync(_logPath, lines);

        AuditLogWriter second = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);

        string[] linesAfter = await File.ReadAllLinesAsync(_logPath);
        Assert.Contains(linesAfter, l => l.Contains("AuditChainBrokenDetected", StringComparison.Ordinal));
        Assert.NotEqual(originalChainId, second.Checkpoint.ChainId);
    }

    /// <summary>Bug 4 regression (test-runner): <c>AppendAsync</c> không còn nhận tham số <c>path</c> riêng — luôn ghi vào đúng path đã dùng lúc <see cref="AuditLogWriter.InitializeAsync"/>, loại bỏ hẳn khả năng lệch sang path khác (production) do caller truyền nhầm.</summary>
    [Fact]
    public async Task AppendAsync_AlwaysWritesToPathUsedAtInitialize_NeverAnyOtherPath()
    {
        string otherPath = Path.Combine(Path.GetTempPath(), $"pg-audit-other-{Guid.NewGuid():N}.log");
        AuditLogWriter writer = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);

        await writer.AppendAsync("ConfigChanged", new { field = "x" }, CancellationToken.None);

        Assert.True(File.Exists(_logPath));
        Assert.False(File.Exists(otherPath));
    }

    /// <summary>`10-ui-architecture.md` mục 6.3 (gap fix Đợt 7) — mới nhất trước, `has_more` đúng khi còn trang kế tiếp.</summary>
    [Fact]
    public async Task ReadPageAsync_ReturnsNewestFirst_WithHasMore()
    {
        AuditLogWriter writer = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None); // seq=1 ServiceStarted
        await writer.AppendAsync("MonitoringToggled", new { enabled = true }, CancellationToken.None); // seq=2
        await writer.AppendAsync("MonitoringToggled", new { enabled = false }, CancellationToken.None); // seq=3

        (IReadOnlyList<AuditLogEntryRaw> page0, bool hasMore0) = await writer.ReadPageAsync(page: 0, pageSize: 2, CancellationToken.None);
        Assert.Equal(2, page0.Count);
        Assert.Equal(3, page0[0].Seq); // mới nhất trước
        Assert.Equal(2, page0[1].Seq);
        Assert.True(hasMore0);

        (IReadOnlyList<AuditLogEntryRaw> page1, bool hasMore1) = await writer.ReadPageAsync(page: 1, pageSize: 2, CancellationToken.None);
        Assert.Single(page1);
        Assert.Equal(1, page1[0].Seq);
        Assert.False(hasMore1);
    }

    [Fact]
    public async Task ReadPageAsync_ContentBlockedEvent_ExtractsProcessNameAndRiskScore()
    {
        AuditLogWriter writer = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        await writer.AppendAsync("ContentBlocked", new { windowHandle = 123456L, processName = "chrome.exe", riskScore = 0.87f, bbox = new { x = 0, y = 0, width = 10, height = 10 } }, CancellationToken.None);

        (IReadOnlyList<AuditLogEntryRaw> page, _) = await writer.ReadPageAsync(page: 0, pageSize: 10, CancellationToken.None);

        AuditLogEntryRaw entry = Assert.Single(page, e => e.EventType == "ContentBlocked");
        Assert.Equal("chrome.exe", entry.ProcessName);
        Assert.Equal(0.87f, entry.RiskScore, precision: 2);
    }

    [Fact]
    public async Task ReadPageAsync_NonContentBlockedEvent_LeavesProcessNameEmpty()
    {
        AuditLogWriter writer = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);

        (IReadOnlyList<AuditLogEntryRaw> page, _) = await writer.ReadPageAsync(page: 0, pageSize: 10, CancellationToken.None);

        AuditLogEntryRaw entry = Assert.Single(page);
        Assert.Equal("ServiceStarted", entry.EventType);
        Assert.Equal("", entry.ProcessName);
    }

    public void Dispose()
    {
        if (File.Exists(_logPath))
        {
            File.Delete(_logPath);
        }
    }
}

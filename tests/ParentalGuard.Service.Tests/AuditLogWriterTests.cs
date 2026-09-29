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

    /// <summary>
    /// Audit fix 2026-09-29 (FAIL cứng do test-runner phát hiện): bản gốc <c>VerifyFullChainAsync</c> bail
    /// tuyến tính ở bất thường ĐẦU TIÊN của cả file — sau 1 tamper cũ đã "phục hồi" (đoạn chain mới), MỘT
    /// tamper ĐỘC LẬP thứ 2 xảy ra SAU đó trong đoạn chain mới không bao giờ được phát hiện (verify mãi mãi
    /// báo lại đúng điểm đứt CŨ). Test này PHẢI FAIL nếu ai đó lỡ revert <see cref="AuditLogWriter.VerifySegments"/>
    /// về vòng quét tuyến tính bail-sớm cũ.
    /// </summary>
    [Fact]
    public async Task VerifyFullChainAsync_SecondIndependentTamperInNewSegment_IsDetectedAndAppendsSecondBrokenRecord()
    {
        AuditLogWriter writer = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None); // chain1 seq=1 ServiceStarted

        // Tamper thứ 1 — chain1 seq=1.
        string[] lines1 = await File.ReadAllLinesAsync(_logPath);
        lines1[0] = lines1[0].Replace("ServiceStarted", "ServiceStartedTampered", StringComparison.Ordinal);
        await File.WriteAllLinesAsync(_logPath, lines1);

        AuditChainVerifyResult firstVerify = await writer.VerifyFullChainAsync(CancellationToken.None);
        Assert.False(firstVerify.IsIntact);
        string[] afterFirst = await File.ReadAllLinesAsync(_logPath);
        Assert.Single(afterFirst, l => l.Contains("AuditChainBrokenDetected", StringComparison.Ordinal));

        // Ghi 2 record hợp lệ vào chain2 (đoạn mới do AuditChainBrokenDetected vừa tạo).
        await writer.AppendAsync("MonitoringToggled", new { enabled = true }, CancellationToken.None); // chain2 seq=2
        await writer.AppendAsync("MonitoringToggled", new { enabled = false }, CancellationToken.None); // chain2 seq=3

        // Tamper ĐỘC LẬP thứ 2 — 1 record khác nằm SÂU trong chain2 (không phải seq=1 của chain2).
        string[] lines2 = await File.ReadAllLinesAsync(_logPath);
        int chain2SecondRecordIndex = Array.FindIndex(lines2, l => l.Contains("\"seq\":2", StringComparison.Ordinal) && l.Contains("MonitoringToggled", StringComparison.Ordinal));
        Assert.True(chain2SecondRecordIndex >= 0, "Không tìm thấy record chain2 seq=2 để tamper — fixture sai.");
        lines2[chain2SecondRecordIndex] = lines2[chain2SecondRecordIndex].Replace("\"enabled\":true", "\"enabled\":false", StringComparison.Ordinal);
        await File.WriteAllLinesAsync(_logPath, lines2);

        AuditChainVerifyResult secondVerify = await writer.VerifyFullChainAsync(CancellationToken.None);

        Assert.False(secondVerify.IsIntact);
        string[] afterSecond = await File.ReadAllLinesAsync(_logPath);
        // Phải có 2 AuditChainBrokenDetected riêng biệt — 1 cho chain1 (cũ, không lặp lại), 1 MỚI cho tamper độc lập vừa phát hiện ở chain2.
        Assert.Equal(2, afterSecond.Count(l => l.Contains("AuditChainBrokenDetected", StringComparison.Ordinal)));
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

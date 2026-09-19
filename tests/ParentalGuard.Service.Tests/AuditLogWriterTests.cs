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

    public void Dispose()
    {
        if (File.Exists(_logPath))
        {
            File.Delete(_logPath);
        }
    }
}

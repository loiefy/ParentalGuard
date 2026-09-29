using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

/// <summary>ADR-137 (Architecture/04-data-architecture.md mục 5.1) — <c>event_type="ContentBlocked"</c> ghi đúng 1 lần/lượt block (edge-triggered), không lặp lại khi cùng cửa sổ tiếp tục vi phạm.</summary>
public class OverlayDecisionCoordinatorContentBlockedTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"pg-audit-contentblocked-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task HandleVisionResultAsync_FirstViolation_AppendsContentBlockedWithProcessNameAndRiskScore()
    {
        (OverlayDecisionCoordinator coordinator, AuditLogWriter auditLog) = await CreateAsync();

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f, processName: "chrome.exe");

        string content = await File.ReadAllTextAsync(_logPath);
        Assert.Contains("\"event_type\":\"ContentBlocked\"", content);
        Assert.Contains("\"processName\":\"chrome.exe\"", content);
    }

    [Fact]
    public async Task HandleVisionResultAsync_SameWindowKeepsViolating_DoesNotAppendContentBlockedAgain()
    {
        (OverlayDecisionCoordinator coordinator, AuditLogWriter _) = await CreateAsync();

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f, processName: "chrome.exe", bboxX: 0);
        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.95f, processName: "chrome.exe", bboxX: 100); // bbox đổi (di chuyển/resize) — vẫn cùng 1 lượt block

        string content = await File.ReadAllTextAsync(_logPath);
        int count = CountOccurrences(content, "\"event_type\":\"ContentBlocked\"");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task HandleVisionResultAsync_ViolationClearedThenReoccurs_AppendsContentBlockedTwice()
    {
        (OverlayDecisionCoordinator coordinator, AuditLogWriter _) = await CreateAsync();

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f, processName: "chrome.exe");
        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.1f, processName: "chrome.exe"); // gỡ chặn
        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f, processName: "chrome.exe"); // vi phạm lại — lượt block MỚI

        string content = await File.ReadAllTextAsync(_logPath);
        Assert.Equal(2, CountOccurrences(content, "\"event_type\":\"ContentBlocked\""));
    }

    [Fact]
    public async Task HandleVisionResultAsync_NoBboxSet_DoesNotThrow_WritesZeroedBbox()
    {
        (OverlayDecisionCoordinator coordinator, AuditLogWriter _) = await CreateAsync();

        var message = new IpcPayload { VisionResult = new VisionInferenceResult { WindowHandle = 7, MonitorId = 1, RiskScore = 0.9f, ProcessName = "notepad.exe" } };
        await coordinator.HandleVisionResultAsync(message, CancellationToken.None);

        string content = await File.ReadAllTextAsync(_logPath);
        Assert.Contains("\"event_type\":\"ContentBlocked\"", content);
    }

    private async Task<(OverlayDecisionCoordinator Coordinator, AuditLogWriter AuditLog)> CreateAsync()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        var supervisor = new ChildProcessSupervisor(
            ProcessType.Overlay, "test-pipe", "test.exe", TimeSpan.FromSeconds(2),
            initialPushBuilders: null, hmacKey: new byte[32], auditLog, NullLogger.Instance);
        return (new OverlayDecisionCoordinator(supervisor, auditLog, () => 0.7f), auditLog);
    }

    private static Task ReportAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle, float riskScore, string processName, int bboxX = 0)
    {
        var message = new IpcPayload
        {
            VisionResult = new VisionInferenceResult
            {
                WindowHandle = windowHandle,
                MonitorId = 1,
                RiskScore = riskScore,
                ProcessName = processName,
                Bbox = new Rect { X = bboxX, Y = 0, Width = 100, Height = 100 },
            },
        };
        return coordinator.HandleVisionResultAsync(message, CancellationToken.None);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_logPath);
        }
        catch (IOException)
        {
        }
    }
}

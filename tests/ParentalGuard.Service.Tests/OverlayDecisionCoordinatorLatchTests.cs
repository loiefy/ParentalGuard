using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// `BE-034`/`034b` (ĐÃ CHỐT 2026-09-30) — regression bug real-hardware: Desktop Duplication chụp cả
/// overlay, khung hình kế tiếp điểm thấp → overlay bị gỡ → nội dung lộ → phát hiện lại (nhấp nháy).
/// Overlay giờ khoá cứng, chỉ gỡ qua ForceCloseRequest (nút/auto-timeout/cửa sổ biến mất) hoặc Pause.
/// </summary>
public class OverlayDecisionCoordinatorLatchTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"pg-audit-latch-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task LowScoreAfterViolation_DoesNotRemoveOverlay()
    {
        (OverlayDecisionCoordinator coordinator, _) = await CreateAsync();

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f);
        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.05f); // khung hình chỉ còn thấy chính overlay

        Assert.Equal([42UL], Push(coordinator).Rects.Select(r => r.WindowHandle));
        Assert.Equal([42UL], coordinator.CoveredWindowHandles);
    }

    [Fact]
    public async Task ResultsForCoveredWindow_AreIgnored_RectKeepsFirstValue()
    {
        (OverlayDecisionCoordinator coordinator, _) = await CreateAsync();

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f, bboxX: 10);
        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.95f, bboxX: 500); // kết quả tới muộn — bỏ qua

        Assert.Equal(10, Push(coordinator).Rects.Single().Rect.X);
    }

    [Theory]
    [InlineData(CloseSource.Manual)]
    [InlineData(CloseSource.AutoTimeout)]
    [InlineData(CloseSource.WindowGone)]
    public async Task ForceClose_AnySource_ReleasesOverlayAndCoveredList(CloseSource source)
    {
        int notifications = 0;
        (OverlayDecisionCoordinator coordinator, _) = await CreateAsync(() => notifications++);

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f);
        await ForceCloseAsync(coordinator, windowHandle: 42, source);

        Assert.Empty(Push(coordinator).Rects);
        Assert.Empty(coordinator.CoveredWindowHandles);
        Assert.Equal(2, notifications); // 1 lúc che + 1 lúc gỡ — mỗi lần đổi đều đẩy lại ControlVisionCommand
    }

    [Fact]
    public async Task WindowGone_DoesNotWriteForceCloseAudit_ButManualAndTimeoutDo()
    {
        (OverlayDecisionCoordinator coordinator, _) = await CreateAsync();

        await ReportAsync(coordinator, windowHandle: 1, riskScore: 0.9f);
        await ReportAsync(coordinator, windowHandle: 2, riskScore: 0.9f);
        await ReportAsync(coordinator, windowHandle: 3, riskScore: 0.9f);
        await ForceCloseAsync(coordinator, windowHandle: 1, CloseSource.WindowGone);
        await ForceCloseAsync(coordinator, windowHandle: 2, CloseSource.Manual);
        await ForceCloseAsync(coordinator, windowHandle: 3, CloseSource.AutoTimeout);

        string content = await File.ReadAllTextAsync(_logPath);
        Assert.Contains("\"source\":\"manual\"", content);
        Assert.Contains("\"source\":\"auto-timeout\"", content);
        Assert.Equal(2, content.Split("\"event_type\":\"ForceCloseRequested\"").Length - 1);
    }

    [Fact]
    public async Task ClearForPause_ReleasesAllAndNotifiesVision()
    {
        int notifications = 0;
        (OverlayDecisionCoordinator coordinator, _) = await CreateAsync(() => notifications++);

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f);
        coordinator.ClearForPause();

        Assert.Empty(coordinator.CoveredWindowHandles);
        Assert.Equal(2, notifications);
    }

    /// <summary>`BE-034c` (ĐÃ CHỐT 2026-10-01): cửa sổ không đóng được sau auto-timeout/nút → vi phạm lại thì blur lại, overlay mới (đếm 60s mới).</summary>
    [Theory]
    [InlineData(CloseSource.AutoTimeout)]
    [InlineData(CloseSource.Manual)]
    public async Task WindowStillViolatingAfterForceClose_IsCoveredAgainWithNewOverlay(CloseSource source)
    {
        (OverlayDecisionCoordinator coordinator, _) = await CreateAsync();
        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f);
        uint firstOverlayId = Assert.Single(Push(coordinator).Rects).OverlayId;

        await ForceCloseAsync(coordinator, windowHandle: 42, source);
        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.9f);

        OverlayRect again = Assert.Single(Push(coordinator).Rects);
        Assert.Equal(42UL, again.WindowHandle);
        Assert.NotEqual(firstOverlayId, again.OverlayId);
        Assert.Equal([42UL], coordinator.CoveredWindowHandles);
    }

    [Fact]
    public async Task BelowThresholdWhenNotCovered_DoesNothing()
    {
        int notifications = 0;
        (OverlayDecisionCoordinator coordinator, _) = await CreateAsync(() => notifications++);

        await ReportAsync(coordinator, windowHandle: 42, riskScore: 0.1f);

        Assert.Empty(Push(coordinator).Rects);
        Assert.Equal(0, notifications);
    }

    private async Task<(OverlayDecisionCoordinator Coordinator, AuditLogWriter AuditLog)> CreateAsync(Action? onCoveredWindowsChanged = null)
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        var supervisor = new ChildProcessSupervisor(
            ProcessType.Overlay, "test-pipe", "test.exe", TimeSpan.FromSeconds(2),
            initialPushBuilders: null, hmacKey: new byte[32], auditLog, NullLogger.Instance);
        return (new OverlayDecisionCoordinator(supervisor, auditLog, () => 0.7f, onCoveredWindowsChanged), auditLog);
    }

    private static Task ReportAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle, float riskScore, int bboxX = 0) =>
        coordinator.HandleVisionResultAsync(
            new IpcPayload
            {
                VisionResult = new VisionInferenceResult
                {
                    WindowHandle = windowHandle,
                    MonitorId = 1,
                    RiskScore = riskScore,
                    Bbox = new Rect { X = bboxX, Y = 0, Width = 100, Height = 100 },
                },
            },
            CancellationToken.None);

    private static Task ForceCloseAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle, CloseSource source) =>
        coordinator.HandleForceCloseAsync(
            new IpcPayload { ForceClose = new ForceCloseRequest { WindowHandle = windowHandle, OverlayId = 1, Source = source } },
            CancellationToken.None);

    private static OverlayRectListCommand Push(OverlayDecisionCoordinator coordinator)
    {
        var payload = new IpcPayload();
        coordinator.ConfigureInitialPush(payload);
        return payload.OverlayRects;
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

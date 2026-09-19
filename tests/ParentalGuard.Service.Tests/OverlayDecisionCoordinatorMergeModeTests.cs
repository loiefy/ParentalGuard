using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

/// <summary>`BE-088`/`089` (Architecture/07-overlay-architecture.md mục 3.2) — ngưỡng hysteresis + gộp theo màn hình, quan sát qua <see cref="OverlayDecisionCoordinator.ConfigureInitialPush"/> (đúng con đường production dùng để đẩy state hiện hành).</summary>
public class OverlayDecisionCoordinatorMergeModeTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"pg-audit-merge-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task ConfigureInitialPush_At11ViolationsOnOneMonitor_ReturnsSingleMergedRectWithAllHandles()
    {
        OverlayDecisionCoordinator coordinator = await CreateCoordinatorAsync();

        for (ulong handle = 1; handle <= 11; handle++)
        {
            await ReportViolationAsync(coordinator, handle, monitorId: 1);
        }

        OverlayRectListCommand command = Push(coordinator);

        OverlayRect merged = Assert.Single(command.Rects);
        Assert.True(merged.IsMerged);
        Assert.Equal(11, merged.MergedWindowHandles.Count);
        Assert.Equal(1u, merged.MonitorId);
    }

    [Fact]
    public async Task ConfigureInitialPush_At10ViolationsOnOneMonitor_DoesNotMerge()
    {
        OverlayDecisionCoordinator coordinator = await CreateCoordinatorAsync();

        for (ulong handle = 1; handle <= 10; handle++)
        {
            await ReportViolationAsync(coordinator, handle, monitorId: 1);
        }

        OverlayRectListCommand command = Push(coordinator);

        Assert.Equal(10, command.Rects.Count);
        Assert.All(command.Rects, r => Assert.False(r.IsMerged));
    }

    [Fact]
    public async Task ConfigureInitialPush_SplitAcrossTwoMonitors_ReturnsOneMergedRectPerMonitorWithFullSystemHandleList()
    {
        OverlayDecisionCoordinator coordinator = await CreateCoordinatorAsync();

        for (ulong handle = 1; handle <= 6; handle++)
        {
            await ReportViolationAsync(coordinator, handle, monitorId: 1);
        }

        for (ulong handle = 101; handle <= 106; handle++)
        {
            await ReportViolationAsync(coordinator, handle, monitorId: 2);
        }

        OverlayRectListCommand command = Push(coordinator);

        Assert.Equal(2, command.Rects.Count);
        Assert.All(command.Rects, r => Assert.True(r.IsMerged));
        Assert.All(command.Rects, r => Assert.Equal(12, r.MergedWindowHandles.Count)); // BE-089: TOÀN BỘ handle hệ thống, không chỉ riêng màn hình
        Assert.Contains(command.Rects, r => r.MonitorId == 1);
        Assert.Contains(command.Rects, r => r.MonitorId == 2);
    }

    [Fact]
    public async Task ConfigureInitialPush_HysteresisExit_StaysMergedAt9ThenExitsAt8()
    {
        OverlayDecisionCoordinator coordinator = await CreateCoordinatorAsync();
        for (ulong handle = 1; handle <= 11; handle++)
        {
            await ReportViolationAsync(coordinator, handle, monitorId: 1);
        }

        Assert.True(Push(coordinator).Rects.Single().IsMerged);

        await ClearViolationAsync(coordinator, windowHandle: 11); // còn 10
        await ClearViolationAsync(coordinator, windowHandle: 10); // còn 9 — ADR-57: vẫn PHẢI còn gộp (ngưỡng ra = 8)
        Assert.True(Push(coordinator).Rects.Single().IsMerged);

        await ClearViolationAsync(coordinator, windowHandle: 9); // còn 8 — chạm ngưỡng ra, thoát gộp
        OverlayRectListCommand exited = Push(coordinator);
        Assert.Equal(8, exited.Rects.Count);
        Assert.All(exited.Rects, r => Assert.False(r.IsMerged));
    }

    private async Task<OverlayDecisionCoordinator> CreateCoordinatorAsync()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        var supervisor = new ChildProcessSupervisor(
            ProcessType.Overlay, "test-pipe", "test.exe", TimeSpan.FromSeconds(2),
            initialPushBuilders: null, hmacKey: new byte[32], auditLog, NullLogger.Instance);
        return new OverlayDecisionCoordinator(supervisor, auditLog, () => 0.7f);
    }

    private static Task ReportViolationAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle, uint monitorId)
    {
        var message = new IpcPayload
        {
            VisionResult = new VisionInferenceResult { WindowHandle = windowHandle, MonitorId = monitorId, RiskScore = 0.9f },
        };
        return coordinator.HandleVisionResultAsync(message, CancellationToken.None);
    }

    private static Task ClearViolationAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle)
    {
        var message = new IpcPayload
        {
            VisionResult = new VisionInferenceResult { WindowHandle = windowHandle, MonitorId = 1, RiskScore = 0.1f },
        };
        return coordinator.HandleVisionResultAsync(message, CancellationToken.None);
    }

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

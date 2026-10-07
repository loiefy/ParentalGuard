using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

/// <summary>`BE-034d` (2026-10-07): "Tắt nội dung" buộc đóng được ứng dụng vi phạm, nhưng chỉ khi đủ điều kiện an toàn.</summary>
public class ForceKillTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"pg-audit-kill-{Guid.NewGuid():N}.log");

    [Fact]
    public void Policy_MatchingUserProcess_Allowed()
    {
        Assert.Equal(ForceKillDecision.Allowed, ForceKillPolicy.Decide("chrome.exe", TimeSpan.FromSeconds(3), new ProcessFacts(10, 1, "chrome.exe")));
    }

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("svchost.exe")]
    [InlineData("ParentalGuard.UI.exe")]
    public void Policy_ProtectedProcess_Refused(string exe)
    {
        Assert.Equal(ForceKillDecision.ProtectedProcess, ForceKillPolicy.Decide(exe, TimeSpan.FromSeconds(3), new ProcessFacts(10, 1, exe)));
    }

    [Fact]
    public void Policy_PidOfDifferentProcess_Refused()
    {
        Assert.Equal(ForceKillDecision.ProcessNameMismatch, ForceKillPolicy.Decide("chrome.exe", TimeSpan.FromSeconds(3), new ProcessFacts(10, 1, "notepad++.exe")));
    }

    [Fact]
    public void Policy_SessionZero_NoRecentClose_TooLate_Gone_Refused()
    {
        Assert.Equal(ForceKillDecision.SessionZero, ForceKillPolicy.Decide("x.exe", TimeSpan.FromSeconds(3), new ProcessFacts(10, 0, "x.exe")));
        Assert.Equal(ForceKillDecision.NoRecentManualClose, ForceKillPolicy.Decide(null, TimeSpan.FromSeconds(3), new ProcessFacts(10, 1, "x.exe")));
        Assert.Equal(ForceKillDecision.NoRecentManualClose, ForceKillPolicy.Decide("x.exe", TimeSpan.FromMinutes(5), new ProcessFacts(10, 1, "x.exe")));
        Assert.Equal(ForceKillDecision.ProcessGone, ForceKillPolicy.Decide("x.exe", TimeSpan.FromSeconds(3), null));
    }

    [Fact]
    public async Task Coordinator_ManualCloseThenKillRequest_KillsAndAudits()
    {
        var killed = new List<int>();
        OverlayDecisionCoordinator coordinator = await CreateAsync(pid => new ProcessFacts(pid, 1, "chrome.exe"), killed.Add);

        await ReportAsync(coordinator, 42, "chrome.exe");
        await ForceCloseAsync(coordinator, 42, CloseSource.Manual);
        await KillAsync(coordinator, 42, pid: 777);

        Assert.Equal([777], killed);
        Assert.Contains("\"event_type\":\"ForceKillExecuted\"", await File.ReadAllTextAsync(_logPath));
    }

    /// <summary>Auto-timeout không phải người bấm nút — không được buộc đóng (`BE-034d`).</summary>
    [Fact]
    public async Task Coordinator_AutoTimeoutClose_KillRequestRefused()
    {
        var killed = new List<int>();
        OverlayDecisionCoordinator coordinator = await CreateAsync(pid => new ProcessFacts(pid, 1, "chrome.exe"), killed.Add);

        await ReportAsync(coordinator, 42, "chrome.exe");
        await ForceCloseAsync(coordinator, 42, CloseSource.AutoTimeout);
        await KillAsync(coordinator, 42, pid: 777);

        Assert.Empty(killed);
        Assert.Contains("\"event_type\":\"ForceKillRefused\"", await File.ReadAllTextAsync(_logPath));
    }

    [Fact]
    public async Task Coordinator_KillRequestWithoutPriorClose_Refused()
    {
        var killed = new List<int>();
        OverlayDecisionCoordinator coordinator = await CreateAsync(pid => new ProcessFacts(pid, 1, "chrome.exe"), killed.Add);

        await KillAsync(coordinator, 42, pid: 777);

        Assert.Empty(killed);
    }

    private async Task<OverlayDecisionCoordinator> CreateAsync(Func<int, ProcessFacts?> lookup, Action<int> kill)
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_logPath, CancellationToken.None);
        var supervisor = new ChildProcessSupervisor(
            ProcessType.Overlay, "test-pipe", "test.exe", TimeSpan.FromSeconds(2),
            initialPushBuilders: null, hmacKey: new byte[32], auditLog, NullLogger.Instance);
        return new OverlayDecisionCoordinator(supervisor, auditLog, () => 0.7f, lookupProcess: lookup, killProcess: kill);
    }

    private static Task ReportAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle, string processName) =>
        coordinator.HandleVisionResultAsync(
            new IpcPayload
            {
                VisionResult = new VisionInferenceResult
                {
                    WindowHandle = windowHandle,
                    MonitorId = 1,
                    RiskScore = 0.9f,
                    ProcessName = processName,
                    Bbox = new Rect { X = 0, Y = 0, Width = 100, Height = 100 },
                },
            },
            CancellationToken.None);

    private static Task ForceCloseAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle, CloseSource source) =>
        coordinator.HandleForceCloseAsync(new IpcPayload { ForceClose = new ForceCloseRequest { WindowHandle = windowHandle, Source = source } }, CancellationToken.None);

    private static Task KillAsync(OverlayDecisionCoordinator coordinator, ulong windowHandle, uint pid) =>
        coordinator.HandleForceKillAsync(new IpcPayload { ForceKill = new ForceKillRequest { WindowHandle = windowHandle, ProcessId = pid } }, CancellationToken.None);

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

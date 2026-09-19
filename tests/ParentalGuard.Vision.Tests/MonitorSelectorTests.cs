using ParentalGuard.Vision.Capture;

namespace ParentalGuard.Vision.Tests;

/// <summary>ADR-63 — chỉ test phần thuần (khớp HMONITOR với danh sách output), không cần DXGI thật.</summary>
public class MonitorSelectorTests
{
    [Fact]
    public void MatchOutputByMonitor_FindsMatchingOutput()
    {
        MonitorSelector.OutputInfo[] outputs =
        [
            new(AdapterIndex: 0, OutputIndex: 0, Monitor: new IntPtr(101)),
            new(AdapterIndex: 0, OutputIndex: 1, Monitor: new IntPtr(102)),
        ];

        MonitorSelector.OutputInfo? result = MonitorSelector.MatchOutputByMonitor(outputs, new IntPtr(102));

        Assert.Equal(1, result!.Value.OutputIndex);
    }

    [Fact]
    public void MatchOutputByMonitor_NoMatch_ReturnsNull()
    {
        MonitorSelector.OutputInfo[] outputs = [new(0, 0, new IntPtr(101))];

        Assert.Null(MonitorSelector.MatchOutputByMonitor(outputs, new IntPtr(999)));
    }

    [Fact]
    public void MatchOutputByMonitor_ZeroMonitor_ReturnsNull()
    {
        MonitorSelector.OutputInfo[] outputs = [new(0, 0, new IntPtr(101))];

        Assert.Null(MonitorSelector.MatchOutputByMonitor(outputs, IntPtr.Zero));
    }
}

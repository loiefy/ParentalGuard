using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>`BE-073c` (2026-10-12): chỉ khay biểu tượng ẩn của explorer.exe được bỏ qua — File Explorer, taskbar, xem trước vẫn giám sát.</summary>
public sealed class ShellUiWindowFilterTests
{
    [Theory]
    [InlineData("explorer.exe", "TopLevelWindowForOverflowXamlIsland", true)] // Win11 khay biểu tượng ẩn
    [InlineData("EXPLORER.EXE", "NotifyIconOverflowWindow", true)]            // Win10
    [InlineData("explorer.exe", "CabinetWClass", false)]                      // cửa sổ File Explorer — có thumbnail ảnh
    [InlineData("explorer.exe", "TaskListThumbnailWnd", false)]               // xem trước cửa sổ trên taskbar
    [InlineData("explorer.exe", "Shell_TrayWnd", false)]
    [InlineData("chrome.exe", "TopLevelWindowForOverflowXamlIsland", false)]  // giả tên lớp ở tiến trình khác
    [InlineData("explorer.exe", null, false)]
    public void OnlyExplorerTrayOverflowIsExcluded(string process, string? className, bool expected) =>
        Assert.Equal(expected, ShellUiWindowFilter.IsExcludedShellWindow(process, className));
}

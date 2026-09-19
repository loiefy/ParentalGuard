using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

public class ExcludeProcessMatcherTests
{
    private static readonly string[] _excludeList = ["Taskmgr.exe", "notepad.exe"];

    [Theory]
    [InlineData("Taskmgr.exe")]
    [InlineData("TASKMGR.EXE")]
    [InlineData("taskmgr.exe")]
    public void IsExcluded_MatchesCaseInsensitive(string processName)
    {
        Assert.True(ExcludeProcessMatcher.IsExcluded(processName, _excludeList));
    }

    [Fact]
    public void IsExcluded_NotInList_ReturnsFalse()
    {
        Assert.False(ExcludeProcessMatcher.IsExcluded("chrome.exe", _excludeList));
    }

    [Fact]
    public void IsExcluded_NullProcessName_ReturnsFalse()
    {
        Assert.False(ExcludeProcessMatcher.IsExcluded(null, _excludeList));
    }

    [Fact]
    public void IsExcluded_EmptyExcludeList_ReturnsFalse()
    {
        Assert.False(ExcludeProcessMatcher.IsExcluded("Taskmgr.exe", []));
    }

    [Fact]
    public void IsExcluded_NeverExcludesExplorerByDefault_NotInBe073aStaticList()
    {
        // BE-073a: File Explorer KHÔNG được đưa vào danh sách tĩnh (có thể hiển thị ảnh/video qua preview) —
        // guard test này chỉ khẳng định hành vi của matcher (không tự loại trừ gì ngoài danh sách nhận vào),
        // danh sách khởi điểm thật nằm ở MonitoringStateData.InitialExcludeProcessNames (Service).
        Assert.False(ExcludeProcessMatcher.IsExcluded("explorer.exe", _excludeList));
    }
}

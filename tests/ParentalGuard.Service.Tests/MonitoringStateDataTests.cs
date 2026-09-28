using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

public class MonitoringStateDataTests
{
    // BE-073a: các ứng dụng có khả năng hiển thị ảnh/video (dù trông giống tiện ích hệ thống)
    // TUYỆT ĐỐI không được lọt vào danh sách loại trừ tĩnh — regression guard.
    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("SystemSettings.exe")]
    [InlineData("SearchHost.exe")]
    public void InitialExcludeProcessNames_NeverContainsAppsThatCanShowImages(string forbiddenProcessName)
    {
        Assert.DoesNotContain(MonitoringStateData.InitialExcludeProcessNames, p => string.Equals(p, forbiddenProcessName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateFailSecureDefault_UsesEmptyExcludeList_NotInitialList()
    {
        MonitoringStateData state = MonitoringStateData.CreateFailSecureDefault();

        Assert.Empty(state.ExcludeProcessNames);
        Assert.True(state.MonitoringEnabled);
    }

    /// <summary>`04-data-architecture.md` mục 3.3/6.2 — `user_whitelisted_process_names`/`overlay_message` cùng chịu nguyên tắc fail-secure `ANTI-070` như `exclude_process_names`.</summary>
    [Fact]
    public void CreateFailSecureDefault_ResetsWhitelistAndOverlayMessageToSafeDefaults()
    {
        MonitoringStateData state = MonitoringStateData.CreateFailSecureDefault();

        Assert.Empty(state.UserWhitelistedProcessNames);
        Assert.Equal("", state.OverlayMessage);
    }

    [Fact]
    public void CreateFirstRunDefault_MonitoringEnabledByDefault()
    {
        MonitoringStateData state = MonitoringStateData.CreateFirstRunDefault();

        Assert.True(state.MonitoringEnabled);
        Assert.Equal(MonitoringStateData.InitialExcludeProcessNames, state.ExcludeProcessNames);
    }
}

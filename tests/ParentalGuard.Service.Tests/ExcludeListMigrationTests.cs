using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

/// <summary>`BE-073b` (2026-10-12): bộ gõ tiếng Việt vào exclude-list — máy mới có sẵn, máy đã cài nhận thêm đúng 1 lần.</summary>
public sealed class ExcludeListMigrationTests
{
    [Fact]
    public void FirstRun_ContainsVietnameseInputMethods_AndIsCurrentRevision()
    {
        MonitoringStateData state = MonitoringStateData.CreateFirstRunDefault();

        Assert.Contains("UniKeyNT.exe", state.ExcludeProcessNames);
        Assert.Contains("Taskmgr.exe", state.ExcludeProcessNames);
        Assert.Equal(MonitoringStateData.CurrentExcludeListRevision, state.ExcludeListRevision);
        Assert.Null(state.MigrateExcludeList());
    }

    [Fact]
    public void OldInstall_GetsAdditionsOnce_KeepsUserRemovals_NoDuplicates()
    {
        // Máy cài từ trước: chưa có trường revision, phụ huynh đã xoá notepad.exe, có sẵn "unikeynt.exe" khác hoa/thường.
        MonitoringStateData old = MonitoringStateData.CreateFirstRunDefault() with
        {
            ExcludeProcessNames = ["Taskmgr.exe", "unikeynt.exe"],
            ExcludeListRevision = 0,
        };

        MonitoringStateData migrated = old.MigrateExcludeList()!;

        Assert.Contains("EVKey64.exe", migrated.ExcludeProcessNames);
        Assert.Contains("notepad++.exe", migrated.ExcludeProcessNames);
        Assert.Contains("StartMenuExperienceHost.exe", migrated.ExcludeProcessNames);
        Assert.DoesNotContain("notepad.exe", migrated.ExcludeProcessNames);
        Assert.Single(migrated.ExcludeProcessNames, n => n.Equals("UniKeyNT.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(MonitoringStateData.CurrentExcludeListRevision, migrated.ExcludeListRevision);

        // Sau đó phụ huynh xoá UniKey → khởi động lại KHÔNG thêm lại.
        MonitoringStateData removed = migrated with { ExcludeProcessNames = [.. migrated.ExcludeProcessNames.Where(n => !n.StartsWith("UniKey", StringComparison.OrdinalIgnoreCase))] };
        Assert.Null(removed.MigrateExcludeList());
    }

    [Fact]
    public void InstallAtRevision1_GetsOnlyRevision2Additions_NotRemovedUniKeyAgain()
    {
        // Máy đã nhận lượt 1 rồi phụ huynh xoá UniKey — lượt 2 chỉ thêm Notepad++/Start, KHÔNG thêm lại UniKey.
        MonitoringStateData atRev1 = MonitoringStateData.CreateFirstRunDefault() with
        {
            ExcludeProcessNames = ["Taskmgr.exe", "EVKey64.exe"],
            ExcludeListRevision = 1,
        };

        MonitoringStateData migrated = atRev1.MigrateExcludeList()!;

        Assert.Equal(["Taskmgr.exe", "EVKey64.exe", "notepad++.exe", "StartMenuExperienceHost.exe"], migrated.ExcludeProcessNames);
        Assert.Equal(2, migrated.ExcludeListRevision);
    }

    [Fact]
    public void FailSecureConfig_IsNeverMigrated()
    {
        MonitoringStateData failSecure = MonitoringStateData.CreateFailSecureDefault();

        Assert.Empty(failSecure.ExcludeProcessNames);
        Assert.Null(failSecure.MigrateExcludeList());
    }
}

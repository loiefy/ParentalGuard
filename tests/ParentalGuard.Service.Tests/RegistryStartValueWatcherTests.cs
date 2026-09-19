using ParentalGuard.Ipc.Tamper;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// ANTI-031 (Architecture/09-anti-tamper-architecture.md mục 4.1/4.2) — test THẬT qua
/// <c>RegNotifyChangeKeyValue</c> dưới HKCU (không cần quyền SYSTEM, đúng overload test-only của
/// <see cref="RegistryStartValueWatcher"/>) để verify P/Invoke hoạt động đúng, không chỉ mock logic.
/// </summary>
public class RegistryStartValueWatcherTests : IDisposable
{
    private readonly string _testKeyPath = $@"Software\ParentalGuardTest\{Guid.NewGuid():N}";

    public RegistryStartValueWatcherTests()
    {
        int createStatus = RegistryTamperInterop.RegCreateKeyExForTest(
            RegistryTamperInterop.HKeyCurrentUserForTest, _testKeyPath, 0, IntPtr.Zero,
            RegistryTamperInterop.RegOptionNonVolatile, 0x1 | 0x2 | 0x10, IntPtr.Zero, out IntPtr hKey, out _);
        Assert.Equal(RegistryTamperInterop.ErrorSuccess, createStatus);

        uint initial = RegistryStartValueWatcher.ExpectedStartValue;
        RegistryTamperInterop.RegSetValueEx(hKey, "Start", 0, RegistryTamperInterop.RegDwordType, in initial, sizeof(uint));
        RegistryTamperInterop.RegCloseKey(hKey);
    }

    [Fact]
    public void Watcher_WhenStartValueTamperedExternally_SelfHealsAndReportsOldNewValue()
    {
        using var signaled = new ManualResetEventSlim(false);
        string? reportedKeyPath = null;
        uint reportedOld = 0;
        uint reportedNew = 0;

        using var watcher = new RegistryStartValueWatcher(RegistryTamperInterop.HKeyCurrentUserForTest, _testKeyPath, (keyPath, oldValue, newValue) =>
        {
            reportedKeyPath = keyPath;
            reportedOld = oldValue;
            reportedNew = newValue;
            signaled.Set();
        });
        watcher.Start();

        Thread.Sleep(200); // để thread nền kịp gọi RegNotifyChangeKeyValue lần đầu trước khi ta đổi giá trị.
        SetStartValueExternally(0x00000004); // SERVICE_DISABLED — mô phỏng tamper (mục 4.2).

        bool wasSignaled = signaled.Wait(TimeSpan.FromSeconds(10));

        Assert.True(wasSignaled, "RegistryStartValueWatcher did not detect the external change within 10s.");
        Assert.Equal(_testKeyPath, reportedKeyPath);
        Assert.Equal(0x00000004u, reportedOld);
        Assert.Equal(RegistryStartValueWatcher.ExpectedStartValue, reportedNew);
        Assert.Equal(RegistryStartValueWatcher.ExpectedStartValue, ReadStartValueExternally());
    }

    private void SetStartValueExternally(uint value)
    {
        RegistryTamperInterop.RegOpenKeyEx(RegistryTamperInterop.HKeyCurrentUserForTest, _testKeyPath, 0, 0x2, out IntPtr hKey);
        RegistryTamperInterop.RegSetValueEx(hKey, "Start", 0, RegistryTamperInterop.RegDwordType, in value, sizeof(uint));
        RegistryTamperInterop.RegCloseKey(hKey);
    }

    private uint ReadStartValueExternally()
    {
        RegistryTamperInterop.RegOpenKeyEx(RegistryTamperInterop.HKeyCurrentUserForTest, _testKeyPath, 0, 0x1, out IntPtr hKey);
        int size = sizeof(uint);
        RegistryTamperInterop.RegQueryValueEx(hKey, "Start", IntPtr.Zero, out _, out uint value, ref size);
        RegistryTamperInterop.RegCloseKey(hKey);
        return value;
    }

    public void Dispose()
    {
        RegistryTamperInterop.RegDeleteKeyForTest(RegistryTamperInterop.HKeyCurrentUserForTest, _testKeyPath);
    }
}

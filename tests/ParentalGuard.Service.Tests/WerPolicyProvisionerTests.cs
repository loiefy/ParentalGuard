using Microsoft.Win32;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// `SEC-020` (Architecture/06-security-architecture.md mục 6, ADR-140) — test THẬT qua registry dưới
/// HKCU (không cần quyền SYSTEM, đúng overload test-only của <see cref="WerPolicyProvisioner"/>, cùng
/// mẫu hình <c>RegistryStartValueWatcherTests</c>).
/// </summary>
public class WerPolicyProvisionerTests : IDisposable
{
    private const string _visionFileName = "ParentalGuard.Vision.exe";
    private readonly string _testRoot = $@"Software\ParentalGuardTest\{Guid.NewGuid():N}";

    [Fact]
    public void Apply_WritesExcludedApplicationsAndLocalDumpsMiniDumpType()
    {
        using RegistryKey baseKey = Registry.CurrentUser.CreateSubKey(_testRoot, writable: true);

        WerPolicyProvisioner.Apply(baseKey, _visionFileName);

        using RegistryKey? excluded = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\ExcludedApplications");
        Assert.Equal(1, excluded?.GetValue(_visionFileName));

        using RegistryKey? localDumps = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\{_visionFileName}");
        Assert.Equal(1, localDumps?.GetValue("DumpType")); // MiniDumpNormal, KHÔNG phải 2 (Full) — mục 6.1.
    }

    [Fact]
    public void Apply_CalledTwice_IsIdempotent_DoesNotThrowAndKeepsSameValue()
    {
        using RegistryKey baseKey = Registry.CurrentUser.CreateSubKey(_testRoot, writable: true);

        WerPolicyProvisioner.Apply(baseKey, _visionFileName);
        WerPolicyProvisioner.Apply(baseKey, _visionFileName); // idempotent (mục 6.2) — không ném lỗi, không ghi thừa.

        using RegistryKey? excluded = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\ExcludedApplications");
        Assert.Equal(1, excluded?.GetValue(_visionFileName));
    }

    [Fact]
    public void Apply_ExistingDifferentValue_OverwritesToExpected()
    {
        using RegistryKey baseKey = Registry.CurrentUser.CreateSubKey(_testRoot, writable: true);
        using (RegistryKey preExisting = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\ExcludedApplications", writable: true))
        {
            preExisting.SetValue(_visionFileName, 0, RegistryValueKind.DWord); // giá trị sai lệch (vd bị Group Policy/tool khác ghi đè)
        }

        WerPolicyProvisioner.Apply(baseKey, _visionFileName);

        using RegistryKey? excluded = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\ExcludedApplications");
        Assert.Equal(1, excluded?.GetValue(_visionFileName));
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_testRoot, throwOnMissingSubKey: false);
    }
}

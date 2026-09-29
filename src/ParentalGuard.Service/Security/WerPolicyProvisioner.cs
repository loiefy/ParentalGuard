using Microsoft.Win32;

namespace ParentalGuard.Service.Security;

/// <summary>
/// `SEC-020` (Architecture/06-security-architecture.md mục 6, ADR-140): loại
/// <c>ParentalGuard.Vision.exe</c> khỏi Windows Error Reporting — <c>ExcludedApplications</c> (chính,
/// tắt hẳn WER cho process này) + <c>LocalDumps\DumpType=1</c> (phụ, defense-in-depth — nếu
/// <c>ExcludedApplications</c> bị Group Policy máy đó ghi đè/vô hiệu, dump tạo ra tối thiểu là mini,
/// không phải full memory dump chứa frame ảnh). Idempotent — kiểm tra giá trị hiện tại trước khi ghi
/// lại (mục 6.2), <c>Worker</c> gọi best-effort mỗi lần <c>Starting</c>, cùng nhóm ACL/WFP.
///
/// Rủi ro dư ghi nhận minh bạch (mục 6.3, KHÔNG phải bug): Task Manager "Create dump file"/`procdump`
/// chủ động của 1 Administrator KHÔNG bị 2 registry key này chặn — chúng chỉ chi phối luồng WER TỰ ĐỘNG
/// khi crash không kiểm soát, đúng ranh giới `SEC-005` (Administrator là ranh giới tin cậy cao nhất).
/// </summary>
public static class WerPolicyProvisioner
{
    private const string _excludedApplicationsKeyPath = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\ExcludedApplications";
    private const string _localDumpsKeyPathFormat = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\{0}";
    private const string _dumpTypeValueName = "DumpType";
    private const int _miniDumpType = 1; // MiniDumpNormal — KHÔNG phải 2 (Full), mục 6.1.

    public static void Apply(string visionExecutableFileName) => Apply(Registry.LocalMachine, visionExecutableFileName);

    /// <summary>Overload chỉ dùng cho unit test (root hive tuỳ ý, vd HKCU — không cần quyền SYSTEM), cùng mẫu hình <c>RegistryStartValueWatcher</c>.</summary>
    internal static void Apply(RegistryKey baseKey, string visionExecutableFileName)
    {
        using RegistryKey excluded = baseKey.CreateSubKey(_excludedApplicationsKeyPath, writable: true);
        SetDwordIfDifferent(excluded, visionExecutableFileName, expected: 1);

        using RegistryKey localDumps = baseKey.CreateSubKey(string.Format(_localDumpsKeyPathFormat, visionExecutableFileName), writable: true);
        SetDwordIfDifferent(localDumps, _dumpTypeValueName, _miniDumpType);
    }

    private static void SetDwordIfDifferent(RegistryKey key, string valueName, int expected)
    {
        if (key.GetValue(valueName) is int current && current == expected)
        {
            return; // idempotent (mục 6.2) — đã đúng giá trị, không ghi lại mỗi lần Starting.
        }

        key.SetValue(valueName, expected, RegistryValueKind.DWord);
    }
}

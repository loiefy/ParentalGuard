using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Security;

[StructLayout(LayoutKind.Sequential)]
internal struct AuditPolicyInformation
{
    public Guid AuditSubCategoryGuid;
    public uint AuditingInformation;
    public Guid AuditCategoryGuid;
}

/// <summary>P/Invoke bật Windows Security Auditing cho subcategory "Filtering Platform Connection" (Architecture/06 mục 3.3).</summary>
internal static class AuditPolicyInterop
{
    internal const uint PolicyAuditEventFailure = 0x00020000;

    // Subcategory GUID chuẩn của Windows cho "Filtering Platform Connection" (Advanced Audit Policy).
    internal static readonly Guid FilteringPlatformConnectionSubCategory = new("0CCE9226-69AE-11D9-BED3-505054503030");

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AuditSetSystemPolicy(AuditPolicyInformation[] auditPolicy, uint policyCount);
}

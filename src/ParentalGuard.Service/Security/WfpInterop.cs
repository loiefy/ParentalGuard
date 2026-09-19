using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Security;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct FwpmDisplayData0
{
    public IntPtr Name;
    public IntPtr Description;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpByteBlob
{
    public uint Size;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmProvider0
{
    public Guid ProviderKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public FwpByteBlob ProviderData;
    public IntPtr ServiceName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmSubLayer0
{
    public Guid SubLayerKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public IntPtr ProviderKey; // GUID* — NULL nếu không gắn provider cụ thể
    public FwpByteBlob ProviderData;
    public ushort Weight;
}

/// <summary>Đại diện chung cho union giá trị của WFP (FWP_VALUE0/FWP_CONDITION_VALUE0) — 8 byte, đọc theo <see cref="Type"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FwpValue0
{
    public uint Type;
    public ulong UnionValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmFilterCondition0
{
    public Guid FieldKey;
    public uint MatchType;
    public FwpValue0 ConditionValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmAction0
{
    public uint Type;
    public Guid FilterOrCalloutKey;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmFilter0
{
    public Guid FilterKey;
    public FwpmDisplayData0 DisplayData;
    public uint Flags;
    public IntPtr ProviderKey; // GUID*
    public FwpByteBlob ProviderData;
    public Guid LayerKey;
    public Guid SubLayerKey;
    public FwpValue0 Weight;
    public uint NumFilterConditions;
    public IntPtr FilterCondition; // FWPM_FILTER_CONDITION0*
    public FwpmAction0 Action;
    public Guid ProviderContextKeyOrRawContext;
    public IntPtr Reserved; // GUID*
    public ulong FilterId; // out
    public FwpValue0 EffectiveWeight; // out
}

/// <summary>
/// P/Invoke Windows Filtering Platform (Fwpuclnt.dll) — Architecture/06-security-architecture.md
/// mục 3, ADR-32. Dùng API gốc (không Windows Firewall bậc cao) theo đúng nghĩa đen SEC-010.
/// </summary>
internal static class WfpInterop
{
    // FWPM_FILTER_FLAG_PERSISTENT / FWPM_PROVIDER_FLAG_PERSISTENT / FWPM_SUBLAYER_FLAG_PERSISTENT
    internal const uint FwpmPersistent = 0x00010000;

    internal const uint FwpActionFlagTerminating = 0x00001000;
    internal const uint FwpActionBlock = FwpActionFlagTerminating | 0x1;

    internal const uint FwpUint8 = 1;
    internal const uint FwpByteBlobType = 12;
    internal const uint FwpMatchEqual = 0;

    internal const uint RpcCAuthnWinnt = 10;

    // GUID điều kiện ALE_APP_ID (FWPM_CONDITION_ALE_APP_ID, fwpmu.h).
    internal static readonly Guid ConditionAleAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");

    // Layer ALE (fwpmu.h) — chặn cả outbound connect lẫn inbound accept, IPv4 + IPv6.
    internal static readonly Guid LayerAleAuthConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    internal static readonly Guid LayerAleAuthConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    internal static readonly Guid LayerAleAuthRecvAcceptV4 = new("c973b13a-1f0b-4bee-b022-4e3fcd5b3a7a");
    internal static readonly Guid LayerAleAuthRecvAcceptV6 = new("6a35a520-cad2-4423-a3f2-0122a8ba6b7c");

    // Provider/Sublayer riêng của dự án (ADR-32) — GUID hằng số cố định để idempotent check.
    internal static readonly Guid ProviderKey = new("5dddd3aa-f2c0-4f84-8362-1731ebc9e400");
    internal static readonly Guid SubLayerKey = new("e2539dea-dec0-440d-971b-3b05bb8e7b9b");

    internal static readonly Guid FilterKeyAuthConnectV4 = new("6267dbcd-c238-4cbb-97bb-e46b4703db23");
    internal static readonly Guid FilterKeyAuthConnectV6 = new("829585c0-7c8d-4e13-939e-8574ee3d227e");
    internal static readonly Guid FilterKeyRecvAcceptV4 = new("1c1a7e0a-6b1e-4a3a-9a3a-1e0a6b1e4a3a");
    internal static readonly Guid FilterKeyRecvAcceptV6 = new("2d2b8f1b-7c2f-5b4b-8b4b-2f1b7c2f5b4b");

    internal const uint FwpEFilterNotFound = 0x80320003;
    internal const uint FwpEProviderNotFound = 0x80320005;
    internal const uint FwpESubLayerNotFound = 0x80320007;

    [DllImport("fwpuclnt.dll", SetLastError = false)]
    internal static extern uint FwpmEngineOpen0(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
        uint authnService,
        IntPtr authIdentity,
        IntPtr session,
        out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmProviderAdd0(IntPtr engineHandle, ref FwpmProvider0 provider, IntPtr sd);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmProviderGetByKey0(IntPtr engineHandle, ref Guid key, out IntPtr provider);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, ref FwpmSubLayer0 subLayer, IntPtr sd);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmSubLayerGetByKey0(IntPtr engineHandle, ref Guid key, out IntPtr subLayer);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmFilterAdd0(IntPtr engineHandle, ref FwpmFilter0 filter, IntPtr sd, out ulong id);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmFilterGetByKey0(IntPtr engineHandle, ref Guid key, out IntPtr filter);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmFreeMemory0(ref IntPtr p);

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    internal static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

    // Đợt 4 (ANTI-020, Architecture/09 mục 5.5 bước 3) — gỡ ngược lại provider/sublayer/filter lúc uninstall.
    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmFilterDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmSubLayerDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    internal static extern uint FwpmProviderDeleteByKey0(IntPtr engineHandle, ref Guid key);
}

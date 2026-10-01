using System.Runtime.InteropServices;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Bug real-hardware 2026-10-01 — cờ persistent sai làm WFP từ chối provider (FWP_E_INVALID_FLAGS),
/// Vision không bao giờ bị chặn mạng. Khoá giá trị cờ + layout struct x64 theo đúng fwpmtypes.h, vì
/// sandbox/CI không có quyền admin để gọi WFP thật.
/// </summary>
public class WfpInteropLayoutTests
{
    [Fact]
    public void PersistentFlag_MatchesFwpmTypesHeader() => Assert.Equal(0x00000001u, WfpInterop.FwpmPersistent);

    [Fact]
    public void AleLayerGuids_MatchFwpmuHeader()
    {
        Assert.Equal(new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82"), WfpInterop.LayerAleAuthConnectV4);
        Assert.Equal(new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4"), WfpInterop.LayerAleAuthConnectV6);
        Assert.Equal(new Guid("e1cd9fe7-f4b5-4273-96c0-592e487b8650"), WfpInterop.LayerAleAuthRecvAcceptV4);
        Assert.Equal(new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f"), WfpInterop.LayerAleAuthRecvAcceptV6);
    }

    [Fact]
    public void FwpmFilter0_X64Layout_MatchesNativeHeader()
    {
        Assert.Equal(8, IntPtr.Size); // dự án chỉ build x64
        Assert.Equal(40, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.ProviderKey)));
        Assert.Equal(64, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.LayerKey)));
        Assert.Equal(96, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.Weight)));
        Assert.Equal(120, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.FilterCondition)));
        Assert.Equal(128, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.Action)));
        Assert.Equal(152, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.ProviderContextLow)));
        Assert.Equal(168, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.Reserved)));
        Assert.Equal(176, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.FilterId)));
        Assert.Equal(200, Marshal.SizeOf<FwpmFilter0>());
    }

    [Fact]
    public void FwpmProviderAndSubLayer_X64Layout_MatchesNativeHeader()
    {
        Assert.Equal(32, (int)Marshal.OffsetOf<FwpmProvider0>(nameof(FwpmProvider0.Flags)));
        Assert.Equal(64, Marshal.SizeOf<FwpmProvider0>());
        Assert.Equal(40, (int)Marshal.OffsetOf<FwpmSubLayer0>(nameof(FwpmSubLayer0.ProviderKey)));
        Assert.Equal(64, (int)Marshal.OffsetOf<FwpmSubLayer0>(nameof(FwpmSubLayer0.Weight)));
        Assert.Equal(72, Marshal.SizeOf<FwpmSubLayer0>());
        Assert.Equal(40, Marshal.SizeOf<FwpmFilterCondition0>());
    }
}

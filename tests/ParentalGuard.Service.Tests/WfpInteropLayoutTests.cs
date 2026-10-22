using System.Runtime.InteropServices;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Bug real-hardware 2026-10-01 — cờ persistent sai làm WFP từ chối provider (FWP_E_INVALID_FLAGS),
/// Vision không bao giờ bị chặn mạng. Khoá giá trị cờ + layout struct theo đúng fwpmtypes.h, vì
/// sandbox/CI không có quyền admin để gọi WFP thật. `GEN-001a` (2026-10-23): cả x64 lẫn x86 — offset
/// x86 theo quy tắc căn lề MSVC 32-bit (con trỏ 4 byte, UINT64 căn lề 8); chạy bộ test này bằng
/// <c>dotnet test -a x86</c> để kiểm bản 32-bit.
/// </summary>
public class WfpInteropLayoutTests
{
    private static bool Is64 => IntPtr.Size == 8;

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
    public void FwpmFilter0_Layout_MatchesNativeHeader()
    {
        Assert.Equal(Is64 ? 40 : 28, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.ProviderKey)));
        Assert.Equal(Is64 ? 64 : 40, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.LayerKey)));
        Assert.Equal(Is64 ? 96 : 72, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.Weight)));
        Assert.Equal(Is64 ? 120 : 84, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.FilterCondition)));
        Assert.Equal(Is64 ? 128 : 88, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.Action)));
        Assert.Equal(Is64 ? 152 : 112, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.ProviderContextLow)));
        Assert.Equal(Is64 ? 168 : 128, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.Reserved)));
        Assert.Equal(Is64 ? 176 : 136, (int)Marshal.OffsetOf<FwpmFilter0>(nameof(FwpmFilter0.FilterId)));
        Assert.Equal(Is64 ? 200 : 152, Marshal.SizeOf<FwpmFilter0>());
    }

    [Fact]
    public void FwpmProviderAndSubLayer_Layout_MatchesNativeHeader()
    {
        Assert.Equal(Is64 ? 32 : 24, (int)Marshal.OffsetOf<FwpmProvider0>(nameof(FwpmProvider0.Flags)));
        Assert.Equal(Is64 ? 64 : 40, Marshal.SizeOf<FwpmProvider0>());
        Assert.Equal(Is64 ? 40 : 28, (int)Marshal.OffsetOf<FwpmSubLayer0>(nameof(FwpmSubLayer0.ProviderKey)));
        Assert.Equal(Is64 ? 64 : 40, (int)Marshal.OffsetOf<FwpmSubLayer0>(nameof(FwpmSubLayer0.Weight)));
        Assert.Equal(Is64 ? 72 : 44, Marshal.SizeOf<FwpmSubLayer0>());
        Assert.Equal(Is64 ? 40 : 28, Marshal.SizeOf<FwpmFilterCondition0>());
    }
}

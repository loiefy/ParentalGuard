using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ParentalGuard.Service.Security;

/// <summary>
/// Chặn network 2 chiều cho <c>ParentalGuard.Vision.exe</c> qua Windows Filtering Platform
/// (SEC-010/SEC-016–018, Architecture/06-security-architecture.md mục 3, ADR-32). Idempotent —
/// an toàn gọi lại mỗi lần <c>Service</c> Starting (kể cả sau Watchdog restart).
/// </summary>
public sealed class WfpVisionBlocker(ILogger<WfpVisionBlocker> logger)
{
    public void Apply(string visionExecutablePath)
    {
        if (WfpInterop.FwpmEngineOpen0(null, WfpInterop.RpcCAuthnWinnt, IntPtr.Zero, IntPtr.Zero, out IntPtr engine) != 0)
        {
            throw new InvalidOperationException("FwpmEngineOpen0 failed.");
        }

        try
        {
            EnsureProvider(engine);
            EnsureSubLayer(engine);

            IntPtr appIdBlob = GetAppIdBlob(visionExecutablePath);
            try
            {
                EnsureFilter(engine, WfpInterop.FilterKeyAuthConnectV4, WfpInterop.LayerAleAuthConnectV4, appIdBlob, "Block Vision outbound connect (IPv4)");
                EnsureFilter(engine, WfpInterop.FilterKeyAuthConnectV6, WfpInterop.LayerAleAuthConnectV6, appIdBlob, "Block Vision outbound connect (IPv6)");
                EnsureFilter(engine, WfpInterop.FilterKeyRecvAcceptV4, WfpInterop.LayerAleAuthRecvAcceptV4, appIdBlob, "Block Vision inbound accept (IPv4)");
                EnsureFilter(engine, WfpInterop.FilterKeyRecvAcceptV6, WfpInterop.LayerAleAuthRecvAcceptV6, appIdBlob, "Block Vision inbound accept (IPv6)");
            }
            finally
            {
                WfpInterop.FwpmFreeMemory0(ref appIdBlob);
            }

            logger.LogInformation("WFP network block for Vision applied/verified for '{Path}'.", visionExecutablePath);
        }
        finally
        {
            WfpInterop.FwpmEngineClose0(engine);
        }
    }

    /// <summary>
    /// Đảo ngược <see cref="Apply"/> (ANTI-020, Architecture/09-anti-tamper-architecture.md mục 5.5
    /// bước 3) — best-effort, idempotent: lỗi "not found" bỏ qua, không throw để không chặn các bước
    /// dọn dẹp còn lại của luồng uninstall.
    /// </summary>
    public void Remove()
    {
        if (WfpInterop.FwpmEngineOpen0(null, WfpInterop.RpcCAuthnWinnt, IntPtr.Zero, IntPtr.Zero, out IntPtr engine) != 0)
        {
            logger.LogWarning("FwpmEngineOpen0 failed while removing WFP filters — continuing best-effort.");
            return;
        }

        try
        {
            DeleteFilterBestEffort(engine, WfpInterop.FilterKeyAuthConnectV4);
            DeleteFilterBestEffort(engine, WfpInterop.FilterKeyAuthConnectV6);
            DeleteFilterBestEffort(engine, WfpInterop.FilterKeyRecvAcceptV4);
            DeleteFilterBestEffort(engine, WfpInterop.FilterKeyRecvAcceptV6);

            Guid subLayerKey = WfpInterop.SubLayerKey;
            WfpInterop.FwpmSubLayerDeleteByKey0(engine, ref subLayerKey);

            Guid providerKey = WfpInterop.ProviderKey;
            WfpInterop.FwpmProviderDeleteByKey0(engine, ref providerKey);

            logger.LogInformation("WFP filters/sublayer/provider removed (uninstall).");
        }
        finally
        {
            WfpInterop.FwpmEngineClose0(engine);
        }
    }

    private static void DeleteFilterBestEffort(IntPtr engine, Guid filterKey)
    {
        Guid key = filterKey;
        WfpInterop.FwpmFilterDeleteByKey0(engine, ref key); // "not found" (0x80320003) coi như đã xoá — idempotent.
    }

    private static void EnsureProvider(IntPtr engine)
    {
        Guid key = WfpInterop.ProviderKey;
        uint status = WfpInterop.FwpmProviderGetByKey0(engine, ref key, out IntPtr existing);
        if (status == 0)
        {
            WfpInterop.FwpmFreeMemory0(ref existing);
            return; // đã tồn tại — idempotent (mục 3.2).
        }

        if (status != WfpInterop.FwpEProviderNotFound)
        {
            throw new InvalidOperationException($"FwpmProviderGetByKey0 failed: 0x{status:X8}");
        }

        IntPtr name = Marshal.StringToHGlobalUni("ParentalGuard");
        IntPtr description = Marshal.StringToHGlobalUni("ParentalGuard local network isolation provider");
        try
        {
            var provider = new FwpmProvider0
            {
                ProviderKey = key,
                DisplayData = new FwpmDisplayData0 { Name = name, Description = description },
                Flags = WfpInterop.FwpmPersistent,
            };
            uint addStatus = WfpInterop.FwpmProviderAdd0(engine, ref provider, IntPtr.Zero);
            if (addStatus != 0)
            {
                throw new InvalidOperationException($"FwpmProviderAdd0 failed: 0x{addStatus:X8}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(description);
        }
    }

    private static void EnsureSubLayer(IntPtr engine)
    {
        Guid key = WfpInterop.SubLayerKey;
        uint status = WfpInterop.FwpmSubLayerGetByKey0(engine, ref key, out IntPtr existing);
        if (status == 0)
        {
            WfpInterop.FwpmFreeMemory0(ref existing);
            return;
        }

        if (status != WfpInterop.FwpESubLayerNotFound)
        {
            throw new InvalidOperationException($"FwpmSubLayerGetByKey0 failed: 0x{status:X8}");
        }

        IntPtr name = Marshal.StringToHGlobalUni("ParentalGuard");
        IntPtr description = Marshal.StringToHGlobalUni("ParentalGuard local network isolation sublayer");
        IntPtr providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        try
        {
            Marshal.StructureToPtr(WfpInterop.ProviderKey, providerKeyPtr, fDeleteOld: false);
            var subLayer = new FwpmSubLayer0
            {
                SubLayerKey = key,
                DisplayData = new FwpmDisplayData0 { Name = name, Description = description },
                Flags = WfpInterop.FwpmPersistent,
                ProviderKey = providerKeyPtr,
                Weight = ushort.MaxValue,
            };
            uint addStatus = WfpInterop.FwpmSubLayerAdd0(engine, ref subLayer, IntPtr.Zero);
            if (addStatus != 0)
            {
                throw new InvalidOperationException($"FwpmSubLayerAdd0 failed: 0x{addStatus:X8}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(providerKeyPtr);
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(description);
        }
    }

    private static IntPtr GetAppIdBlob(string executablePath)
    {
        uint status = WfpInterop.FwpmGetAppIdFromFileName0(executablePath, out IntPtr appId);
        if (status != 0)
        {
            throw new InvalidOperationException($"FwpmGetAppIdFromFileName0 failed: 0x{status:X8}");
        }

        return appId;
    }

    private static void EnsureFilter(IntPtr engine, Guid filterKey, Guid layerKey, IntPtr appIdBlob, string description)
    {
        Guid key = filterKey;
        uint getStatus = WfpInterop.FwpmFilterGetByKey0(engine, ref key, out IntPtr existing);
        if (getStatus == 0)
        {
            WfpInterop.FwpmFreeMemory0(ref existing);
            return; // đã tồn tại — idempotent (mục 3.2).
        }

        if (getStatus != WfpInterop.FwpEFilterNotFound)
        {
            throw new InvalidOperationException($"FwpmFilterGetByKey0 failed: 0x{getStatus:X8}");
        }

        IntPtr name = Marshal.StringToHGlobalUni("ParentalGuard.Vision network block");
        IntPtr desc = Marshal.StringToHGlobalUni(description);
        IntPtr providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        IntPtr conditions = Marshal.AllocHGlobal(Marshal.SizeOf<FwpmFilterCondition0>());
        try
        {
            Marshal.StructureToPtr(WfpInterop.ProviderKey, providerKeyPtr, fDeleteOld: false);

            var condition = new FwpmFilterCondition0
            {
                FieldKey = WfpInterop.ConditionAleAppId,
                MatchType = WfpInterop.FwpMatchEqual,
                ConditionValue = new FwpValue0 { Type = WfpInterop.FwpByteBlobType, UnionValue = (ulong)appIdBlob.ToInt64() },
            };
            Marshal.StructureToPtr(condition, conditions, fDeleteOld: false);

            var filter = new FwpmFilter0
            {
                FilterKey = filterKey,
                DisplayData = new FwpmDisplayData0 { Name = name, Description = desc },
                Flags = WfpInterop.FwpmPersistent,
                ProviderKey = providerKeyPtr,
                LayerKey = layerKey,
                SubLayerKey = WfpInterop.SubLayerKey,
                Weight = new FwpValue0 { Type = WfpInterop.FwpUint8, UnionValue = 15 }, // mức cao nhất thang 0-15 (mục 3.1)
                NumFilterConditions = 1,
                FilterCondition = conditions,
                Action = new FwpmAction0 { Type = WfpInterop.FwpActionBlock },
            };

            uint addStatus = WfpInterop.FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out _);
            if (addStatus != 0)
            {
                throw new InvalidOperationException($"FwpmFilterAdd0 failed: 0x{addStatus:X8}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(conditions);
            Marshal.FreeHGlobal(providerKeyPtr);
            Marshal.FreeHGlobal(desc);
            Marshal.FreeHGlobal(name);
        }
    }
}

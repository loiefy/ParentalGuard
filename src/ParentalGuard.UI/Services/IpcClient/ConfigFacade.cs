using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IConfigFacade"/>
public sealed class ConfigFacade(UiIpcClient client) : IConfigFacade
{
    public async Task<ConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.ConfigQuery = new ConfigQuery();
        return await client.SendRequestAsync(request, MapConfigResponse, cancellationToken).ConfigureAwait(false);
    }

    private static ConfigSnapshot MapConfigResponse(IpcPayload response)
    {
        ConfigResponse resp = response.ConfigResp;
        return new ConfigSnapshot(resp.OverlayMessage, [.. resp.UserWhitelistedProcessNames], MapPerformanceMode(resp.PerformanceMode));
    }

    public Task<ConfigUpdateOutcome> UpdateOverlayMessageAsync(string overlayMessage, PerformanceModeOption currentPerformanceMode, CancellationToken cancellationToken) =>
        UpdateConfigAsync(overlayMessage, currentPerformanceMode, cancellationToken);

    public Task<ConfigUpdateOutcome> UpdatePerformanceModeAsync(string currentOverlayMessage, PerformanceModeOption performanceMode, CancellationToken cancellationToken) =>
        UpdateConfigAsync(currentOverlayMessage, performanceMode, cancellationToken);

    /// <summary>Mục 6.4 — "full update": mọi lần Save LUÔN gửi đầy đủ cả 2 field hiện hành, bất kể mục đích gọi là đổi field nào.</summary>
    private async Task<ConfigUpdateOutcome> UpdateConfigAsync(string overlayMessage, PerformanceModeOption performanceMode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overlayMessage);

        IpcPayload request = client.NewEnvelope();
        request.ConfigUpdateReq = new ConfigUpdateRequest
        {
            OverlayMessage = overlayMessage,
            PerformanceMode = MapPerformanceMode(performanceMode),
        };
        return await client.SendRequestAsync(request, MapConfigUpdateResponse, cancellationToken).ConfigureAwait(false);
    }

    private static ConfigUpdateOutcome MapConfigUpdateResponse(IpcPayload response)
    {
        ConfigUpdateResponse resp = response.ConfigUpdateResp;
        return resp.Result switch
        {
            ConfigUpdateResult.Success => ConfigUpdateOutcome.Success,
            ConfigUpdateResult.InvalidCharacters => ConfigUpdateOutcome.InvalidCharacters,
            ConfigUpdateResult.TooLong => ConfigUpdateOutcome.TooLong,
            _ => throw new UiIpcConnectionException($"Unexpected ConfigUpdateResult: {resp.Result}."),
        };
    }

    public async Task<RemoveWhitelistOutcome> RemoveWhitelistEntryAsync(byte[] actionToken, string processName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);
        ArgumentException.ThrowIfNullOrEmpty(processName);

        IpcPayload request = client.NewEnvelope();
        request.RemoveWhitelistReq = new RemoveWhitelistEntryRequest
        {
            ActionToken = ByteString.CopyFrom(actionToken),
            ProcessName = processName,
        };
        return await client.SendRequestAsync(request, MapRemoveWhitelistResponse, cancellationToken).ConfigureAwait(false);
    }

    private static RemoveWhitelistOutcome MapRemoveWhitelistResponse(IpcPayload response)
    {
        RemoveWhitelistEntryResponse resp = response.RemoveWhitelistResp;
        return resp.Result switch
        {
            RemoveWhitelistEntryResult.Success => RemoveWhitelistOutcome.Success,
            RemoveWhitelistEntryResult.InvalidToken => RemoveWhitelistOutcome.InvalidToken,
            RemoveWhitelistEntryResult.NotFound => RemoveWhitelistOutcome.NotFound,
            _ => throw new UiIpcConnectionException($"Unexpected RemoveWhitelistEntryResult: {resp.Result}."),
        };
    }

    private static PerformanceMode MapPerformanceMode(PerformanceModeOption option) => option switch
    {
        PerformanceModeOption.Balanced => PerformanceMode.Balanced,
        PerformanceModeOption.MaximumProtection => PerformanceMode.MaximumProtection,
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

    private static PerformanceModeOption MapPerformanceMode(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Balanced => PerformanceModeOption.Balanced,
        PerformanceMode.MaximumProtection => PerformanceModeOption.MaximumProtection,
        _ => throw new UiIpcConnectionException($"Unexpected PerformanceMode: {mode}."),
    };
}

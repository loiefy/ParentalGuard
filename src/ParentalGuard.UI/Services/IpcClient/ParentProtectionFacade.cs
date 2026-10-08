using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IParentProtectionFacade"/>
public sealed class ParentProtectionFacade(UiIpcClient client) : IParentProtectionFacade
{
    public async Task<ParentGameStart> StartPauseGameAsync(byte[] actionToken, PauseDurationOption duration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);

        IpcPayload request = client.NewEnvelope();
        request.ParentGameStartReq = new ParentGameStartRequest
        {
            Purpose = ParentGamePurpose.Pause,
            ActionToken = ByteString.CopyFrom(actionToken),
            Duration = PauseFacade.MapDuration(duration),
        };
        return await client.SendRequestAsync(request, MapStart, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ParentGameStart> StartSettingsGameAsync(bool enabled, uint gameMeters, CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.ParentGameStartReq = new ParentGameStartRequest
        {
            Purpose = ParentGamePurpose.Settings,
            SettingsEnabled = enabled,
            SettingsGameMeters = gameMeters,
        };
        return await client.SendRequestAsync(request, MapStart, cancellationToken).ConfigureAwait(false);
    }

    private static ParentGameStart MapStart(IpcPayload response)
    {
        ParentGameStartResponse resp = response.ParentGameStartResp;
        return new ParentGameStart(MapResult(resp.Result), resp.TargetMeters, resp.Seed, resp.MinDurationMs);
    }

    public async Task<ParentGameFinish> FinishGameAsync(bool completed, uint metersReached, CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.ParentGameFinishReq = new ParentGameFinishRequest { Completed = completed, MetersReached = metersReached };
        return await client.SendRequestAsync(
            request,
            r => new ParentGameFinish(MapResult(r.ParentGameFinishResp.Result), r.ParentGameFinishResp.PauseExpiresAtUnixMs),
            cancellationToken).ConfigureAwait(false);
    }

    private static ParentGameOutcome MapResult(ParentGameResult result) => result switch
    {
        ParentGameResult.Started => ParentGameOutcome.Started,
        ParentGameResult.InvalidToken => ParentGameOutcome.InvalidToken,
        ParentGameResult.NotAuthenticated => ParentGameOutcome.NotAuthenticated,
        ParentGameResult.NotRequired => ParentGameOutcome.NotRequired,
        ParentGameResult.AlreadyPaused => ParentGameOutcome.AlreadyPaused,
        ParentGameResult.Paused => ParentGameOutcome.Paused,
        ParentGameResult.Applied => ParentGameOutcome.Applied,
        ParentGameResult.Lost => ParentGameOutcome.Lost,
        ParentGameResult.TooFast => ParentGameOutcome.TooFast,
        ParentGameResult.NoGame => ParentGameOutcome.NoGame,
        _ => ParentGameOutcome.Failed,
    };

    public async Task<SetParentProtectionOutcome> SetParentProtectionAsync(bool enabled, uint gameMeters, CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.SetParentProtectionReq = new SetParentProtectionRequest { Enabled = enabled, ParentGameMeters = gameMeters };
        return await client.SendRequestAsync(request, MapSetProtection, cancellationToken).ConfigureAwait(false);
    }

    private static SetParentProtectionOutcome MapSetProtection(IpcPayload response) => response.SetParentProtectionResp.Result switch
    {
        SetParentProtectionResult.Success => SetParentProtectionOutcome.Success,
        SetParentProtectionResult.NotAuthenticated => SetParentProtectionOutcome.NotAuthenticated,
        SetParentProtectionResult.ChallengeRequired => SetParentProtectionOutcome.ChallengeRequired,
        _ => SetParentProtectionOutcome.Failed,
    };

    public async Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(languageCode);

        IpcPayload request = client.NewEnvelope();
        request.SetLanguageReq = new SetLanguageRequest { Language = languageCode };
        return await client.SendRequestAsync(request, r => r.SetLanguageResp.Accepted, cancellationToken).ConfigureAwait(false);
    }
}

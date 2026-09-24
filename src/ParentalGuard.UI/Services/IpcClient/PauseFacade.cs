using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IPauseFacade"/>
public sealed class PauseFacade(UiIpcClient client) : IPauseFacade
{
    public async Task<PauseMonitoringResult> PauseMonitoringAsync(byte[] actionToken, PauseDurationOption duration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);

        IpcPayload request = client.NewEnvelope();
        request.PauseMonitoringReq = new PauseMonitoringRequest
        {
            ActionToken = ByteString.CopyFrom(actionToken),
            Duration = MapDuration(duration),
        };
        return await client.SendRequestAsync(request, MapPauseResponse, cancellationToken).ConfigureAwait(false);
    }

    private static PauseDuration MapDuration(PauseDurationOption duration) => duration switch
    {
        PauseDurationOption.FifteenMinutes => PauseDuration.FifteenMinutes,
        PauseDurationOption.ThirtyMinutes => PauseDuration.ThirtyMinutes,
        PauseDurationOption.OneHour => PauseDuration.OneHour,
        PauseDurationOption.FourHours => PauseDuration.FourHours,
        PauseDurationOption.EndOfDay => PauseDuration.EndOfDay,
        _ => throw new ArgumentOutOfRangeException(nameof(duration)),
    };

    private static PauseMonitoringResult MapPauseResponse(IpcPayload response)
    {
        PauseMonitoringResponse resp = response.PauseMonitoringResp;
        PauseOutcome outcome = resp.Result switch
        {
            PauseResult.Success => PauseOutcome.Success,
            PauseResult.InvalidToken => PauseOutcome.InvalidToken,
            PauseResult.AlreadyPaused => PauseOutcome.AlreadyPaused,
            _ => throw new UiIpcConnectionException($"Unexpected PauseResult: {resp.Result}."),
        };
        return new PauseMonitoringResult(outcome, resp.PauseExpiresAtUnixMs);
    }

    public async Task<ResumeMonitoringResult> ResumeMonitoringAsync(byte[] actionToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);

        IpcPayload request = client.NewEnvelope();
        request.ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(actionToken) };
        return await client.SendRequestAsync(request, MapResumeResponse, cancellationToken).ConfigureAwait(false);
    }

    private static ResumeMonitoringResult MapResumeResponse(IpcPayload response)
    {
        ResumeMonitoringResponse resp = response.ResumeMonitoringResp;
        ResumeOutcome outcome = resp.Result switch
        {
            ResumeResult.Success => ResumeOutcome.Success,
            ResumeResult.InvalidToken => ResumeOutcome.InvalidToken,
            ResumeResult.NotPaused => ResumeOutcome.NotPaused,
            _ => throw new UiIpcConnectionException($"Unexpected ResumeResult: {resp.Result}."),
        };
        return new ResumeMonitoringResult(outcome);
    }
}

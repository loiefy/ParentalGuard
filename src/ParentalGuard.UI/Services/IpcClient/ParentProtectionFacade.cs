using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IParentProtectionFacade"/>
public sealed class ParentProtectionFacade(UiIpcClient client) : IParentProtectionFacade
{
    public async Task<ChallengeStart> StartChallengeAsync(CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.ChallengeStartReq = new ChallengeStartRequest();
        return await client.SendRequestAsync(request, MapStart, cancellationToken).ConfigureAwait(false);
    }

    private static ChallengeStart MapStart(IpcPayload response)
    {
        ChallengeStartResponse resp = response.ChallengeStartResp;
        return new ChallengeStart(MapResult(resp.Result), [.. resp.Questions], resp.ExpiresAtUnixMs, resp.LockedUntilUnixMs);
    }

    public async Task<ChallengeSubmitResult> SubmitChallengeAsync(IReadOnlyList<int> answers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answers);

        IpcPayload request = client.NewEnvelope();
        var submit = new ChallengeSubmitRequest();
        submit.Answers.AddRange(answers);
        request.ChallengeSubmitReq = submit;
        return await client.SendRequestAsync(request, MapSubmit, cancellationToken).ConfigureAwait(false);
    }

    private static ChallengeSubmitResult MapSubmit(IpcPayload response)
    {
        ChallengeSubmitResponse resp = response.ChallengeSubmitResp;
        return new ChallengeSubmitResult(MapResult(resp.Result), resp.LockedUntilUnixMs, resp.FailuresBeforeLockout);
    }

    private static ChallengeOutcome MapResult(ChallengeResult result) => result switch
    {
        ChallengeResult.Success => ChallengeOutcome.Started,
        ChallengeResult.Passed => ChallengeOutcome.Passed,
        ChallengeResult.Wrong => ChallengeOutcome.Wrong,
        ChallengeResult.Expired => ChallengeOutcome.Expired,
        ChallengeResult.LockedOut => ChallengeOutcome.LockedOut,
        ChallengeResult.NoChallenge => ChallengeOutcome.NoChallenge,
        _ => throw new UiIpcConnectionException($"Unexpected ChallengeResult: {result}."),
    };

    public async Task<SetParentProtectionOutcome> SetParentProtectionAsync(bool enabled, CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.SetParentProtectionReq = new SetParentProtectionRequest { Enabled = enabled };
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

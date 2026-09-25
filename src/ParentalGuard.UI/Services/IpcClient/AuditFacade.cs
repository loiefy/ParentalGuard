using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IAuditFacade"/>
public sealed class AuditFacade(UiIpcClient client) : IAuditFacade
{
    public async Task<AuditLogFetchResult> GetAuditLogAsync(byte[] actionToken, uint page, uint pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);

        IpcPayload request = client.NewEnvelope();
        request.AuditLogQuery = new AuditLogQuery
        {
            ActionToken = ByteString.CopyFrom(actionToken),
            Page = page,
            PageSize = pageSize,
        };
        return await client.SendRequestAsync(request, MapResponse, cancellationToken).ConfigureAwait(false);
    }

    private static AuditLogFetchResult MapResponse(IpcPayload response)
    {
        AuditLogResponse resp = response.AuditLogResp;
        AuditLogQueryOutcome outcome = resp.Result switch
        {
            AuditLogQueryResult.Success => AuditLogQueryOutcome.Success,
            AuditLogQueryResult.InvalidToken => AuditLogQueryOutcome.InvalidToken,
            _ => throw new UiIpcConnectionException($"Unexpected AuditLogQueryResult: {resp.Result}."),
        };
        List<AuditLogEntry> entries = resp.Entries
            .Select(e => new AuditLogEntry(e.Seq, e.TsUnixMs, e.EventType, e.ProcessName, e.RiskScore))
            .ToList();
        return new AuditLogFetchResult(outcome, entries, resp.HasMore);
    }

    public async Task<MarkFalsePositiveOutcome> MarkFalsePositiveAsync(byte[] actionToken, string processName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);
        ArgumentException.ThrowIfNullOrEmpty(processName);

        IpcPayload request = client.NewEnvelope();
        request.MarkFalsePositiveReq = new MarkFalsePositiveRequest
        {
            ActionToken = ByteString.CopyFrom(actionToken),
            ProcessName = processName,
        };
        return await client.SendRequestAsync(request, MapMarkFalsePositiveResponse, cancellationToken).ConfigureAwait(false);
    }

    private static MarkFalsePositiveOutcome MapMarkFalsePositiveResponse(IpcPayload response)
    {
        MarkFalsePositiveResponse resp = response.MarkFalsePositiveResp;
        return resp.Result switch
        {
            MarkFalsePositiveResult.Success => MarkFalsePositiveOutcome.Success,
            MarkFalsePositiveResult.InvalidToken => MarkFalsePositiveOutcome.InvalidToken,
            MarkFalsePositiveResult.AlreadyListed => MarkFalsePositiveOutcome.AlreadyListed,
            _ => throw new UiIpcConnectionException($"Unexpected MarkFalsePositiveResult: {resp.Result}."),
        };
    }
}

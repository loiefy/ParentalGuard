using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IParentSessionFacade"/>
public sealed class ParentSessionFacade(UiIpcClient client) : IParentSessionFacade
{
    public async Task<ParentSessionOutcome> OpenAsync(byte[] actionToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);

        IpcPayload request = client.NewEnvelope();
        var req = new ParentSessionRequest { Op = ParentSessionOp.Open, ActionToken = ByteString.CopyFrom(actionToken) };
        request.ParentSessionReq = req;
        try
        {
            return await client.SendRequestAsync(request, MapResponse, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actionToken);
            CredentialBytes.Zero(CredentialBytes.UnsafeGetBuffer(req.ActionToken));
        }
    }

    public async Task<ParentSessionOutcome> KeepAliveAsync(CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.ParentSessionReq = new ParentSessionRequest { Op = ParentSessionOp.Keepalive };
        return await client.SendRequestAsync(request, MapResponse, cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.ParentSessionReq = new ParentSessionRequest { Op = ParentSessionOp.Close };
        await client.SendRequestAsync(request, MapResponse, cancellationToken).ConfigureAwait(false);
    }

    private static ParentSessionOutcome MapResponse(IpcPayload response) => response.ParentSessionResp.Result switch
    {
        ParentSessionResult.Success => ParentSessionOutcome.Success,
        ParentSessionResult.InvalidToken => ParentSessionOutcome.InvalidToken,
        ParentSessionResult.NotActive => ParentSessionOutcome.NotActive,
        _ => throw new UiIpcConnectionException($"Unexpected ParentSessionResult: {response.ParentSessionResp.Result}."),
    };
}

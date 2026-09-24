using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IAuthFacade"/>
public sealed class AuthFacade(UiIpcClient client) : IAuthFacade
{
    public async Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.AuthStatusQuery = new AuthStatusQuery();
        return await client.SendRequestAsync(request, resp => resp.AuthStatusResp.PasswordConfigured, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passwordUtf8Pinned);

        IpcPayload request = client.NewEnvelope();
        var req = new SetInitialPasswordRequest { Password = ByteString.CopyFrom(passwordUtf8Pinned) };
        request.SetInitialPasswordReq = req;
        try
        {
            return await client.SendRequestAsync(request, MapSetInitialPasswordResponse, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Architecture/08 mục 5.3 — zero byte[] pinned gốc + buffer ByteString vừa build.
            CryptographicOperations.ZeroMemory(passwordUtf8Pinned);
            CredentialBytes.Zero(CredentialBytes.UnsafeGetBuffer(req.Password));
        }
    }

    private static SetInitialPasswordResult MapSetInitialPasswordResponse(IpcPayload response)
    {
        SetInitialPasswordResponse resp = response.SetInitialPasswordResp;
        SetupOutcome outcome = resp.Result switch
        {
            SetupResult.Success => SetupOutcome.Success,
            SetupResult.PasswordTooLong => SetupOutcome.PasswordTooLong,
            SetupResult.AlreadyConfigured => SetupOutcome.AlreadyConfigured,
            _ => throw new UiIpcConnectionException($"Unexpected SetupResult: {resp.Result}."),
        };

        if (outcome != SetupOutcome.Success)
        {
            return new SetInitialPasswordResult(outcome, null, null);
        }

        // Mục 5.2 — lấy trực tiếp buffer ByteString nội bộ (không copy thêm); caller (OnboardingViewModel)
        // sở hữu vòng đời zero từ đây, đúng ADR-83 áp dụng phía UI (Service đã zero phần của nó rồi).
        byte[] recoveryKeyPlaintext = CredentialBytes.UnsafeGetBuffer(resp.RecoveryKeyPlaintext);
        byte[] setupToken = resp.SetupToken.ToByteArray();
        return new SetInitialPasswordResult(outcome, recoveryKeyPlaintext, setupToken);
    }

    public async Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupToken);

        IpcPayload request = client.NewEnvelope();
        request.ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = ByteString.CopyFrom(setupToken), Confirmed = confirmed };
        ConfirmOutcome outcome = await client.SendRequestAsync(request, resp => resp.ConfirmRecoveryResp.Result switch
        {
            ConfirmResult.Persisted => ConfirmOutcome.Persisted,
            ConfirmResult.TokenExpired => ConfirmOutcome.TokenExpired,
            ConfirmResult.TokenNotFound => ConfirmOutcome.TokenNotFound,
            _ => throw new UiIpcConnectionException($"Unexpected ConfirmResult: {resp.ConfirmRecoveryResp.Result}."),
        }, cancellationToken).ConfigureAwait(false);

        return new ConfirmRecoveryKeySavedResult(outcome);
    }

    public async Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passwordUtf8Pinned);
        ArgumentException.ThrowIfNullOrEmpty(actionContext);

        IpcPayload request = client.NewEnvelope();
        var req = new AuthVerifyRequest { Password = ByteString.CopyFrom(passwordUtf8Pinned), ActionContext = actionContext };
        request.AuthVerifyReq = req;
        try
        {
            return await client.SendRequestAsync(request, MapAuthVerifyResponse, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordUtf8Pinned);
            CredentialBytes.Zero(CredentialBytes.UnsafeGetBuffer(req.Password));
        }
    }

    private static AuthVerifyResult MapAuthVerifyResponse(IpcPayload response)
    {
        AuthVerifyResponse resp = response.AuthVerifyResp;
        AuthOutcome outcome = resp.Result switch
        {
            AuthResult.Success => AuthOutcome.Success,
            AuthResult.WrongPassword => AuthOutcome.WrongPassword,
            AuthResult.LockedOut => AuthOutcome.LockedOut,
            _ => throw new UiIpcConnectionException($"Unexpected AuthResult: {resp.Result}."),
        };

        byte[]? actionToken = outcome == AuthOutcome.Success ? resp.ActionToken.ToByteArray() : null;
        return new AuthVerifyResult(outcome, actionToken, resp.ActionTokenExpiresAtUnixMs, resp.LockoutUntilUnixMs, resp.ConsecutiveFailures);
    }
}

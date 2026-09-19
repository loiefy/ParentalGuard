using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Uninstaller;

public enum UninstallFlowOutcome
{
    Completed,
    CancelledByUser,
    ServiceUnreachable,
    ExecutionFailed,
    ConnectionLost,
}

public sealed record UninstallFlowResult(UninstallFlowOutcome Outcome, UninstallResult? ServiceResult = null, string? AuditLogCopyPath = null);

/// <summary>
/// Orchestrator luồng end-to-end (Architecture/09-anti-tamper-architecture.md mục 5.3) — thuần
/// logic, KHÔNG chạm pipe/OS trực tiếp (qua <see cref="IUninstallServiceConnection"/>/
/// <see cref="ILocalCleanup"/>) để unit test được BẤT BIẾN AN TOÀN CỐT LÕI (ADR-98):
/// <see cref="ILocalCleanup.CleanupAsync"/> KHÔNG BAO GIỜ được gọi trừ khi Service trả về
/// <c>UninstallResult.Success</c> — không có đường tắt nào khác.
/// </summary>
public sealed class UninstallFlow(IUninstallServiceConnection connection, ILocalCleanup localCleanup)
{
    public async Task<UninstallFlowResult> RunAsync(
        Func<AuthVerifyResponse?, CancellationToken, Task<byte[]?>> promptPasswordAsync,
        Func<CancellationToken, Task<bool?>> confirmProceedAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            return new UninstallFlowResult(UninstallFlowOutcome.ServiceUnreachable);
        }

        byte[]? actionToken = null;
        AuthVerifyResponse? lastAttempt = null;
        while (actionToken is null)
        {
            byte[]? password = await promptPasswordAsync(lastAttempt, cancellationToken).ConfigureAwait(false);
            if (password is null)
            {
                return new UninstallFlowResult(UninstallFlowOutcome.CancelledByUser);
            }

            AuthVerifyResponse response;
            try
            {
                response = await connection.VerifyPasswordAsync(password, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                return new UninstallFlowResult(UninstallFlowOutcome.ConnectionLost);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(password);
            }

            if (response.Result == AuthResult.Success)
            {
                actionToken = response.ActionToken.ToByteArray();
                break;
            }

            lastAttempt = response;
        }

        bool? keepAuditLog = await confirmProceedAsync(cancellationToken).ConfigureAwait(false);
        if (keepAuditLog is null)
        {
            return new UninstallFlowResult(UninstallFlowOutcome.CancelledByUser);
        }

        UninstallExecuteResponse execResponse;
        try
        {
            execResponse = await connection.ExecuteUninstallAsync(actionToken, keepAuditLog.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            return new UninstallFlowResult(UninstallFlowOutcome.ConnectionLost);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actionToken);
        }

        // ADR-98 — BẤT BIẾN AN TOÀN CỐT LÕI: chỉ dọn dẹp cục bộ khi ĐÚNG SUCCESS, không có ngoại lệ
        // (kể cả PARTIAL_FAILURE cũng KHÔNG tự xoá %ProgramFiles% — đúng nghĩa đen ADR-98).
        if (execResponse.Result != UninstallResult.Success)
        {
            return new UninstallFlowResult(UninstallFlowOutcome.ExecutionFailed, execResponse.Result);
        }

        await localCleanup.CleanupAsync(cancellationToken).ConfigureAwait(false);
        return new UninstallFlowResult(UninstallFlowOutcome.Completed, execResponse.Result, NullIfEmpty(execResponse.AuditLogCopyPath));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}

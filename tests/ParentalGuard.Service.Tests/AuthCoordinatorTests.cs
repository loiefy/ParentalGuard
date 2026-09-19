using System.Diagnostics;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Configuration;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// `PWD-001`-`041`, Architecture/08 mục 7 — toàn bộ 6 luồng nghiệp vụ qua <see cref="AuthCoordinator.HandleAsync"/>
/// (đúng con đường production <c>UiSessionServer</c> gọi), không cần pipe/transport thật.
/// </summary>
public class AuthCoordinatorTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-coord-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-coord-{Guid.NewGuid():N}.log");

    private async Task<(AuthCoordinator Coordinator, FakeMonotonicClock Clock)> CreateCoordinatorAsync()
    {
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var coordinator = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);
        return (coordinator, clock);
    }

    private static IpcPayload Envelope(ulong messageId = 1) => new() { MessageId = messageId };

    private static IpcPayload SetInitialPasswordRequest(string password, ulong messageId = 1)
    {
        IpcPayload payload = Envelope(messageId);
        payload.SetInitialPasswordReq = new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8(password) };
        return payload;
    }

    private static IpcPayload ConfirmRequest(ByteString setupToken, bool confirmed, ulong messageId = 2)
    {
        IpcPayload payload = Envelope(messageId);
        payload.ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = setupToken, Confirmed = confirmed };
        return payload;
    }

    private static IpcPayload AuthVerifyRequest(string password, string actionContext = "pause_monitoring", ulong messageId = 3)
    {
        IpcPayload payload = Envelope(messageId);
        payload.AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFromUtf8(password), ActionContext = actionContext };
        return payload;
    }

    private static IpcPayload ChangePasswordRequest(string oldPassword, string newPassword, bool regenerate, ulong messageId = 4)
    {
        IpcPayload payload = Envelope(messageId);
        payload.ChangePasswordReq = new ChangePasswordRequest
        {
            OldPassword = ByteString.CopyFromUtf8(oldPassword),
            NewPassword = ByteString.CopyFromUtf8(newPassword),
            RegenerateRecoveryKey = regenerate,
        };
        return payload;
    }

    private static IpcPayload RecoveryResetRequest(string recoveryKey, string newPassword, ulong messageId = 5)
    {
        IpcPayload payload = Envelope(messageId);
        payload.RecoveryResetReq = new RecoveryResetRequest { RecoveryKey = ByteString.CopyFromUtf8(recoveryKey), NewPassword = ByteString.CopyFromUtf8(newPassword) };
        return payload;
    }

    private async Task<string> SetupAndConfirmAsync(AuthCoordinator coordinator, string password)
    {
        IpcPayload setupResponse = await coordinator.HandleAsync(SetInitialPasswordRequest(password), CancellationToken.None);
        Assert.Equal(SetupResult.Success, setupResponse.SetInitialPasswordResp.Result);

        IpcPayload confirmResponse = await coordinator.HandleAsync(ConfirmRequest(setupResponse.SetInitialPasswordResp.SetupToken, confirmed: true), CancellationToken.None);
        Assert.Equal(ConfirmResult.Persisted, confirmResponse.ConfirmRecoveryResp.Result);

        return setupResponse.SetInitialPasswordResp.RecoveryKeyPlaintext.ToStringUtf8();
    }

    // ---------- AuthStatusQuery ----------

    [Fact]
    public async Task AuthStatusQuery_BeforeSetup_ReturnsNotConfigured()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        IpcPayload response = await coordinator.HandleAsync(new IpcPayload { MessageId = 1, AuthStatusQuery = new AuthStatusQuery() }, CancellationToken.None);

        Assert.False(response.AuthStatusResp.PasswordConfigured);
    }

    [Fact]
    public async Task AuthStatusQuery_AfterSetup_ReturnsConfigured()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "correct-horse-battery");

        IpcPayload response = await coordinator.HandleAsync(new IpcPayload { MessageId = 1, AuthStatusQuery = new AuthStatusQuery() }, CancellationToken.None);

        Assert.True(response.AuthStatusResp.PasswordConfigured);
    }

    // ---------- Setup (PWD-001-004, PWD-030/030a) ----------

    [Fact]
    public async Task SetInitialPassword_FirstTime_ReturnsSuccessWithRecoveryKeyAndSetupToken()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        IpcPayload response = await coordinator.HandleAsync(SetInitialPasswordRequest("MyStrongPassw0rd"), CancellationToken.None);

        Assert.Equal(SetupResult.Success, response.SetInitialPasswordResp.Result);
        Assert.Equal(29, response.SetInitialPasswordResp.RecoveryKeyPlaintext.Length); // 24 ký tự + 5 dấu gạch (6 nhóm)
        Assert.Equal(16, response.SetInitialPasswordResp.SetupToken.Length);
        Assert.False(AuthDataStore.Exists(_authDatPath)); // PWD-030a: CHƯA ghi auth.dat cho tới khi confirm
    }

    [Fact]
    public async Task SetInitialPassword_PasswordOver50Chars_ReturnsPasswordTooLong()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string tooLong = new('a', 51);

        IpcPayload response = await coordinator.HandleAsync(SetInitialPasswordRequest(tooLong), CancellationToken.None);

        Assert.Equal(SetupResult.PasswordTooLong, response.SetInitialPasswordResp.Result);
        Assert.False(AuthDataStore.Exists(_authDatPath));
    }

    [Fact]
    public async Task SetInitialPassword_Exactly50Chars_ReturnsSuccess()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string exactly50 = new('a', 50);

        IpcPayload response = await coordinator.HandleAsync(SetInitialPasswordRequest(exactly50), CancellationToken.None);

        Assert.Equal(SetupResult.Success, response.SetInitialPasswordResp.Result);
    }

    [Fact]
    public async Task SetInitialPassword_AfterAlreadyConfigured_ReturnsAlreadyConfigured()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "first-password");

        IpcPayload response = await coordinator.HandleAsync(SetInitialPasswordRequest("second-password"), CancellationToken.None);

        Assert.Equal(SetupResult.AlreadyConfigured, response.SetInitialPasswordResp.Result);
    }

    [Fact]
    public async Task SetInitialPassword_ZerosThePasswordByteStringBufferAfterUse()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        IpcPayload request = SetInitialPasswordRequest("zero-me-please");

        await coordinator.HandleAsync(request, CancellationToken.None);

        byte[] bufferAfter = CredentialBytes.UnsafeGetBuffer(request.SetInitialPasswordReq.Password);
        Assert.All(bufferAfter, b => Assert.Equal(0, b));
    }

    /// <summary>Mục 5.5 điểm 4 (ADR-83): <see cref="PendingSetup"/> chỉ giữ hash, gọi Setup lần 2 trước Confirm chỉ ghi đè property bình thường (không còn plaintext "mồ côi" cần lo).</summary>
    [Fact]
    public async Task SetInitialPassword_CalledTwiceBeforeConfirm_OverwritesPendingSetupWithNewHashes()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        await coordinator.HandleAsync(SetInitialPasswordRequest("first-password"), CancellationToken.None);
        string firstHash = coordinator.PendingSetupForTest!.PasswordHashPhc;

        await coordinator.HandleAsync(SetInitialPasswordRequest("second-password"), CancellationToken.None);

        Assert.NotEqual(firstHash, coordinator.PendingSetupForTest!.PasswordHashPhc);
    }

    // ---------- FAIL 1 regression (security-privacy-auditor Đợt 3, ADR-83): Recovery Key plaintext
    // zero-out kiểu IMG-003 — buffer chỉ zero SAU KHI UiSessionServer báo đã ghi xong response vào
    // pipe (ZeroRecoveryKeyPlaintextAfterSend), không phải ngay sau khi hash xong. ----------

    [Fact]
    public async Task SetInitialPassword_ZeroRecoveryKeyPlaintextAfterSend_ZerosResponseByteStringBuffer()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        IpcPayload response = await coordinator.HandleAsync(SetInitialPasswordRequest("zero-recovery-key"), CancellationToken.None);
        byte[] responseBuffer = CredentialBytes.UnsafeGetBuffer(response.SetInitialPasswordResp.RecoveryKeyPlaintext);
        Assert.Contains(responseBuffer, b => b != 0); // precondition: buffer thật có dữ liệu trước khi zero

        coordinator.ZeroRecoveryKeyPlaintextAfterSend();

        Assert.All(responseBuffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task ChangePassword_RegenerateRecoveryKey_ZeroRecoveryKeyPlaintextAfterSend_ZerosResponseByteStringBuffer()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "old-password");

        IpcPayload response = await coordinator.HandleAsync(ChangePasswordRequest("old-password", "new-password", regenerate: true), CancellationToken.None);
        byte[] responseBuffer = CredentialBytes.UnsafeGetBuffer(response.ChangePasswordResp.NewRecoveryKeyPlaintext);
        Assert.Contains(responseBuffer, b => b != 0);

        coordinator.ZeroRecoveryKeyPlaintextAfterSend();

        Assert.All(responseBuffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task RecoveryReset_ZeroRecoveryKeyPlaintextAfterSend_ZerosResponseByteStringBuffer()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string grouped = await SetupAndConfirmAsync(coordinator, "old-password");
        string canonical = grouped.Replace("-", string.Empty);

        IpcPayload response = await coordinator.HandleAsync(RecoveryResetRequest(canonical, "recovered-password"), CancellationToken.None);
        byte[] responseBuffer = CredentialBytes.UnsafeGetBuffer(response.RecoveryResetResp.NewRecoveryKeyPlaintext);
        Assert.Contains(responseBuffer, b => b != 0);

        coordinator.ZeroRecoveryKeyPlaintextAfterSend();

        Assert.All(responseBuffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task HandleAsync_UnrelatedMessage_ZeroRecoveryKeyPlaintextAfterSendIsNoOp()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        await coordinator.HandleAsync(new IpcPayload { MessageId = 1, AuthStatusQuery = new AuthStatusQuery() }, CancellationToken.None);

        // Không có gì để zero — phải không throw, không ảnh hưởng gì (đa số message không mang Recovery Key plaintext).
        coordinator.ZeroRecoveryKeyPlaintextAfterSend();
    }

    // ---------- ConfirmRecoveryKeySaved (PWD-030a) ----------

    [Fact]
    public async Task ConfirmRecoveryKeySaved_WithoutPriorSetup_ReturnsTokenNotFound()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        IpcPayload response = await coordinator.HandleAsync(ConfirmRequest(ByteString.CopyFrom(new byte[16]), confirmed: true), CancellationToken.None);

        Assert.Equal(ConfirmResult.TokenNotFound, response.ConfirmRecoveryResp.Result);
    }

    [Fact]
    public async Task ConfirmRecoveryKeySaved_WrongToken_ReturnsTokenNotFound()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await coordinator.HandleAsync(SetInitialPasswordRequest("pw"), CancellationToken.None);

        IpcPayload response = await coordinator.HandleAsync(ConfirmRequest(ByteString.CopyFrom(new byte[16]), confirmed: true), CancellationToken.None);

        Assert.Equal(ConfirmResult.TokenNotFound, response.ConfirmRecoveryResp.Result);
        Assert.False(AuthDataStore.Exists(_authDatPath));
    }

    [Fact]
    public async Task ConfirmRecoveryKeySaved_NotConfirmed_DoesNotPersist_AndCanRetryWithConfirmedTrue()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        IpcPayload setup = await coordinator.HandleAsync(SetInitialPasswordRequest("pw"), CancellationToken.None);

        IpcPayload declined = await coordinator.HandleAsync(ConfirmRequest(setup.SetInitialPasswordResp.SetupToken, confirmed: false), CancellationToken.None);
        Assert.Equal(ConfirmResult.TokenNotFound, declined.ConfirmRecoveryResp.Result);
        Assert.False(AuthDataStore.Exists(_authDatPath));

        IpcPayload confirmed = await coordinator.HandleAsync(ConfirmRequest(setup.SetInitialPasswordResp.SetupToken, confirmed: true), CancellationToken.None);
        Assert.Equal(ConfirmResult.Persisted, confirmed.ConfirmRecoveryResp.Result);
        Assert.True(AuthDataStore.Exists(_authDatPath));
    }

    [Fact]
    public async Task ConfirmRecoveryKeySaved_After30Minutes_ReturnsTokenExpired_AndClearsPendingSetup()
    {
        (AuthCoordinator coordinator, FakeMonotonicClock clock) = await CreateCoordinatorAsync();
        IpcPayload setup = await coordinator.HandleAsync(SetInitialPasswordRequest("pw"), CancellationToken.None);

        clock.Now += (long)PendingSetup.Ttl.TotalMilliseconds; // đúng ranh giới hết hạn (mục 7.1)

        IpcPayload response = await coordinator.HandleAsync(ConfirmRequest(setup.SetInitialPasswordResp.SetupToken, confirmed: true), CancellationToken.None);

        Assert.Equal(ConfirmResult.TokenExpired, response.ConfirmRecoveryResp.Result);
        Assert.False(AuthDataStore.Exists(_authDatPath));
        Assert.Null(coordinator.PendingSetupForTest);
    }

    [Fact]
    public async Task ConfirmRecoveryKeySaved_Persisted_ClearsPendingSetup()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        IpcPayload setup = await coordinator.HandleAsync(SetInitialPasswordRequest("pw"), CancellationToken.None);

        await coordinator.HandleAsync(ConfirmRequest(setup.SetInitialPasswordResp.SetupToken, confirmed: true), CancellationToken.None);

        Assert.Null(coordinator.PendingSetupForTest);
    }

    [Fact]
    public async Task ConfirmRecoveryKeySaved_JustBefore30Minutes_StillSucceeds()
    {
        (AuthCoordinator coordinator, FakeMonotonicClock clock) = await CreateCoordinatorAsync();
        IpcPayload setup = await coordinator.HandleAsync(SetInitialPasswordRequest("pw"), CancellationToken.None);

        clock.Now += (long)PendingSetup.Ttl.TotalMilliseconds - 1;

        IpcPayload response = await coordinator.HandleAsync(ConfirmRequest(setup.SetInitialPasswordResp.SetupToken, confirmed: true), CancellationToken.None);

        Assert.Equal(ConfirmResult.Persisted, response.ConfirmRecoveryResp.Result);
    }

    // ---------- AuthVerify (PWD-020-023) ----------

    [Fact]
    public async Task AuthVerify_CorrectPassword_ReturnsSuccessWithActionToken()
    {
        (AuthCoordinator coordinator, FakeMonotonicClock clock) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");

        IpcPayload response = await coordinator.HandleAsync(AuthVerifyRequest("the-password", "pause_monitoring"), CancellationToken.None);

        Assert.Equal(AuthResult.Success, response.AuthVerifyResp.Result);
        Assert.Equal(16, response.AuthVerifyResp.ActionToken.Length);
        Assert.Equal(clock.Now + 15_000, response.AuthVerifyResp.ActionTokenExpiresAtUnixMs);
    }

    [Fact]
    public async Task AuthVerify_WrongPassword_ReturnsWrongPasswordAndIncrementsCounter()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");

        IpcPayload response = await coordinator.HandleAsync(AuthVerifyRequest("wrong-password"), CancellationToken.None);

        Assert.Equal(AuthResult.WrongPassword, response.AuthVerifyResp.Result);
        Assert.Equal(1u, response.AuthVerifyResp.ConsecutiveFailures);
    }

    [Fact]
    public async Task AuthVerify_ZerosThePasswordByteStringBufferAfterUse_OnSuccessAndOnFailure()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");

        IpcPayload wrongRequest = AuthVerifyRequest("wrong-password");
        await coordinator.HandleAsync(wrongRequest, CancellationToken.None);
        Assert.All(CredentialBytes.UnsafeGetBuffer(wrongRequest.AuthVerifyReq.Password), b => Assert.Equal(0, b));

        IpcPayload correctRequest = AuthVerifyRequest("the-password");
        await coordinator.HandleAsync(correctRequest, CancellationToken.None);
        Assert.All(CredentialBytes.UnsafeGetBuffer(correctRequest.AuthVerifyReq.Password), b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task AuthVerify_FourthConsecutiveFailure_Requires30SecondDelay_LockedOutBeforeThen()
    {
        (AuthCoordinator coordinator, FakeMonotonicClock clock) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");

        for (int i = 0; i < 4; i++)
        {
            await coordinator.HandleAsync(AuthVerifyRequest("wrong"), CancellationToken.None);
        }

        // Ngay sau lần sai thứ 4 (delay 30s theo bảng PWD-021) — thử lại NGAY phải bị khoá.
        IpcPayload lockedResponse = await coordinator.HandleAsync(AuthVerifyRequest("the-password"), CancellationToken.None);
        Assert.Equal(AuthResult.LockedOut, lockedResponse.AuthVerifyResp.Result);
        Assert.Equal(clock.Now + 30_000, lockedResponse.AuthVerifyResp.LockoutUntilUnixMs);

        // Sau khi delay trôi qua — kể cả mật khẩu ĐÚNG cũng verify lại bình thường, không còn khoá.
        clock.Now += 30_000;
        IpcPayload afterDelay = await coordinator.HandleAsync(AuthVerifyRequest("the-password"), CancellationToken.None);
        Assert.Equal(AuthResult.Success, afterDelay.AuthVerifyResp.Result);
    }

    [Fact]
    public async Task AuthVerify_UpToThirdFailure_NoDelay_CanRetryImmediately()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");

        for (int i = 0; i < 3; i++)
        {
            IpcPayload wrong = await coordinator.HandleAsync(AuthVerifyRequest("wrong"), CancellationToken.None);
            Assert.Equal(AuthResult.WrongPassword, wrong.AuthVerifyResp.Result);
        }

        IpcPayload correct = await coordinator.HandleAsync(AuthVerifyRequest("the-password"), CancellationToken.None);
        Assert.Equal(AuthResult.Success, correct.AuthVerifyResp.Result); // vẫn cho thử ngay, không bị khoá (1-3 lần sai)
    }

    [Fact]
    public async Task AuthVerify_NinthConsecutiveFailure_StillReturnsWrongPasswordWithCorrectCount()
    {
        (AuthCoordinator coordinator, FakeMonotonicClock clock) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");

        IpcPayload last = null!;
        int realFailures = 0;
        while (realFailures < 9)
        {
            last = await coordinator.HandleAsync(AuthVerifyRequest("wrong"), CancellationToken.None);
            if (last.AuthVerifyResp.Result == AuthResult.LockedOut)
            {
                clock.Now = last.AuthVerifyResp.LockoutUntilUnixMs; // nhảy qua đúng lúc hết khoá rồi thử lại — không tính là 1 trong 9 lần sai thật
                continue;
            }

            realFailures++;
        }

        Assert.Equal(AuthResult.WrongPassword, last.AuthVerifyResp.Result);
        Assert.Equal(9u, last.AuthVerifyResp.ConsecutiveFailures);
    }

    [Fact]
    public async Task AuthVerify_CorrectPassword_ResetsConsecutiveFailuresToZero()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");
        await coordinator.HandleAsync(AuthVerifyRequest("wrong"), CancellationToken.None);
        await coordinator.HandleAsync(AuthVerifyRequest("wrong"), CancellationToken.None);

        await coordinator.HandleAsync(AuthVerifyRequest("the-password"), CancellationToken.None);
        IpcPayload wrongAfterReset = await coordinator.HandleAsync(AuthVerifyRequest("wrong"), CancellationToken.None);

        Assert.Equal(1u, wrongAfterReset.AuthVerifyResp.ConsecutiveFailures); // đếm lại từ 1, không tiếp tục từ 3
    }

    [Fact]
    public async Task AuthVerify_BeforeAnySetup_ReturnsWrongPassword_NotCrash()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        // Không có Requirement ID nào mô tả rõ hành vi này — nhưng KHÔNG được throw (input không
        // tin cậy từ IPC, DEV-040); trả kết quả "sai" là lựa chọn an toàn nhất hiện có trong enum
        // đã Approved (không có case "chưa setup" riêng ở AuthVerifyResponse).
        IpcPayload response = await coordinator.HandleAsync(AuthVerifyRequest("anything"), CancellationToken.None);

        Assert.Equal(AuthResult.WrongPassword, response.AuthVerifyResp.Result);
    }

    // ---------- ChangePassword (PWD-040/041) ----------

    [Fact]
    public async Task ChangePassword_WrongOldPassword_ReturnsWrongOldPassword()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "old-password");

        IpcPayload response = await coordinator.HandleAsync(ChangePasswordRequest("not-the-old-password", "new-password", regenerate: false), CancellationToken.None);

        Assert.Equal(ChangeResult.WrongOldPassword, response.ChangePasswordResp.Result);
    }

    [Fact]
    public async Task ChangePassword_Correct_UpdatesPassword_NewPasswordVerifiesAfterwards()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "old-password");

        IpcPayload response = await coordinator.HandleAsync(ChangePasswordRequest("old-password", "new-password", regenerate: false), CancellationToken.None);
        Assert.Equal(ChangeResult.Success, response.ChangePasswordResp.Result);
        Assert.Equal(ByteString.Empty, response.ChangePasswordResp.NewRecoveryKeyPlaintext);

        IpcPayload verifyOld = await coordinator.HandleAsync(AuthVerifyRequest("old-password"), CancellationToken.None);
        Assert.Equal(AuthResult.WrongPassword, verifyOld.AuthVerifyResp.Result);

        IpcPayload verifyNew = await coordinator.HandleAsync(AuthVerifyRequest("new-password"), CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyNew.AuthVerifyResp.Result);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordTooLong_ReturnsNewPasswordTooLong_OldPasswordStillWorks()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "old-password");

        IpcPayload response = await coordinator.HandleAsync(ChangePasswordRequest("old-password", new string('x', 51), regenerate: false), CancellationToken.None);

        Assert.Equal(ChangeResult.NewPasswordTooLong, response.ChangePasswordResp.Result);
        IpcPayload verifyOld = await coordinator.HandleAsync(AuthVerifyRequest("old-password"), CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyOld.AuthVerifyResp.Result);
    }

    [Fact]
    public async Task ChangePassword_RegenerateRecoveryKeyTrue_ReturnsDifferentRecoveryKey_OldRecoveryKeyStopsWorking()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string originalGrouped = await SetupAndConfirmAsync(coordinator, "old-password");
        string originalCanonical = originalGrouped.Replace("-", string.Empty);

        IpcPayload response = await coordinator.HandleAsync(ChangePasswordRequest("old-password", "new-password", regenerate: true), CancellationToken.None);

        Assert.Equal(ChangeResult.Success, response.ChangePasswordResp.Result);
        Assert.NotEmpty(response.ChangePasswordResp.NewRecoveryKeyPlaintext);
        Assert.NotEqual(originalGrouped, response.ChangePasswordResp.NewRecoveryKeyPlaintext.ToStringUtf8());

        IpcPayload oldKeyAttempt = await coordinator.HandleAsync(RecoveryResetRequest(originalCanonical, "another-password"), CancellationToken.None);
        Assert.Equal(RecoveryResetResult.WrongRecoveryKey, oldKeyAttempt.RecoveryResetResp.Result);
    }

    [Fact]
    public async Task ChangePassword_RegenerateRecoveryKeyFalse_OldRecoveryKeyStillWorks()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string originalGrouped = await SetupAndConfirmAsync(coordinator, "old-password");
        string originalCanonical = originalGrouped.Replace("-", string.Empty);

        await coordinator.HandleAsync(ChangePasswordRequest("old-password", "new-password", regenerate: false), CancellationToken.None);

        IpcPayload recovery = await coordinator.HandleAsync(RecoveryResetRequest(originalCanonical, "via-recovery-password"), CancellationToken.None);
        Assert.Equal(RecoveryResetResult.Success, recovery.RecoveryResetResp.Result);
    }

    // ---------- RecoveryReset (PWD-032/033) ----------

    [Fact]
    public async Task RecoveryReset_WrongKey_ReturnsWrongRecoveryKey()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "old-password");

        IpcPayload response = await coordinator.HandleAsync(RecoveryResetRequest("0000000000000000000000", "new-password"), CancellationToken.None);

        Assert.Equal(RecoveryResetResult.WrongRecoveryKey, response.RecoveryResetResp.Result);
    }

    [Fact]
    public async Task RecoveryReset_CorrectKey_SetsNewPassword_AndAlwaysRegeneratesRecoveryKey()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string grouped = await SetupAndConfirmAsync(coordinator, "old-password");
        string canonical = grouped.Replace("-", string.Empty);

        IpcPayload response = await coordinator.HandleAsync(RecoveryResetRequest(canonical, "recovered-password"), CancellationToken.None);

        Assert.Equal(RecoveryResetResult.Success, response.RecoveryResetResp.Result);
        Assert.NotEmpty(response.RecoveryResetResp.NewRecoveryKeyPlaintext);
        Assert.NotEqual(grouped, response.RecoveryResetResp.NewRecoveryKeyPlaintext.ToStringUtf8());

        IpcPayload verifyNew = await coordinator.HandleAsync(AuthVerifyRequest("recovered-password"), CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyNew.AuthVerifyResp.Result);
    }

    [Fact]
    public async Task RecoveryReset_AcceptsLowercaseAndHyphenatedInput_NormalizesBeforeVerify()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string grouped = await SetupAndConfirmAsync(coordinator, "old-password");

        IpcPayload response = await coordinator.HandleAsync(RecoveryResetRequest(grouped.ToLowerInvariant(), "recovered-password"), CancellationToken.None);

        Assert.Equal(RecoveryResetResult.Success, response.RecoveryResetResp.Result);
    }

    [Fact]
    public async Task RecoveryReset_SameKeyUsedTwice_SecondAttemptFails()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string grouped = await SetupAndConfirmAsync(coordinator, "old-password");
        string canonical = grouped.Replace("-", string.Empty);

        IpcPayload first = await coordinator.HandleAsync(RecoveryResetRequest(canonical, "first-recovery"), CancellationToken.None);
        Assert.Equal(RecoveryResetResult.Success, first.RecoveryResetResp.Result);

        IpcPayload second = await coordinator.HandleAsync(RecoveryResetRequest(canonical, "second-recovery"), CancellationToken.None);
        Assert.Equal(RecoveryResetResult.WrongRecoveryKey, second.RecoveryResetResp.Result);
    }

    [Fact]
    public async Task RecoveryReset_NewPasswordTooLong_ReturnsNewPasswordTooLong()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string grouped = await SetupAndConfirmAsync(coordinator, "old-password");
        string canonical = grouped.Replace("-", string.Empty);

        IpcPayload response = await coordinator.HandleAsync(RecoveryResetRequest(canonical, new string('x', 51)), CancellationToken.None);

        Assert.Equal(RecoveryResetResult.NewPasswordTooLong, response.RecoveryResetResp.Result);
    }

    /// <summary>
    /// FAIL 2 regression (security-privacy-auditor Đợt 3): thứ tự cũ (verify Recovery Key TRƯỚC,
    /// kiểm tra độ dài new_password SAU) để lại đường "verify đúng nhưng dừng giữa chừng" khiến
    /// Recovery Key KHÔNG bị vô hiệu hoá — vẫn dùng lại được ở lượt sau dù coi như đã "verify đúng"
    /// 1 lần. Sau fix (kiểm tra độ dài trước): key phải CÒN nguyên vẹn, dùng lại được với mật khẩu hợp lệ.
    /// </summary>
    [Fact]
    public async Task RecoveryReset_NewPasswordTooLong_DoesNotConsumeRecoveryKey_StillWorksAfterwards()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        string grouped = await SetupAndConfirmAsync(coordinator, "old-password");
        string canonical = grouped.Replace("-", string.Empty);

        IpcPayload tooLong = await coordinator.HandleAsync(RecoveryResetRequest(canonical, new string('x', 51)), CancellationToken.None);
        Assert.Equal(RecoveryResetResult.NewPasswordTooLong, tooLong.RecoveryResetResp.Result);

        IpcPayload retryWithValidPassword = await coordinator.HandleAsync(RecoveryResetRequest(canonical, "valid-password"), CancellationToken.None);
        Assert.Equal(RecoveryResetResult.Success, retryWithValidPassword.RecoveryResetResp.Result);
    }

    [Fact]
    public async Task RecoveryReset_SharesRateLimitCounterWithAuthVerify()
    {
        (AuthCoordinator coordinator, FakeMonotonicClock clock) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");

        for (int i = 0; i < 4; i++)
        {
            await coordinator.HandleAsync(RecoveryResetRequest("0000000000000000000000", "x"), CancellationToken.None);
        }

        // ADR-76: bộ đếm dùng chung — 4 lần sai Recovery Key cũng khoá luôn đường xác thực mật khẩu.
        IpcPayload passwordAttempt = await coordinator.HandleAsync(AuthVerifyRequest("the-password"), CancellationToken.None);
        Assert.Equal(AuthResult.LockedOut, passwordAttempt.AuthVerifyResp.Result);
    }

    // ---------- FAIL 3 regression (security-privacy-auditor Đợt 3, ADR-84/mục 7.9): bù thời gian
    // chống timing oracle nhánh auth.dat corrupt/chưa-setup. Đo thời gian TƯƠNG ĐỐI (không đo tuyệt
    // đối chính xác ms — phụ thuộc máy chạy test) chỉ để xác nhận nhánh "chưa-setup"/"corrupt" có
    // thật sự chạy 1 lần Argon2id đầy đủ (RunDecoyArgon2idAsync) thay vì trả về gần như tức thời
    // (một no-op/Task.Delay giả sẽ luôn < vài ms, trong khi Argon2id thật với tham số chính thức
    // m=32MiB/t=2/p=2 luôn tốn ít nhất vài chục ms trên mọi máy — biên dưới chọn rất rộng rãi để
    // tránh flaky, không nhằm khẳng định con số đo được ở Architecture/08 (~100ms)). ----------

    [Fact]
    public async Task AuthVerify_NeverConfigured_TakesNonTrivialTime_ProvingDecoyHashRan()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        var stopwatch = Stopwatch.StartNew();
        await coordinator.HandleAsync(AuthVerifyRequest("anything"), CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 5, $"Expected decoy Argon2id hash to take >= 5ms, took {stopwatch.ElapsedMilliseconds}ms — RunDecoyArgon2idAsync có thể đã không chạy.");
    }

    [Fact]
    public async Task AuthVerify_AuthDatCorrupt_TakesNonTrivialTime_ProvingDecoyHashRan()
    {
        File.WriteAllBytes(_authDatPath, [0x01, 0x02]); // < 4 byte version header hợp lệ — AuthDataCorruptException lúc load (Architecture/08 mục 7.8)
        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var coordinator = new AuthCoordinator(_authDatPath, auditLog, new FakeMonotonicClock(), NullLogger.Instance);

        var stopwatch = Stopwatch.StartNew();
        await coordinator.HandleAsync(AuthVerifyRequest("anything"), CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 5, $"Expected decoy Argon2id hash to take >= 5ms, took {stopwatch.ElapsedMilliseconds}ms — RunDecoyArgon2idAsync có thể đã không chạy.");
    }

    [Fact]
    public async Task ChangePassword_NeverConfigured_TakesNonTrivialTime_ProvingDecoyHashRan()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        var stopwatch = Stopwatch.StartNew();
        await coordinator.HandleAsync(ChangePasswordRequest("anything", "new-password", regenerate: false), CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 5, $"Expected decoy Argon2id hash to take >= 5ms, took {stopwatch.ElapsedMilliseconds}ms — RunDecoyArgon2idAsync có thể đã không chạy.");
    }

    [Fact]
    public async Task RecoveryReset_NeverConfigured_TakesNonTrivialTime_ProvingDecoyHashRan()
    {
        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();

        var stopwatch = Stopwatch.StartNew();
        await coordinator.HandleAsync(RecoveryResetRequest("0000000000000000000000", "x"), CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 5, $"Expected decoy Argon2id hash to take >= 5ms, took {stopwatch.ElapsedMilliseconds}ms — RunDecoyArgon2idAsync có thể đã không chạy.");
    }

    // ---------- Bug 4 regression (test-runner, hardcode path): AuthCoordinator phải ghi audit.log
    // ĐÚNG path AuditLogWriter đã InitializeAsync — không bao giờ lệch sang InstallPaths.AuditLogPath
    // production dù không được khởi tạo bằng path đó. ----------

    [Fact]
    public async Task HandleAsync_NeverWritesAuditEventsToInstallPathsAuditLogPath()
    {
        string productionPath = InstallPaths.AuditLogPath;
        bool existedBefore = File.Exists(productionPath);
        DateTime? lastWriteBefore = existedBefore ? File.GetLastWriteTimeUtc(productionPath) : null;

        (AuthCoordinator coordinator, _) = await CreateCoordinatorAsync();
        await SetupAndConfirmAsync(coordinator, "the-password");
        await coordinator.HandleAsync(AuthVerifyRequest("wrong"), CancellationToken.None);

        Assert.Equal(existedBefore, File.Exists(productionPath));
        if (existedBefore)
        {
            Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(productionPath));
        }
    }

    public void Dispose()
    {
        foreach (string path in new[] { _authDatPath, _authDatPath + ".tmp", _auditLogPath })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}

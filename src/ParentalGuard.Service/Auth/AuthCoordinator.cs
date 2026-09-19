using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;
using ParentalGuard.Service.Audit;

namespace ParentalGuard.Service.Auth;

/// <summary>
/// Orchestrator toàn bộ luồng nghiệp vụ Password &amp; Authentication (`PWD-0xx`, Architecture/08
/// mục 7) — nhận <see cref="IpcPayload"/> từ pipe <c>UI</c>, trả về response, ghi <c>auth.dat</c>/
/// <c>audit.log</c>. Không tự đọc/ghi pipe (đó là <c>UiSessionServer</c>) — thuần business logic,
/// dễ unit test độc lập với transport.
///
/// <para>
/// GAP đã ghi nhận (không có trong Architecture/08, cần architecture-writer/spec-maintainer xác
/// nhận): file không định nghĩa hành vi khi <c>auth.dat</c> TỒN TẠI nhưng KHÔNG đọc/giải mã được
/// (<see cref="AuthDataCorruptException"/> — khác nhánh fail-secure của <c>config.db</c> ở
/// Architecture/04 mục 6, vốn chỉ áp dụng domain giám sát). Coi "auth.dat không đọc được" như
/// "chưa từng setup" (password_configured=false) sẽ cho phép kẻ tấn công làm hỏng 4 byte đầu file
/// để bắt buộc luồng Setup chạy lại và tự đặt mật khẩu mới — một cách bypass xác thực nguy hiểm.
/// Lựa chọn tạm thời (fail-secure, nghiêng về phía VẪN yêu cầu xác thực — Architecture/01 mục 5):
/// coi corrupt = "đã configured" (chặn Setup lại) + mọi verify đều thất bại (không tự nới lỏng bảo
/// vệ, không tự wipe/regenerate). Không tự sửa auth.dat, không đoán ý định người dùng.
/// </para>
/// </summary>
public sealed class AuthCoordinator
{
    private readonly string _authDatPath;
    private readonly AuditLogWriter _auditLog;
    private readonly MonotonicClock _clock;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IpcMessageIdGenerator _messageIds = new();
    private readonly AuthState _state = new();

    private AuthData? _cached;
    private bool _dataCorrupt;

    /// <summary>
    /// Cleanup Recovery Key plaintext của response VỪA xử lý (mục 5.5/ADR-83) — <c>UiSessionServer</c>
    /// gọi <see cref="ZeroRecoveryKeyPlaintextAfterSend"/> ngay sau khi ghi xong frame vào pipe. An
    /// toàn không cần khoá riêng: pipe UI chỉ 1 kết nối, 1 request xử lý xong (qua <see cref="_gate"/>)
    /// rồi mới ghi response rồi mới đọc request kế tiếp (<c>UiSessionServer.RunConnectionAsync</c>
    /// tuần tự) — không có 2 lượt gọi <see cref="HandleAsync"/> chồng lấp trên cùng field này.
    /// </summary>
    private Action? _pendingAfterSendCleanup;

    public AuthCoordinator(string authDatPath, AuditLogWriter auditLog, MonotonicClock clock, ILogger logger)
    {
        _authDatPath = authDatPath;
        _auditLog = auditLog;
        _clock = clock;
        _logger = logger;

        try
        {
            _cached = AuthDataStore.TryLoad(authDatPath);
        }
        catch (AuthDataCorruptException ex)
        {
            _dataCorrupt = true;
            _logger.LogError(ex, "auth.dat exists but could not be read — denying auth until resolved (see AuthCoordinator gap note).");
        }
    }

    /// <summary>
    /// Đợt 4 (ANTI-020, Architecture/09-anti-tamper-architecture.md mục 5.3): xác thực + tiêu thụ
    /// (dùng 1 lần, xoá khỏi <c>PendingActionTokens</c> ngay — mục 7.2 đã chốt ở Architecture/08) 1
    /// <c>action_token</c> đã phát hành qua <see cref="HandleAuthVerifyAsync"/>. Dùng chung khoá
    /// <see cref="_gate"/> với các luồng khác — không có 2 lượt tiêu thụ chồng lấp trên cùng token.
    /// </summary>
    public async Task<bool> TryConsumeActionTokenAsync(byte[] token, string expectedActionContext, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string key = Convert.ToHexString(token);
            if (!_state.PendingActionTokens.Remove(key, out PendingActionToken? pending))
            {
                return false;
            }

            return pending.ActionContext == expectedActionContext && _clock.UtcNowUnixMs < pending.ExpiresAtUnixMs;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Định tuyến theo <see cref="IpcPayload.BodyOneofCase"/> — pipe UI chỉ gọi đúng 1 hàm này (mục 3, kênh UI chỉ nhận message domain Password/Auth).</summary>
    public Task<IpcPayload> HandleAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        _pendingAfterSendCleanup = null; // phòng hờ — request trước đó (nếu có) lẽ ra đã tự consume qua ZeroRecoveryKeyPlaintextAfterSend.
        return request.BodyCase switch
        {
            IpcPayload.BodyOneofCase.AuthStatusQuery => HandleAuthStatusQueryAsync(request),
            IpcPayload.BodyOneofCase.SetInitialPasswordReq => HandleSetInitialPasswordAsync(request),
            IpcPayload.BodyOneofCase.ConfirmRecoveryReq => HandleConfirmRecoveryKeySavedAsync(request),
            IpcPayload.BodyOneofCase.AuthVerifyReq => HandleAuthVerifyAsync(request, cancellationToken),
            IpcPayload.BodyOneofCase.ChangePasswordReq => HandleChangePasswordAsync(request, cancellationToken),
            IpcPayload.BodyOneofCase.RecoveryResetReq => HandleRecoveryResetAsync(request, cancellationToken),
            _ => throw new InvalidOperationException($"AuthCoordinator received unexpected message: {request.BodyCase}."),
        };
    }

    /// <summary>
    /// Gọi bởi <c>UiSessionServer</c> ngay sau khi response vừa rồi đã ghi xong vào pipe (thành công
    /// hay lỗi giữa chừng đều gọi — mục 5.5 bước 2-3, ADR-83). Zero Recovery Key plaintext (pinned
    /// buffer gốc + buffer <c>ByteString</c> nội bộ Protobuf vừa gửi) nếu response đó có mang; no-op
    /// cho mọi response khác (đa số message không có gì để zero ở đây).
    /// </summary>
    internal void ZeroRecoveryKeyPlaintextAfterSend() => Interlocked.Exchange(ref _pendingAfterSendCleanup, null)?.Invoke();

    private Task<IpcPayload> HandleAuthStatusQueryAsync(IpcPayload request)
    {
        bool configured = _dataCorrupt || AuthDataStore.Exists(_authDatPath);
        IpcPayload response = NewResponse(request);
        response.AuthStatusResp = new AuthStatusResponse { PasswordConfigured = configured };
        return Task.FromResult(response);
    }

    /// <summary>Mục 7.1 — không ghi <c>auth.dat</c> ngay (PWD-030a), chỉ giữ trong <see cref="AuthState.PendingSetup"/>.</summary>
    private async Task<IpcPayload> HandleSetInitialPasswordAsync(IpcPayload request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            IpcPayload response = NewResponse(request);
            if (_dataCorrupt || AuthDataStore.Exists(_authDatPath))
            {
                response.SetInitialPasswordResp = new SetInitialPasswordResponse { Result = SetupResult.AlreadyConfigured };
                return response;
            }

            byte[] passwordBytes = CredentialBytes.UnsafeGetBuffer(request.SetInitialPasswordReq.Password);
            try
            {
                if (Encoding.UTF8.GetCharCount(passwordBytes) > 50) // PWD-002a
                {
                    response.SetInitialPasswordResp = new SetInitialPasswordResponse { Result = SetupResult.PasswordTooLong };
                    return response;
                }

                string passwordHashPhc = Argon2idHasher.Hash(passwordBytes, Argon2Params.Official);

                string canonicalRecoveryKey = RecoveryKeyGenerator.GenerateCanonical();
                string recoveryKeyHashPhc;
                byte[] recoveryKeyCanonicalUtf8 = Encoding.UTF8.GetBytes(canonicalRecoveryKey);
                try
                {
                    recoveryKeyHashPhc = Argon2idHasher.Hash(recoveryKeyCanonicalUtf8, Argon2Params.Official);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(recoveryKeyCanonicalUtf8);
                }

                string groupedRecoveryKey = RecoveryKeyGenerator.FormatGrouped(canonicalRecoveryKey);
                byte[] setupToken = RandomNumberGenerator.GetBytes(16);

                // PendingSetup (mục 5.5 điểm 4, ADR-83) chỉ giữ hash — không còn plaintext để zero
                // ở đây; gọi lại SetInitialPasswordRequest lần 2 trước Confirm chỉ đơn thuần ghi đè
                // property, không có buffer "mồ côi" nào cần dọn (khác bản v0.1.0/v0.2.0).
                _state.PendingSetup = new PendingSetup
                {
                    PasswordHashPhc = passwordHashPhc,
                    RecoveryKeyHashPhc = recoveryKeyHashPhc,
                    SetupToken = setupToken,
                    CreatedAtUnixMs = _clock.UtcNowUnixMs,
                };

                // Buffer pinned riêng cho GIÁ TRỊ HIỂN THỊ (khác _state.PendingSetup — không lưu ở
                // đó nữa) — zero SAU KHI response đã ghi xong pipe, không phải sau khi hash xong
                // (mục 5.5 điểm 1-3, ADR-83: "dùng xong" = đã chuyển giao cho UI).
                byte[] pinnedRecoveryKeyDisplay = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(groupedRecoveryKey), pinned: true);
                Encoding.UTF8.GetBytes(groupedRecoveryKey, pinnedRecoveryKeyDisplay);

                var setupResp = new SetInitialPasswordResponse
                {
                    Result = SetupResult.Success,
                    RecoveryKeyPlaintext = ByteString.CopyFrom(pinnedRecoveryKeyDisplay),
                    SetupToken = ByteString.CopyFrom(setupToken),
                };
                response.SetInitialPasswordResp = setupResp;
                _pendingAfterSendCleanup = () =>
                {
                    CryptographicOperations.ZeroMemory(pinnedRecoveryKeyDisplay);
                    CredentialBytes.Zero(CredentialBytes.UnsafeGetBuffer(setupResp.RecoveryKeyPlaintext));
                };
                return response;
            }
            finally
            {
                CredentialBytes.Zero(passwordBytes);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mục 7.1 — ghi <c>auth.dat</c> đúng 1 lần, chỉ khi <c>confirmed=true</c> và token còn hạn.</summary>
    private async Task<IpcPayload> HandleConfirmRecoveryKeySavedAsync(IpcPayload request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            IpcPayload response = NewResponse(request);
            ConfirmRecoveryKeySavedRequest req = request.ConfirmRecoveryReq;
            PendingSetup? pending = _state.PendingSetup;

            if (pending is null || !pending.TokenMatches(CredentialBytes.UnsafeGetBuffer(req.SetupToken)))
            {
                // Token không khớp bất kỳ pending nào đang giữ — coi như Service đã restart giữa
                // chừng (mất RAM) hoặc UI gửi token cũ/sai (mục 7.1: "Service vừa restart giữa
                // chừng... coi như chưa thành công, quay lại từ đầu").
                response.ConfirmRecoveryResp = new ConfirmRecoveryKeySavedResponse { Result = ConfirmResult.TokenNotFound };
                return response;
            }

            if (pending.IsExpired(_clock.UtcNowUnixMs))
            {
                _state.PendingSetup = null;
                response.ConfirmRecoveryResp = new ConfirmRecoveryKeySavedResponse { Result = ConfirmResult.TokenExpired };
                return response;
            }

            if (!req.Confirmed)
            {
                // UI chỉ được kỳ vọng gửi request này SAU KHI đã tick xác nhận (mục 7.1) — nếu
                // vẫn nhận confirmed=false, không persist gì và KHÔNG xoá pending (để lần gọi đúng
                // trong cùng cửa sổ 30 phút vẫn dùng lại được), trả cùng mã như "chưa có gì để
                // dùng" — tránh phát minh thêm enum ngoài Architecture/08 đã Approved.
                response.ConfirmRecoveryResp = new ConfirmRecoveryKeySavedResponse { Result = ConfirmResult.TokenNotFound };
                return response;
            }

            long now = _clock.UtcNowUnixMs;
            var data = new AuthData(
                new PasswordEntryData(pending.PasswordHashPhc, Argon2ParamsData.From(Argon2Params.Official), now),
                new RecoveryKeyEntryData(pending.RecoveryKeyHashPhc, Argon2ParamsData.From(Argon2Params.Official), now, Used: false),
                RateLimitData.Initial);
            AuthDataStore.Save(_authDatPath, data);
            _cached = data;
            _dataCorrupt = false;

            _state.PendingSetup = null;

            await _auditLog.AppendAsync("PasswordInitialSetup", new { }, CancellationToken.None).ConfigureAwait(false);

            response.ConfirmRecoveryResp = new ConfirmRecoveryKeySavedResponse { Result = ConfirmResult.Persisted };
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mục 7.2 — cổng xác thực chung, phát hành <c>action_token</c> ngắn hạn khi thành công.</summary>
    private async Task<IpcPayload> HandleAuthVerifyAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IpcPayload response = NewResponse(request);
            AuthVerifyRequest req = request.AuthVerifyReq;
            long trustedNow = _clock.UtcNowUnixMs;

            if (_dataCorrupt || _cached is null)
            {
                // _cached is null: chưa từng Setup — UI lẽ ra phải chặn luồng này qua AuthStatusQuery
                // trước, nhưng Service không tin input IPC (DEV-040): trả "sai" thay vì throw/crash,
                // không có gì để RegisterFailureAsync tăng đếm (không có auth.dat để ghi).
                // Bù thời gian (mục 7.9, ADR-84) — chạy Argon2id thật trên chính input vừa nhận rồi
                // huỷ kết quả, để thời gian phản hồi không lộ oracle "auth.dat corrupt/chưa-setup"
                // qua chênh lệch so với nhánh sai mật khẩu thật bên dưới.
                byte[] decoyPasswordBytes = CredentialBytes.UnsafeGetBuffer(req.Password);
                try
                {
                    await RunDecoyArgon2idAsync(decoyPasswordBytes).ConfigureAwait(false);
                }
                finally
                {
                    CredentialBytes.Zero(decoyPasswordBytes);
                }

                response.AuthVerifyResp = new AuthVerifyResponse { Result = AuthResult.WrongPassword };
                return response;
            }

            if (IsLockedOut(trustedNow, out long lockoutUntil))
            {
                response.AuthVerifyResp = new AuthVerifyResponse
                {
                    Result = AuthResult.LockedOut,
                    LockoutUntilUnixMs = lockoutUntil,
                    ConsecutiveFailures = (uint)_cached!.RateLimit.ConsecutiveFailures,
                };
                return response;
            }

            byte[] passwordBytes = CredentialBytes.UnsafeGetBuffer(req.Password);
            bool correct;
            try
            {
                correct = Argon2idHasher.Verify(passwordBytes, _cached!.Password.HashPhc);
            }
            finally
            {
                CredentialBytes.Zero(passwordBytes);
            }

            if (correct)
            {
                ResetRateLimit();

                byte[] actionToken = RandomNumberGenerator.GetBytes(16);
                long expiresAt = trustedNow + (long)PendingActionToken.Ttl.TotalMilliseconds;
                _state.PendingActionTokens[Convert.ToHexString(actionToken)] = new PendingActionToken { ActionContext = req.ActionContext, ExpiresAtUnixMs = expiresAt };

                await _auditLog.AppendAsync("AuthAttempt", new { result = "success", action_context = req.ActionContext }, CancellationToken.None).ConfigureAwait(false);

                response.AuthVerifyResp = new AuthVerifyResponse
                {
                    Result = AuthResult.Success,
                    ActionToken = ByteString.CopyFrom(actionToken),
                    ActionTokenExpiresAtUnixMs = expiresAt,
                };
                return response;
            }

            RateLimitData updated = await RegisterFailureAsync(trustedNow, req.ActionContext).ConfigureAwait(false);
            response.AuthVerifyResp = new AuthVerifyResponse
            {
                Result = AuthResult.WrongPassword,
                ConsecutiveFailures = (uint)updated.ConsecutiveFailures,
            };
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mục 7.4 — nhúng thẳng <c>old_password</c> làm bằng chứng xác thực, không qua <c>action_token</c> (ADR-79).</summary>
    private async Task<IpcPayload> HandleChangePasswordAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IpcPayload response = NewResponse(request);
            ChangePasswordRequest req = request.ChangePasswordReq;
            long trustedNow = _clock.UtcNowUnixMs;

            if (_dataCorrupt || _cached is null)
            {
                // Bù thời gian (mục 7.9, ADR-84) — cùng lý do đã nêu ở HandleAuthVerifyAsync.
                byte[] decoyOldPasswordBytes = CredentialBytes.UnsafeGetBuffer(req.OldPassword);
                try
                {
                    await RunDecoyArgon2idAsync(decoyOldPasswordBytes).ConfigureAwait(false);
                }
                finally
                {
                    CredentialBytes.Zero(decoyOldPasswordBytes);
                }

                response.ChangePasswordResp = new ChangePasswordResponse { Result = ChangeResult.WrongOldPassword };
                return response;
            }

            if (IsLockedOut(trustedNow, out _))
            {
                response.ChangePasswordResp = new ChangePasswordResponse { Result = ChangeResult.LockedOut };
                return response;
            }

            byte[] oldPasswordBytes = CredentialBytes.UnsafeGetBuffer(req.OldPassword);
            bool correct;
            try
            {
                correct = Argon2idHasher.Verify(oldPasswordBytes, _cached!.Password.HashPhc);
            }
            finally
            {
                CredentialBytes.Zero(oldPasswordBytes);
            }

            if (!correct)
            {
                await RegisterFailureAsync(trustedNow, "change_password").ConfigureAwait(false);
                response.ChangePasswordResp = new ChangePasswordResponse { Result = ChangeResult.WrongOldPassword };
                return response;
            }

            // Không cùng lỗi thứ tự FAIL 2 (RecoveryReset, mục 7.5): xác thực old_password KHÔNG
            // tiêu thụ/vô hiệu hoá bất kỳ credential nào (không có khái niệm "used" như Recovery
            // Key) — dừng sớm ở bước kiểm tra độ dài new_password bên dưới không để lại trạng thái
            // lấp lửng nào, old_password vẫn dùng lại được bình thường cho lần thử kế tiếp.
            byte[] newPasswordBytes = CredentialBytes.UnsafeGetBuffer(req.NewPassword);
            try
            {
                if (Encoding.UTF8.GetCharCount(newPasswordBytes) > 50)
                {
                    response.ChangePasswordResp = new ChangePasswordResponse { Result = ChangeResult.NewPasswordTooLong };
                    return response;
                }

                string newHashPhc = Argon2idHasher.Hash(newPasswordBytes, Argon2Params.Official);
                RecoveryKeyEntryData recoveryKey = _cached!.RecoveryKey;
                byte[]? newRecoveryKeyPinnedDisplay = null;
                if (req.RegenerateRecoveryKey)
                {
                    (recoveryKey, newRecoveryKeyPinnedDisplay) = GenerateNewRecoveryKeyEntry(trustedNow);
                }

                var data = new AuthData(new PasswordEntryData(newHashPhc, Argon2ParamsData.From(Argon2Params.Official), trustedNow), recoveryKey, RateLimitData.Initial);
                AuthDataStore.Save(_authDatPath, data);
                _cached = data;

                await _auditLog.AppendAsync("ConfigChanged", new { field = "password" }, CancellationToken.None).ConfigureAwait(false);

                var changeResp = new ChangePasswordResponse { Result = ChangeResult.Success };
                response.ChangePasswordResp = changeResp;
                if (newRecoveryKeyPinnedDisplay is not null)
                {
                    changeResp.NewRecoveryKeyPlaintext = ByteString.CopyFrom(newRecoveryKeyPinnedDisplay);
                    _pendingAfterSendCleanup = () =>
                    {
                        CryptographicOperations.ZeroMemory(newRecoveryKeyPinnedDisplay);
                        CredentialBytes.Zero(CredentialBytes.UnsafeGetBuffer(changeResp.NewRecoveryKeyPlaintext));
                    };
                }

                return response;
            }
            finally
            {
                CredentialBytes.Zero(newPasswordBytes);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mục 7.5 — Recovery Key thay cho mật khẩu; luôn sinh key mới bắt buộc khi thành công (PWD-032).</summary>
    private async Task<IpcPayload> HandleRecoveryResetAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IpcPayload response = NewResponse(request);
            RecoveryResetRequest req = request.RecoveryResetReq;
            long trustedNow = _clock.UtcNowUnixMs;

            if (_dataCorrupt || _cached is null)
            {
                // Bù thời gian (mục 7.9, ADR-84) — chuẩn hoá input rồi hash mồi, đúng thứ tự nhánh
                // thật bên dưới sẽ làm với Recovery Key thật (mục 6.2).
                byte[] rawRecoveryKeyBytes = CredentialBytes.UnsafeGetBuffer(req.RecoveryKey);
                byte[]? decoyNormalized = null;
                try
                {
                    decoyNormalized = RecoveryKeyGenerator.NormalizeUtf8(rawRecoveryKeyBytes);
                    await RunDecoyArgon2idAsync(decoyNormalized).ConfigureAwait(false);
                }
                finally
                {
                    CredentialBytes.Zero(rawRecoveryKeyBytes);
                    if (decoyNormalized is not null)
                    {
                        CryptographicOperations.ZeroMemory(decoyNormalized);
                    }
                }

                response.RecoveryResetResp = new RecoveryResetResponse { Result = RecoveryResetResult.WrongRecoveryKey };
                return response;
            }

            if (IsLockedOut(trustedNow, out long lockoutUntil))
            {
                response.RecoveryResetResp = new RecoveryResetResponse { Result = RecoveryResetResult.LockedOut, LockoutUntilUnixMs = lockoutUntil };
                return response;
            }

            // FAIL 2 fix (security-privacy-auditor, Đợt 3): kiểm tra độ dài new_password TRƯỚC KHI
            // verify Recovery Key — thứ tự cũ (verify trước, kiểm tra độ dài sau) để lại đường
            // "verify đúng nhưng dừng giữa chừng vì new_password quá dài": không có auth.dat nào
            // được ghi (Recovery Key KHÔNG bị đánh dấu Used), nhưng cũng không tính vào rate-limit
            // (RegisterFailureAsync không được gọi cho nhánh "đúng") — kết quả là Recovery Key vẫn
            // dùng lại được nguyên vẹn ở lượt sau, coi như chưa từng bị tiêu thụ. Kiểm tra độ dài
            // trước loại bỏ hoàn toàn đường này: không có nhánh nào "verify thành công nhưng chưa
            // hoàn tất" nữa — new_password không hợp lệ thì Recovery Key chưa từng được đụng tới.
            byte[] newPasswordBytes = CredentialBytes.UnsafeGetBuffer(req.NewPassword);
            try
            {
                if (Encoding.UTF8.GetCharCount(newPasswordBytes) > 50)
                {
                    response.RecoveryResetResp = new RecoveryResetResponse { Result = RecoveryResetResult.NewPasswordTooLong };
                    return response;
                }

                byte[] rawRecoveryKeyBytes = CredentialBytes.UnsafeGetBuffer(req.RecoveryKey);
                byte[]? normalized = null;
                bool correct;
                try
                {
                    normalized = RecoveryKeyGenerator.NormalizeUtf8(rawRecoveryKeyBytes); // mục 6.2 — lặp lại chuẩn hoá phía Service, không tin UI
                    correct = !_cached!.RecoveryKey.Used && Argon2idHasher.Verify(normalized, _cached.RecoveryKey.HashPhc);
                }
                finally
                {
                    CredentialBytes.Zero(rawRecoveryKeyBytes);
                    if (normalized is not null)
                    {
                        CryptographicOperations.ZeroMemory(normalized);
                    }
                }

                if (!correct)
                {
                    await RegisterFailureAsync(trustedNow, "recovery_reset").ConfigureAwait(false);
                    response.RecoveryResetResp = new RecoveryResetResponse { Result = RecoveryResetResult.WrongRecoveryKey };
                    return response;
                }

                string newHashPhc = Argon2idHasher.Hash(newPasswordBytes, Argon2Params.Official);
                (RecoveryKeyEntryData newRecoveryKeyEntry, byte[] newRecoveryKeyPinnedDisplay) = GenerateNewRecoveryKeyEntry(trustedNow);

                var data = new AuthData(new PasswordEntryData(newHashPhc, Argon2ParamsData.From(Argon2Params.Official), trustedNow), newRecoveryKeyEntry, RateLimitData.Initial);
                AuthDataStore.Save(_authDatPath, data);
                _cached = data;

                await _auditLog.AppendAsync("AuthAttempt", new { result = "recovery_success" }, CancellationToken.None).ConfigureAwait(false);

                var recoveryResp = new RecoveryResetResponse { Result = RecoveryResetResult.Success, NewRecoveryKeyPlaintext = ByteString.CopyFrom(newRecoveryKeyPinnedDisplay) };
                response.RecoveryResetResp = recoveryResp;
                _pendingAfterSendCleanup = () =>
                {
                    CryptographicOperations.ZeroMemory(newRecoveryKeyPinnedDisplay);
                    CredentialBytes.Zero(CredentialBytes.UnsafeGetBuffer(recoveryResp.NewRecoveryKeyPlaintext));
                };
                return response;
            }
            finally
            {
                CredentialBytes.Zero(newPasswordBytes);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private (RecoveryKeyEntryData Entry, byte[] PlaintextUtf8Pinned) GenerateNewRecoveryKeyEntry(long nowUnixMs)
    {
        string canonical = RecoveryKeyGenerator.GenerateCanonical();
        byte[] canonicalUtf8 = Encoding.UTF8.GetBytes(canonical);
        string hashPhc;
        try
        {
            hashPhc = Argon2idHasher.Hash(canonicalUtf8, Argon2Params.Official);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonicalUtf8);
        }

        string grouped = RecoveryKeyGenerator.FormatGrouped(canonical);
        byte[] pinnedDisplay = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(grouped), pinned: true);
        Encoding.UTF8.GetBytes(grouped, pinnedDisplay);

        return (new RecoveryKeyEntryData(hashPhc, Argon2ParamsData.From(Argon2Params.Official), nowUnixMs, Used: false), pinnedDisplay);
    }

    /// <summary>
    /// Bù thời gian chống timing oracle (mục 7.9, ADR-84) — chạy Argon2id THẬT trên
    /// <paramref name="attackerSuppliedBytes"/> ghép salt mồi <see cref="AuthState.DecoySalt"/>,
    /// huỷ kết quả ngay, không so sánh/trả về gì. KHÔNG bọc <c>Task.Run</c>: nhánh verify thật
    /// (<see cref="Argon2idHasher.Verify"/>, gọi trực tiếp ở các <c>Handle*Async</c> khác) cũng
    /// không offload sang threadpool riêng — giữ đúng cùng kiểu lập lịch để không lộ thêm oracle
    /// qua khác biệt threading (mục 7.9: "khớp cả pattern lập lịch, không chỉ khớp tổng thời gian").
    /// </summary>
    private Task RunDecoyArgon2idAsync(byte[] attackerSuppliedBytes)
    {
        Argon2idHasher.ComputeAndDiscard(attackerSuppliedBytes, _state.DecoySalt, Argon2Params.Official);
        return Task.CompletedTask;
    }

    /// <summary>Mục 7.7 bước 1 — kiểm tra lockout TRƯỚC KHI hash, không tính thêm vào bộ đếm.</summary>
    private bool IsLockedOut(long trustedNow, out long lockoutUntilUnixMs)
    {
        long? delayUntil = _cached?.RateLimit.DelayUntilUnixMs;
        if (delayUntil is long until && trustedNow < until)
        {
            lockoutUntilUnixMs = until;
            return true;
        }

        lockoutUntilUnixMs = 0;
        return false;
    }

    /// <summary>Mục 7.7 — bộ đếm CHUNG cho mật khẩu lẫn Recovery Key (ADR-76), ghi auth.dat đồng bộ trước khi trả response.</summary>
    private async Task<RateLimitData> RegisterFailureAsync(long trustedNow, string actionContext)
    {
        int consecutive = (_cached?.RateLimit.ConsecutiveFailures ?? 0) + 1;
        long delayMs = RateLimitPolicy.DelayMsFor(consecutive);
        var updated = new RateLimitData(consecutive, trustedNow, delayMs > 0 ? trustedNow + delayMs : null);

        _cached = _cached! with { RateLimit = updated };
        AuthDataStore.Save(_authDatPath, _cached);

        await _auditLog.AppendAsync("AuthAttempt", new { result = "wrong_password", action_context = actionContext }, CancellationToken.None).ConfigureAwait(false);
        if (consecutive >= RateLimitPolicy.BruteForceAuditThreshold)
        {
            await _auditLog.AppendAsync("AuthBruteForceThresholdReached", new { consecutive_failures = consecutive, action_context = actionContext }, CancellationToken.None).ConfigureAwait(false);
        }

        return updated;
    }

    private void ResetRateLimit()
    {
        if (_cached!.RateLimit.ConsecutiveFailures == 0)
        {
            return;
        }

        _cached = _cached with { RateLimit = RateLimitData.Initial };
        AuthDataStore.Save(_authDatPath, _cached);
    }

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);

    /// <summary>Test hook (giống pattern <c>PixelBufferForTest</c> ở Vision, IMG-003) — CHỈ để regression test zero-out, không dùng trong production code.</summary>
    internal PendingSetup? PendingSetupForTest => _state.PendingSetup;
}

using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Services;

public enum ParentLoginOutcome
{
    Success,
    WrongPassword,
    LockedOut,
    Failed,
}

public sealed record ParentLoginResult(ParentLoginOutcome Outcome, uint ConsecutiveFailures = 0, long LockoutUntilUnixMs = 0, string? ErrorMessage = null);

/// <summary>
/// Phiên đăng nhập phụ huynh dùng chung tab Lịch sử (`S3`) và Cài đặt (`S4`) — `PWD-024`, `FE-080`–`083`,
/// Architecture/10 mục 6.8. Service là nguồn sự thật (kiểm tra phiên ở mọi request, ADR-150); lớp này chỉ
/// phản chiếu trạng thái cho giao diện và tự khoá lại sau <see cref="IdleTimeout"/> không có thao tác người dùng.
/// <para>
/// Cũng là <see cref="IAuthPromptService"/> cho các ViewModel đã viết theo mẫu `S5` (whitelist, lịch sử): đã đăng
/// nhập → trả token rỗng (Service chấp nhận qua phiên); chưa đăng nhập → <c>null</c> (coi như huỷ) — không bao
/// giờ mở hộp thoại mật khẩu riêng cho 2 tab này nữa.
/// </para>
/// </summary>
public sealed class ParentSessionService(IAuthFacade authFacade, IParentSessionFacade sessionFacade, Func<DateTimeOffset>? utcNow = null) : IAuthPromptService
{
    public const string ActionContext = "parent_session";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>KEEPALIVE tối đa 1 lần/khoảng này khi có thao tác (Architecture/08 mục 7.10 bước 6).</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(60);

    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset _lastActivity;
    private DateTimeOffset _lastKeepAlive;
    private bool _keepAliveInFlight;

    public bool IsLoggedIn { get; private set; }

    /// <summary>Bắn mỗi khi <see cref="IsLoggedIn"/> đổi (đăng nhập, đăng xuất, hết hạn, Service từ chối phiên).</summary>
    public event EventHandler? SessionChanged;

    /// <summary>
    /// <paramref name="passwordUtf8Pinned"/>: buffer pinned caller vừa đọc từ <c>PasswordBox</c> — bị zero trong
    /// <see cref="IAuthFacade.AuthVerifyAsync"/> bất kể kết quả (Architecture/08 mục 5.3).
    /// </summary>
    public async Task<ParentLoginResult> LoginAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
    {
        try
        {
            AuthVerifyResult verify = await authFacade.AuthVerifyAsync(passwordUtf8Pinned, ActionContext, cancellationToken).ConfigureAwait(true);
            switch (verify.Outcome)
            {
                case AuthOutcome.WrongPassword:
                    return new ParentLoginResult(ParentLoginOutcome.WrongPassword, verify.ConsecutiveFailures);
                case AuthOutcome.LockedOut:
                    return new ParentLoginResult(ParentLoginOutcome.LockedOut, verify.ConsecutiveFailures, verify.LockoutUntilUnixMs);
            }

            if (verify.ActionToken is not { } token)
            {
                return new ParentLoginResult(ParentLoginOutcome.Failed);
            }

            ParentSessionOutcome opened = await sessionFacade.OpenAsync(token, cancellationToken).ConfigureAwait(true);
            if (opened != ParentSessionOutcome.Success)
            {
                return new ParentLoginResult(ParentLoginOutcome.Failed);
            }

            DateTimeOffset now = _utcNow();
            _lastActivity = now;
            _lastKeepAlive = now;
            SetLoggedIn(true);
            return new ParentLoginResult(ParentLoginOutcome.Success);
        }
        catch (UiIpcConnectionException ex)
        {
            return new ParentLoginResult(ParentLoginOutcome.Failed, ErrorMessage: ex.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordUtf8Pinned);
        }
    }

    /// <summary>Nút Đăng xuất / hết hạn idle — luôn khoá giao diện ngay, báo Service best-effort.</summary>
    public async Task LogoutAsync()
    {
        bool wasLoggedIn = IsLoggedIn;
        SetLoggedIn(false);
        if (!wasLoggedIn)
        {
            return;
        }

        try
        {
            await sessionFacade.CloseAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (UiIpcConnectionException)
        {
            // Kết nối đã mất thì phiên phía Service cũng đã chết theo (ADR-149).
        }
    }

    /// <summary>Service từ chối phiên (hết hạn/kết nối mới) — chỉ cập nhật giao diện, không gửi gì thêm.</summary>
    public void MarkLoggedOut() => SetLoggedIn(false);

    /// <summary>Gọi khi người dùng bấm chuột/cuộn/gõ phím trong cửa sổ Dashboard.</summary>
    public void NotifyActivity()
    {
        if (!IsLoggedIn)
        {
            return;
        }

        DateTimeOffset now = _utcNow();
        if (now - _lastActivity >= IdleTimeout)
        {
            // Thao tác đầu tiên SAU khi đã quá hạn không được hồi sinh phiên.
            _ = LogoutAsync();
            return;
        }

        _lastActivity = now;
        if (!_keepAliveInFlight && now - _lastKeepAlive >= KeepAliveInterval)
        {
            _ = KeepAliveAsync(now);
        }
    }

    /// <summary>Gọi định kỳ (timer UI) — khoá lại khi đủ <see cref="IdleTimeout"/> không có thao tác.</summary>
    public void CheckIdle()
    {
        if (IsLoggedIn && _utcNow() - _lastActivity >= IdleTimeout)
        {
            _ = LogoutAsync();
        }
    }

    /// <inheritdoc/>
    public Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot) =>
        Task.FromResult<byte[]?>(IsLoggedIn ? [] : null);

    private async Task KeepAliveAsync(DateTimeOffset now)
    {
        _keepAliveInFlight = true;
        _lastKeepAlive = now;
        try
        {
            ParentSessionOutcome outcome = await sessionFacade.KeepAliveAsync(CancellationToken.None).ConfigureAwait(true);
            if (outcome != ParentSessionOutcome.Success)
            {
                MarkLoggedOut();
            }
        }
        catch (UiIpcConnectionException)
        {
            MarkLoggedOut();
        }
        finally
        {
            _keepAliveInFlight = false;
        }
    }

    private void SetLoggedIn(bool value)
    {
        if (IsLoggedIn == value)
        {
            return;
        }

        IsLoggedIn = value;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}

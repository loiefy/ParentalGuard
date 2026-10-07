using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>`PWD-024`/`FE-080`–`083` (Architecture/10 mục 6.8) — phiên đăng nhập phụ huynh phía UI.</summary>
public class ParentSessionServiceTests
{
    private DateTimeOffset _now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private ParentSessionService Create(FakeAuthFacade auth, FakeSessionFacade session) => new(auth, session, () => _now);

    private static byte[] Password() => "Passw0rd!"u8.ToArray();

    [Fact]
    public async Task Login_Success_OpensSessionWithParentSessionContext_AndRaisesChanged()
    {
        var auth = new FakeAuthFacade { Outcome = AuthOutcome.Success };
        var session = new FakeSessionFacade();
        ParentSessionService service = Create(auth, session);
        int changed = 0;
        service.SessionChanged += (_, _) => changed++;
        byte[] password = Password();

        ParentLoginResult result = await service.LoginAsync(password, CancellationToken.None);

        Assert.Equal(ParentLoginOutcome.Success, result.Outcome);
        Assert.True(service.IsLoggedIn);
        Assert.Equal(1, changed);
        Assert.Equal("parent_session", auth.LastActionContext);
        Assert.Equal(1, session.OpenCalls);
        Assert.All(password, b => Assert.Equal(0, b)); // Architecture/08 mục 5.3 — buffer mật khẩu bị zero
    }

    [Fact]
    public async Task Login_WrongPassword_StaysLoggedOut_DoesNotOpen()
    {
        var auth = new FakeAuthFacade { Outcome = AuthOutcome.WrongPassword, Failures = 2 };
        var session = new FakeSessionFacade();
        ParentSessionService service = Create(auth, session);

        ParentLoginResult result = await service.LoginAsync(Password(), CancellationToken.None);

        Assert.Equal(ParentLoginOutcome.WrongPassword, result.Outcome);
        Assert.Equal(2u, result.ConsecutiveFailures);
        Assert.False(service.IsLoggedIn);
        Assert.Equal(0, session.OpenCalls);
    }

    [Fact]
    public async Task Login_LockedOut_ReturnsLockout()
    {
        var auth = new FakeAuthFacade { Outcome = AuthOutcome.LockedOut, LockoutUntil = 123 };
        ParentSessionService service = Create(auth, new FakeSessionFacade());

        ParentLoginResult result = await service.LoginAsync(Password(), CancellationToken.None);

        Assert.Equal(ParentLoginOutcome.LockedOut, result.Outcome);
        Assert.Equal(123, result.LockoutUntilUnixMs);
        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task Login_ServiceRejectsOpen_Failed()
    {
        var auth = new FakeAuthFacade { Outcome = AuthOutcome.Success };
        ParentSessionService service = Create(auth, new FakeSessionFacade { OpenOutcome = ParentSessionOutcome.InvalidToken });

        ParentLoginResult result = await service.LoginAsync(Password(), CancellationToken.None);

        Assert.Equal(ParentLoginOutcome.Failed, result.Outcome);
        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task Login_ConnectionLost_FailedWithMessage()
    {
        var auth = new FakeAuthFacade { Throw = true };
        ParentSessionService service = Create(auth, new FakeSessionFacade());

        ParentLoginResult result = await service.LoginAsync(Password(), CancellationToken.None);

        Assert.Equal(ParentLoginOutcome.Failed, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
    }

    /// <summary>`PWD-024`: 10 phút không thao tác → tự đăng xuất (báo Service CLOSE).</summary>
    [Fact]
    public async Task CheckIdle_After10MinutesWithoutActivity_LogsOut()
    {
        var session = new FakeSessionFacade();
        ParentSessionService service = Create(new FakeAuthFacade { Outcome = AuthOutcome.Success }, session);
        await service.LoginAsync(Password(), CancellationToken.None);

        _now += TimeSpan.FromMinutes(9);
        service.CheckIdle();
        Assert.True(service.IsLoggedIn);

        _now += TimeSpan.FromMinutes(1);
        service.CheckIdle();
        Assert.False(service.IsLoggedIn);
        Assert.Equal(1, session.CloseCalls);
    }

    [Fact]
    public async Task Activity_ResetsIdle_AndSendsKeepAliveAtMostOncePerMinute()
    {
        var session = new FakeSessionFacade();
        ParentSessionService service = Create(new FakeAuthFacade { Outcome = AuthOutcome.Success }, session);
        await service.LoginAsync(Password(), CancellationToken.None);

        _now += TimeSpan.FromSeconds(30);
        service.NotifyActivity();
        Assert.Equal(0, session.KeepAliveCalls);

        _now += TimeSpan.FromSeconds(40);
        service.NotifyActivity();
        service.NotifyActivity();
        Assert.Equal(1, session.KeepAliveCalls);

        _now += TimeSpan.FromMinutes(9);
        service.CheckIdle();
        Assert.True(service.IsLoggedIn); // hoạt động gần nhất cách đây 9 phút
    }

    [Fact]
    public async Task KeepAlive_NotActive_MarksLoggedOut()
    {
        var session = new FakeSessionFacade { KeepAliveOutcome = ParentSessionOutcome.NotActive };
        ParentSessionService service = Create(new FakeAuthFacade { Outcome = AuthOutcome.Success }, session);
        await service.LoginAsync(Password(), CancellationToken.None);

        _now += TimeSpan.FromMinutes(2);
        service.NotifyActivity();

        Assert.False(service.IsLoggedIn);
    }

    /// <summary>Thao tác đầu tiên SAU khi đã quá hạn không được hồi sinh phiên.</summary>
    [Fact]
    public async Task Activity_AfterIdleExpired_DoesNotRevive()
    {
        ParentSessionService service = Create(new FakeAuthFacade { Outcome = AuthOutcome.Success }, new FakeSessionFacade());
        await service.LoginAsync(Password(), CancellationToken.None);

        _now += TimeSpan.FromMinutes(11);
        service.NotifyActivity();

        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task Logout_ClosesSession_AndPromptReturnsNull()
    {
        var session = new FakeSessionFacade();
        ParentSessionService service = Create(new FakeAuthFacade { Outcome = AuthOutcome.Success }, session);
        await service.LoginAsync(Password(), CancellationToken.None);
        byte[]? token = await service.ShowAuthPromptAsync("manage_whitelist", null!);
        Assert.NotNull(token);
        Assert.Empty(token); // đã đăng nhập → token rỗng, Service chấp nhận qua phiên

        await service.LogoutAsync();

        Assert.False(service.IsLoggedIn);
        Assert.Equal(1, session.CloseCalls);
        Assert.Null(await service.ShowAuthPromptAsync("manage_whitelist", null!));
    }

    [Fact]
    public void LanguageCatalog_Vietnamese_IsBilingual_AllSixAvailable()
    {
        IReadOnlyList<LanguageOption> options = LanguageCatalog.Build("vi", code => "L-" + code, "sắp có", "coming soon");

        Assert.Equal(["vi", "en", "fr", "es", "pt", "zh-Hans"], options.Select(o => o.Code));
        Assert.Equal("L-vi (Vietnamese)", options[0].DisplayName);
        Assert.True(options[0].IsAvailable);
        Assert.Equal("L-fr (French)", options[2].DisplayName); // FE-063a: cả 6 ngôn ngữ đã có bản dịch
        Assert.All(options, o => Assert.True(o.IsAvailable));
        Assert.Equal("Ngôn ngữ / Language", LanguageCatalog.BuildHeader("vi", "Ngôn ngữ", "Language"));
    }

    [Fact]
    public void LanguageCatalog_English_ShowsEnglishOnly()
    {
        IReadOnlyList<LanguageOption> options = LanguageCatalog.Build("en", code => "L-" + code, "sắp có", "coming soon");

        Assert.Equal("Vietnamese", options[0].DisplayName);
        Assert.Equal("French", options[2].DisplayName);
        Assert.Equal("Language", LanguageCatalog.BuildHeader("en", "Ngôn ngữ", "Language"));
    }

    [Fact]
    public void LanguageCatalog_FromResources_CurrentIsVietnamese()
    {
        Assert.Equal("vi", LocalizationService.CurrentLanguageCode);
        Assert.Equal("Tiếng Việt (Vietnamese)", LanguageCatalog.BuildFromResources()[0].DisplayName);
    }

    internal sealed class FakeSessionFacade : IParentSessionFacade
    {
        public ParentSessionOutcome OpenOutcome { get; init; } = ParentSessionOutcome.Success;

        public ParentSessionOutcome KeepAliveOutcome { get; init; } = ParentSessionOutcome.Success;

        public int OpenCalls { get; private set; }

        public int KeepAliveCalls { get; private set; }

        public int CloseCalls { get; private set; }

        public Task<ParentSessionOutcome> OpenAsync(byte[] actionToken, CancellationToken cancellationToken)
        {
            OpenCalls++;
            return Task.FromResult(OpenOutcome);
        }

        public Task<ParentSessionOutcome> KeepAliveAsync(CancellationToken cancellationToken)
        {
            KeepAliveCalls++;
            return Task.FromResult(KeepAliveOutcome);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCalls++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeAuthFacade : IAuthFacade
    {
        public AuthOutcome Outcome { get; init; }

        public uint Failures { get; init; }

        public long LockoutUntil { get; init; }

        public bool Throw { get; init; }

        public string? LastActionContext { get; private set; }

        public Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken)
        {
            Array.Clear(passwordUtf8Pinned);
            LastActionContext = actionContext;
            if (Throw)
            {
                throw new UiIpcConnectionException("lost");
            }

            return Task.FromResult(new AuthVerifyResult(Outcome, Outcome == AuthOutcome.Success ? [1, 2, 3] : null, 0, LockoutUntil, Failures));
        }

        public Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ChangePasswordResult> ChangePasswordAsync(byte[] oldPasswordUtf8Pinned, byte[] newPasswordUtf8Pinned, bool regenerateRecoveryKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RecoveryResult> RecoveryResetAsync(byte[] recoveryKeyUtf8Pinned, byte[] newPasswordUtf8Pinned, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

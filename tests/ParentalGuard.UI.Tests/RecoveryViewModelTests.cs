using System.Text;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `RecoveryViewModel` (Architecture/10-ui-architecture.md mục 6.6, `PWD-032`/`033`) — cùng mẫu hình
/// <see cref="SettingsViewModelTests"/>/<see cref="OnboardingViewModelTests"/> (fake facade, không cần
/// `XamlRoot`/`ContentDialog` thật).
/// </summary>
public sealed class RecoveryViewModelTests
{
    [Theory]
    [InlineData("ABCD-1234-EFGH-5678", "ABCD1234EFGH5678")]
    [InlineData("abcd 1234 efgh", "ABCD1234EFGH")]
    [InlineData("  a-b-c  ", "ABC")]
    public void NormalizeRecoveryKey_StripsDashesAndWhitespace_UppercasesResult(string raw, string expected)
    {
        Assert.Equal(expected, RecoveryViewModel.NormalizeRecoveryKey(raw));
    }

    [Fact]
    public async Task SubmitAsync_Success_ShowsNewRecoveryKeyAndHidesForm()
    {
        byte[] newKeyBytes = "NEW-RECOVERY-KEY-1234"u8.ToArray();
        var authFacade = new FakeAuthFacade { Result = new RecoveryResult(RecoveryOutcome.Success, newKeyBytes, 0) };
        var viewModel = new RecoveryViewModel(authFacade);

        await viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.Equal("NEW-RECOVERY-KEY-1234", viewModel.NewRecoveryKeyDisplay);
        Assert.True(viewModel.HasNewRecoveryKeyDisplay);
        Assert.False(viewModel.IsFormVisible);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task SubmitAsync_WrongRecoveryKey_SetsErrorAndKeepsFormVisible()
    {
        var authFacade = new FakeAuthFacade { Result = new RecoveryResult(RecoveryOutcome.WrongRecoveryKey, null, 0) };
        var viewModel = new RecoveryViewModel(authFacade);

        await viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsFormVisible);
        Assert.False(viewModel.HasNewRecoveryKeyDisplay);
    }

    [Fact]
    public async Task SubmitAsync_LockedOut_DisablesSubmitAndShowsCountdown()
    {
        long lockoutUntil = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();
        var authFacade = new FakeAuthFacade { Result = new RecoveryResult(RecoveryOutcome.LockedOut, null, lockoutUntil) };
        var viewModel = new RecoveryViewModel(authFacade);

        await viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.False(viewModel.IsSubmitEnabled);
        Assert.False(viewModel.CanSubmit);
        Assert.True(viewModel.HasError);
    }

    /// <summary>Defense in depth — nút "Khôi phục" phải bị khoá trong lúc <c>SubmitAsync</c> đang chạy, tránh double-submit.</summary>
    [Fact]
    public void CanSubmit_WhileBusy_IsFalse()
    {
        var viewModel = new RecoveryViewModel(new FakeAuthFacade())
        {
            IsBusy = true,
        };

        Assert.False(viewModel.CanSubmit);
    }

    [Fact]
    public async Task SubmitAsync_NewPasswordTooLong_SetsError()
    {
        var authFacade = new FakeAuthFacade { Result = new RecoveryResult(RecoveryOutcome.NewPasswordTooLong, null, 0) };
        var viewModel = new RecoveryViewModel(authFacade);

        await viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SubmitAsync_ConnectionFailure_SetsErrorMessage()
    {
        var authFacade = new FakeAuthFacade { ThrowOnSubmit = true };
        var viewModel = new RecoveryViewModel(authFacade);

        await viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AcknowledgeNewRecoveryKeyDisplayed_ZeroesBufferAndClearsDisplay()
    {
        byte[] newKeyBytes = Encoding.UTF8.GetBytes("ZERO-ME-1234");
        var authFacade = new FakeAuthFacade { Result = new RecoveryResult(RecoveryOutcome.Success, newKeyBytes, 0) };
        var viewModel = new RecoveryViewModel(authFacade);
        await viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        viewModel.AcknowledgeNewRecoveryKeyDisplayed();

        Assert.Null(viewModel.NewRecoveryKeyDisplay);
        Assert.All(newKeyBytes, b => Assert.Equal(0, b));
    }

    /// <summary>Regression BUG B (đã sửa ở Onboarding/Settings) — đóng app giữa chừng trước khi bấm "Đã lưu" vẫn phải zero buffer.</summary>
    [Fact]
    public async Task Dispose_WhenRecoveryKeyBufferNotAcknowledged_ZeroesBuffer()
    {
        byte[] newKeyBytes = Encoding.UTF8.GetBytes("SAFETY-NET-1234");
        var authFacade = new FakeAuthFacade { Result = new RecoveryResult(RecoveryOutcome.Success, newKeyBytes, 0) };
        var viewModel = new RecoveryViewModel(authFacade);
        await viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        viewModel.Dispose();

        Assert.All(newKeyBytes, b => Assert.Equal(0, b));
        Assert.Null(viewModel.NewRecoveryKeyDisplay);
    }

    [Fact]
    public void Dispose_WhenNoRecoveryKeyBufferPresent_IsNoOp()
    {
        var viewModel = new RecoveryViewModel(new FakeAuthFacade());

        viewModel.Dispose();

        Assert.Null(viewModel.NewRecoveryKeyDisplay);
    }

    /// <summary>
    /// Regression security audit Đợt 6 S6 (bug đã sửa) — Cancel/đóng app (<c>MarkDiscarded</c>) TRONG LÚC
    /// <c>SubmitAsync</c> còn treo IPC: Success đến SAU thời điểm đó không được publish lên
    /// <c>NewRecoveryKeyDisplay</c> của ViewModel mồ côi, buffer phải zero ngay (không có ai để
    /// "Acknowledge" nữa — trước bug fix, buffer này KHÔNG BAO GIỜ bị zero).
    /// </summary>
    [Fact]
    public async Task SubmitAsync_MarkDiscardedWhilePending_LateSuccessZeroesBufferWithoutPublishing()
    {
        var pendingResult = new TaskCompletionSource<RecoveryResult>();
        var authFacade = new FakeAuthFacade { PendingResult = pendingResult.Task };
        var viewModel = new RecoveryViewModel(authFacade);

        Task submitTask = viewModel.SubmitAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);
        Assert.True(viewModel.IsBusy);

        // Cùng thời điểm RecoveryPage.OnNavigatedFrom/App.OnWindowClosed gọi khi Cancel/đóng app giữa chừng.
        viewModel.MarkDiscarded();

        byte[] newKeyBytes = Encoding.UTF8.GetBytes("ORPHANED-KEY-1234");
        pendingResult.SetResult(new RecoveryResult(RecoveryOutcome.Success, newKeyBytes, 0));
        await submitTask;

        Assert.Null(viewModel.NewRecoveryKeyDisplay);
        Assert.False(viewModel.HasNewRecoveryKeyDisplay);
        Assert.All(newKeyBytes, b => Assert.Equal(0, b));
    }

    private sealed class FakeAuthFacade : IAuthFacade
    {
        public RecoveryResult? Result { get; set; }

        public bool ThrowOnSubmit { get; set; }

        public Task<RecoveryResult>? PendingResult { get; set; }

        public Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ChangePasswordResult> ChangePasswordAsync(byte[] oldPasswordUtf8Pinned, byte[] newPasswordUtf8Pinned, bool regenerateRecoveryKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RecoveryResult> RecoveryResetAsync(byte[] recoveryKeyUtf8Pinned, byte[] newPasswordUtf8Pinned, CancellationToken cancellationToken)
            => ThrowOnSubmit
                ? throw new UiIpcConnectionException("Mất kết nối.")
                : PendingResult ?? Task.FromResult(Result ?? throw new InvalidOperationException("Result not configured"));
    }
}

using System.Text;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// Regression test cho BUG B (security audit Đợt 6, giai đoạn 1) — <see cref="OnboardingViewModel.Dispose"/>
/// là safety net khi user đóng app giữa chừng ở màn Recovery Key (trước khi Confirm), phải zero
/// buffer plaintext đang giữ.
/// </summary>
public sealed class OnboardingViewModelTests
{
    [Fact]
    public async Task Dispose_WhenRecoveryKeyBufferNotConfirmed_ZeroesBufferAndClearsDisplay()
    {
        byte[] recoveryKeyPlaintext = Encoding.UTF8.GetBytes("ABCD-1234-EFGH-5678");
        var authFacade = new FakeAuthFacade
        {
            SetInitialPasswordResult = new SetInitialPasswordResult(SetupOutcome.Success, recoveryKeyPlaintext, SetupToken: new byte[] { 1, 2, 3 }),
        };
        var viewModel = new OnboardingViewModel(authFacade);

        await viewModel.SubmitPasswordAsync(GC.AllocateArray<byte>(8, pinned: true), CancellationToken.None);
        Assert.Equal("ABCD-1234-EFGH-5678", viewModel.RecoveryKeyDisplay);

        // Mô phỏng user đóng app giữa chừng (App.OnWindowClosed gọi Dispose trên safety-net reference)
        // TRƯỚC khi tick "đã lưu" + Confirm — ConfirmRecoveryKeySavedAsync không bao giờ được gọi.
        viewModel.Dispose();

        Assert.All(recoveryKeyPlaintext, b => Assert.Equal(0, b));
        Assert.Null(viewModel.RecoveryKeyDisplay);
    }

    [Fact]
    public void Dispose_WhenNoRecoveryKeyBufferPresent_IsNoOp()
    {
        var viewModel = new OnboardingViewModel(new FakeAuthFacade());

        viewModel.Dispose();

        Assert.Null(viewModel.RecoveryKeyDisplay);
    }

    /// <summary>
    /// Regression bug 2026-09-30 — bấm "Tiếp tục" ở bước Đặt mật khẩu thì nút xám vĩnh viễn, không
    /// điều hướng: ViewModel dùng ConfigureAwait(false) nên sau khi IPC (hoàn thành trên thread pool)
    /// trả về, IsBusy=false + event điều hướng chạy ngoài UI thread → WinUI ném RPC_E_WRONG_THREAD.
    /// </summary>
    [Fact]
    public void SubmitPasswordAsync_WhenIpcCompletesOnThreadPool_RaisesPropertyChangedAndNavigationOnCallerContext()
    {
        var offThreadCallbacks = new List<string>();
        SingleThreadSynchronizationContext.Run(async context =>
        {
            var authFacade = new FakeAuthFacade
            {
                SetInitialPasswordResult = new SetInitialPasswordResult(SetupOutcome.Success, Encoding.UTF8.GetBytes("KEY"), SetupToken: [1, 2, 3]),
                CompleteOnThreadPool = true,
            };
            var viewModel = new OnboardingViewModel(authFacade);
            viewModel.PropertyChanged += (_, e) => Record(e.PropertyName!);
            viewModel.NavigateToRecoveryKeyRequested += (_, _) => Record("NavigateToRecoveryKeyRequested");

            void Record(string name)
            {
                if (Environment.CurrentManagedThreadId != context.ThreadId)
                {
                    offThreadCallbacks.Add(name);
                }
            }

            await viewModel.SubmitPasswordAsync(GC.AllocateArray<byte>(8, pinned: true), CancellationToken.None);

            Assert.False(viewModel.IsBusy);
            viewModel.Dispose();
        });

        Assert.Empty(offThreadCallbacks);
    }

    private sealed class FakeAuthFacade : IAuthFacade
    {
        public SetInitialPasswordResult? SetInitialPasswordResult { get; set; }

        /// <summary>Mô phỏng pipe IPC thật — task hoàn thành trên thread pool, không đồng bộ.</summary>
        public bool CompleteOnThreadPool { get; set; }

        public Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
        {
            SetInitialPasswordResult result = SetInitialPasswordResult ?? throw new InvalidOperationException("SetInitialPasswordResult not configured");
            return CompleteOnThreadPool
                ? Task.Run(async () => { await Task.Delay(20, CancellationToken.None).ConfigureAwait(false); return result; }, CancellationToken.None)
                : Task.FromResult(result);
        }

        public Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Không gọi trong kịch bản đóng app giữa chừng.");

        public Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ChangePasswordResult> ChangePasswordAsync(byte[] oldPasswordUtf8Pinned, byte[] newPasswordUtf8Pinned, bool regenerateRecoveryKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RecoveryResult> RecoveryResetAsync(byte[] recoveryKeyUtf8Pinned, byte[] newPasswordUtf8Pinned, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

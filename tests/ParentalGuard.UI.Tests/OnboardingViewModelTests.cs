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

    private sealed class FakeAuthFacade : IAuthFacade
    {
        public SetInitialPasswordResult? SetInitialPasswordResult { get; set; }

        public Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
            => Task.FromResult(SetInitialPasswordResult ?? throw new InvalidOperationException("SetInitialPasswordResult not configured"));

        public Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Không gọi trong kịch bản đóng app giữa chừng.");

        public Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ChangePasswordResult> ChangePasswordAsync(byte[] oldPasswordUtf8Pinned, byte[] newPasswordUtf8Pinned, bool regenerateRecoveryKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

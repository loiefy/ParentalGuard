using Microsoft.UI.Xaml;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `SettingsViewModel` (Architecture/10-ui-architecture.md mục 6.4) — cùng mẫu hình
/// <see cref="DashboardViewModelTests"/>/<see cref="AuditLogViewModelTests"/>: fake facade/
/// `IAuthPromptService`, không cần `XamlRoot`/`ContentDialog` thật (truyền <c>null!</c> đúng mẫu hình
/// đã dùng cho <c>AuditLogViewModelTests</c>, vì <see cref="FakeAuthPromptService"/> không đọc tham số đó).
/// </summary>
public sealed class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(
        FakeConfigFacade? configFacade = null,
        FakeAuthFacade? authFacade = null,
        IAuthPromptService? authPromptService = null)
        => new(configFacade ?? new FakeConfigFacade(), authFacade ?? new FakeAuthFacade(), authPromptService ?? new FakeAuthPromptService([[1]]));

    [Fact]
    public async Task InitializeAsync_Success_LoadsSnapshotIntoProperties()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot("Câu cảnh báo.", ["chrome.exe", "steam.exe"], PerformanceModeOption.MaximumProtection),
        };
        var viewModel = CreateViewModel(configFacade);

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.LoadErrorMessage);
        Assert.Equal("Câu cảnh báo.", viewModel.OverlayMessage);
        Assert.Equal(["chrome.exe", "steam.exe"], viewModel.WhitelistedProcessNames);
        Assert.Equal(PerformanceModeOption.MaximumProtection, viewModel.PerformanceMode);
    }

    [Fact]
    public async Task InitializeAsync_ConnectionFailure_SetsLoadErrorMessage()
    {
        var configFacade = new FakeConfigFacade { ThrowOnGet = true };
        var viewModel = CreateViewModel(configFacade);

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.HasLoadError);
    }

    [Fact]
    public async Task SaveOverlayMessageAsync_Success_SendsCurrentPerformanceMode_AndSetsStatus()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot(string.Empty, [], PerformanceModeOption.MaximumProtection),
            UpdateOutcomes = [ConfigUpdateOutcome.Success],
        };
        var viewModel = CreateViewModel(configFacade);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.OverlayMessage = "Noi dung moi.";

        await viewModel.SaveOverlayMessageAsync(CancellationToken.None);

        Assert.True(viewModel.HasOverlayMessageStatus);
        Assert.False(viewModel.HasOverlayMessageError);
        Assert.Equal("Noi dung moi.", Assert.Single(configFacade.OverlayMessagesReceived));
        // Mục 6.4 — "full update": PHẢI kèm performance_mode hiện hành đã lưu (MaximumProtection từ ConfigQuery), không phải mặc định Balanced.
        Assert.Equal(PerformanceModeOption.MaximumProtection, Assert.Single(configFacade.PerformanceModesReceived));
    }

    [Theory]
    [InlineData(ConfigUpdateOutcome.InvalidCharacters)]
    [InlineData(ConfigUpdateOutcome.TooLong)]
    public async Task SaveOverlayMessageAsync_ServiceRejects_RevertsToLastSavedValue(ConfigUpdateOutcome rejection)
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot("Ban dau.", [], PerformanceModeOption.Balanced),
            UpdateOutcomes = [rejection],
        };
        var viewModel = CreateViewModel(configFacade);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.OverlayMessage = "gia tri se bi tu choi";

        await viewModel.SaveOverlayMessageAsync(CancellationToken.None);

        Assert.Equal("Ban dau.", viewModel.OverlayMessage);
        Assert.True(viewModel.HasOverlayMessageError);
    }

    [Fact]
    public async Task ResetOverlayMessageToDefaultAsync_SetsEmptyAndSaves()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot("Khong con mac dinh.", [], PerformanceModeOption.Balanced),
            UpdateOutcomes = [ConfigUpdateOutcome.Success],
        };
        var viewModel = CreateViewModel(configFacade);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ResetOverlayMessageToDefaultAsync(CancellationToken.None);

        Assert.Equal(string.Empty, viewModel.OverlayMessage);
        Assert.Equal(string.Empty, Assert.Single(configFacade.OverlayMessagesReceived));
    }

    [Fact]
    public async Task SetPerformanceModeAsync_Success_UpdatesMode_SendsLastSavedOverlayMessage_NotDraft()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot("Da luu.", [], PerformanceModeOption.Balanced),
            UpdateOutcomes = [ConfigUpdateOutcome.Success],
        };
        var viewModel = CreateViewModel(configFacade);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.OverlayMessage = "ban nhap chua luu"; // KHÔNG gọi SaveOverlayMessageAsync

        await viewModel.SetPerformanceModeAsync(PerformanceModeOption.MaximumProtection, CancellationToken.None);

        Assert.Equal(PerformanceModeOption.MaximumProtection, viewModel.PerformanceMode);
        // "full update" phải gửi bản ĐÃ LƯU ("Da luu."), không phải bản đang gõ dở trong TextBox.
        Assert.Equal("Da luu.", Assert.Single(configFacade.OverlayMessagesReceived));
    }

    [Fact]
    public async Task SetPerformanceModeAsync_SameAsCurrent_DoesNotCallFacade()
    {
        var configFacade = new FakeConfigFacade { Snapshot = new ConfigSnapshot(string.Empty, [], PerformanceModeOption.Balanced) };
        var viewModel = CreateViewModel(configFacade);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.SetPerformanceModeAsync(PerformanceModeOption.Balanced, CancellationToken.None);

        Assert.Empty(configFacade.PerformanceModesReceived);
    }

    [Fact]
    public async Task SetPerformanceModeAsync_ServiceRejects_RevertsToPreviousMode()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot(string.Empty, [], PerformanceModeOption.Balanced),
            UpdateOutcomes = [ConfigUpdateOutcome.InvalidCharacters],
        };
        var viewModel = CreateViewModel(configFacade);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.SetPerformanceModeAsync(PerformanceModeOption.MaximumProtection, CancellationToken.None);

        Assert.Equal(PerformanceModeOption.Balanced, viewModel.PerformanceMode);
        Assert.True(viewModel.HasPerformanceModeError);
    }

    [Fact]
    public async Task RemoveWhitelistEntryAsync_Success_RemovesFromList()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot(string.Empty, ["chrome.exe"], PerformanceModeOption.Balanced),
            RemoveOutcomes = [RemoveWhitelistOutcome.Success],
        };
        var viewModel = CreateViewModel(configFacade, authPromptService: new FakeAuthPromptService([[9]]));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RemoveWhitelistEntryAsync("chrome.exe", null!);

        Assert.Empty(viewModel.WhitelistedProcessNames);
        Assert.True(viewModel.HasWhitelistStatus);
    }

    /// <summary>Idempotent guard (mục 6.4) — `NOT_FOUND` vẫn coi như đã xoá, không hiển thị lỗi.</summary>
    [Fact]
    public async Task RemoveWhitelistEntryAsync_NotFound_StillRemovesFromListLocally()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot(string.Empty, ["chrome.exe"], PerformanceModeOption.Balanced),
            RemoveOutcomes = [RemoveWhitelistOutcome.NotFound],
        };
        var viewModel = CreateViewModel(configFacade, authPromptService: new FakeAuthPromptService([[9]]));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RemoveWhitelistEntryAsync("chrome.exe", null!);

        Assert.Empty(viewModel.WhitelistedProcessNames);
        Assert.False(viewModel.HasWhitelistError);
    }

    [Fact]
    public async Task RemoveWhitelistEntryAsync_UserCancelsGate_NoRequestSent()
    {
        var configFacade = new FakeConfigFacade { Snapshot = new ConfigSnapshot(string.Empty, ["chrome.exe"], PerformanceModeOption.Balanced) };
        var viewModel = CreateViewModel(configFacade, authPromptService: new FakeAuthPromptService([null]));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RemoveWhitelistEntryAsync("chrome.exe", null!);

        Assert.Empty(configFacade.RemoveTokensReceived);
        Assert.Single(viewModel.WhitelistedProcessNames);
    }

    /// <summary>Mục 6.5 — cùng luồng retry đúng 1 lần như `AuditLogViewModel.MarkFalsePositiveAsync`.</summary>
    [Fact]
    public async Task RemoveWhitelistEntryAsync_InvalidToken_ReopensS5Once_SucceedsWithNewToken()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot(string.Empty, ["chrome.exe"], PerformanceModeOption.Balanced),
            RemoveOutcomes = [RemoveWhitelistOutcome.InvalidToken, RemoveWhitelistOutcome.Success],
        };
        var authPromptService = new FakeAuthPromptService([[1], [2]]);
        var viewModel = CreateViewModel(configFacade, authPromptService: authPromptService);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RemoveWhitelistEntryAsync("chrome.exe", null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Empty(viewModel.WhitelistedProcessNames);
        Assert.False(viewModel.HasWhitelistError);
    }

    [Fact]
    public async Task RemoveWhitelistEntryAsync_InvalidTokenTwice_StopsAfterOneRetry_KeepsEntry()
    {
        var configFacade = new FakeConfigFacade
        {
            Snapshot = new ConfigSnapshot(string.Empty, ["chrome.exe"], PerformanceModeOption.Balanced),
            RemoveOutcomes = [RemoveWhitelistOutcome.InvalidToken, RemoveWhitelistOutcome.InvalidToken],
        };
        var authPromptService = new FakeAuthPromptService([[1], [2]]);
        var viewModel = CreateViewModel(configFacade, authPromptService: authPromptService);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RemoveWhitelistEntryAsync("chrome.exe", null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Single(viewModel.WhitelistedProcessNames);
        Assert.True(viewModel.HasWhitelistError);
    }

    [Fact]
    public async Task ChangePasswordAsync_SuccessWithRegenerate_ShowsNewRecoveryKey()
    {
        byte[] recoveryKeyBytes = "NEW-KEY-1234"u8.ToArray();
        var authFacade = new FakeAuthFacade { Result = new ChangePasswordResult(ChangeOutcome.Success, recoveryKeyBytes) };
        var viewModel = CreateViewModel(authFacade: authFacade);

        await viewModel.ChangePasswordAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.True(viewModel.HasChangePasswordStatus);
        Assert.Equal("NEW-KEY-1234", viewModel.NewRecoveryKeyDisplay);

        viewModel.AcknowledgeNewRecoveryKeyDisplayed();
        Assert.Null(viewModel.NewRecoveryKeyDisplay);
        Assert.All(recoveryKeyBytes, b => Assert.Equal(0, b));
    }

    /// <summary>Security audit Đợt 6 S4 (BUG) — đổi mật khẩu 2 lần liên tiếp TRƯỚC khi acknowledge Recovery Key #1 KHÔNG được để plaintext #1 trôi nổi không zero trên managed heap.</summary>
    [Fact]
    public async Task ChangePasswordAsync_CalledTwiceBeforeAcknowledge_ZeroesPreviousRecoveryKeyBuffer()
    {
        byte[] firstKeyBytes = "OLD-KEY-1111"u8.ToArray();
        byte[] secondKeyBytes = "NEW-KEY-2222"u8.ToArray();
        var authFacade = new FakeAuthFacade();
        authFacade.EnqueueResult(new ChangePasswordResult(ChangeOutcome.Success, firstKeyBytes));
        authFacade.EnqueueResult(new ChangePasswordResult(ChangeOutcome.Success, secondKeyBytes));
        var viewModel = CreateViewModel(authFacade: authFacade);

        await viewModel.ChangePasswordAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);
        Assert.Equal("OLD-KEY-1111", viewModel.NewRecoveryKeyDisplay);
        // KHÔNG gọi AcknowledgeNewRecoveryKeyDisplayed() — đúng kịch bản tái hiện bug.

        await viewModel.ChangePasswordAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.Equal("NEW-KEY-2222", viewModel.NewRecoveryKeyDisplay);
        Assert.All(firstKeyBytes, b => Assert.Equal(0, b));
    }

    /// <summary>Defense in depth lớp 2 (security audit Đợt 6 S4) — nút "Đổi mật khẩu" phải bị khoá trong lúc Recovery Key mới còn hiển thị chưa acknowledge.</summary>
    [Fact]
    public async Task ChangePasswordAsync_SuccessWithRegenerate_LocksSubmitUntilAcknowledged()
    {
        var authFacade = new FakeAuthFacade { Result = new ChangePasswordResult(ChangeOutcome.Success, "NEW-KEY-1234"u8.ToArray()) };
        var viewModel = CreateViewModel(authFacade: authFacade);
        Assert.True(viewModel.CanSubmitChangePassword);

        await viewModel.ChangePasswordAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.False(viewModel.CanSubmitChangePassword);

        viewModel.AcknowledgeNewRecoveryKeyDisplayed();

        Assert.True(viewModel.CanSubmitChangePassword);
    }

    [Fact]
    public async Task ChangePasswordAsync_SuccessWithoutRegenerate_NoRecoveryKeyDisplayed()
    {
        var authFacade = new FakeAuthFacade { Result = new ChangePasswordResult(ChangeOutcome.Success, null) };
        var viewModel = CreateViewModel(authFacade: authFacade);

        await viewModel.ChangePasswordAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.False(viewModel.HasNewRecoveryKeyDisplay);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongOldPassword_SetsError()
    {
        var authFacade = new FakeAuthFacade { Result = new ChangePasswordResult(ChangeOutcome.WrongOldPassword, null) };
        var viewModel = CreateViewModel(authFacade: authFacade);

        await viewModel.ChangePasswordAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);

        Assert.True(viewModel.HasChangePasswordError);
        Assert.False(viewModel.IsChangingPassword);
    }

    [Fact]
    public void Dispose_WhenNoRecoveryKeyBufferPresent_IsNoOp()
    {
        var viewModel = CreateViewModel();

        viewModel.Dispose();

        Assert.Null(viewModel.NewRecoveryKeyDisplay);
    }

    /// <summary>
    /// Regression security audit Đợt 6 S6 (bug đã sửa, cùng lớp lỗi race của <c>RecoveryViewModelTests</c>)
    /// — rời tab (vd "Quên mật khẩu cũ?" → S6, <c>MarkDiscarded</c>) TRONG LÚC <c>ChangePasswordAsync</c>
    /// còn treo IPC: Success đến SAU thời điểm đó không được publish, buffer Recovery Key mới phải zero
    /// ngay (trước bug fix, buffer này KHÔNG BAO GIỜ bị zero — không còn ai để "Acknowledge" nữa).
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_MarkDiscardedWhilePending_LateSuccessZeroesBufferWithoutPublishing()
    {
        var pendingResult = new TaskCompletionSource<ChangePasswordResult>();
        var authFacade = new FakeAuthFacade { PendingResult = pendingResult.Task };
        var viewModel = CreateViewModel(authFacade: authFacade);

        Task changeTask = viewModel.ChangePasswordAsync(GC.AllocateArray<byte>(4, pinned: true), GC.AllocateArray<byte>(4, pinned: true), CancellationToken.None);
        Assert.True(viewModel.IsChangingPassword);

        // Cùng thời điểm SettingsPage.OnNavigatedFrom/App.OnWindowClosed gọi khi rời tab/đóng app giữa chừng.
        viewModel.MarkDiscarded();

        byte[] newKeyBytes = "ORPHANED-KEY-5678"u8.ToArray();
        pendingResult.SetResult(new ChangePasswordResult(ChangeOutcome.Success, newKeyBytes));
        await changeTask;

        Assert.Null(viewModel.NewRecoveryKeyDisplay);
        Assert.False(viewModel.HasNewRecoveryKeyDisplay);
        Assert.All(newKeyBytes, b => Assert.Equal(0, b));
    }

    private sealed class FakeConfigFacade : IConfigFacade
    {
        private int _updateIndex;
        private int _removeIndex;

        public ConfigSnapshot Snapshot { get; set; } = new(string.Empty, [], PerformanceModeOption.Balanced);

        public bool ThrowOnGet { get; set; }

        public List<ConfigUpdateOutcome> UpdateOutcomes { get; set; } = [];

        public List<RemoveWhitelistOutcome> RemoveOutcomes { get; set; } = [];

        public List<string> OverlayMessagesReceived { get; } = [];

        public List<PerformanceModeOption> PerformanceModesReceived { get; } = [];

        public List<byte[]> RemoveTokensReceived { get; } = [];

        public Task<ConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken) =>
            ThrowOnGet ? throw new UiIpcConnectionException("Mất kết nối.") : Task.FromResult(Snapshot);

        public Task<ConfigUpdateOutcome> UpdateOverlayMessageAsync(string overlayMessage, PerformanceModeOption currentPerformanceMode, CancellationToken cancellationToken) =>
            RecordUpdate(overlayMessage, currentPerformanceMode);

        public Task<ConfigUpdateOutcome> UpdatePerformanceModeAsync(string currentOverlayMessage, PerformanceModeOption performanceMode, CancellationToken cancellationToken) =>
            RecordUpdate(currentOverlayMessage, performanceMode);

        private Task<ConfigUpdateOutcome> RecordUpdate(string overlayMessage, PerformanceModeOption mode)
        {
            OverlayMessagesReceived.Add(overlayMessage);
            PerformanceModesReceived.Add(mode);
            ConfigUpdateOutcome outcome = _updateIndex < UpdateOutcomes.Count ? UpdateOutcomes[_updateIndex] : throw new InvalidOperationException("No more fake update outcomes configured.");
            _updateIndex++;
            return Task.FromResult(outcome);
        }

        public Task<RemoveWhitelistOutcome> RemoveWhitelistEntryAsync(byte[] actionToken, string processName, CancellationToken cancellationToken)
        {
            RemoveTokensReceived.Add(actionToken.ToArray());
            RemoveWhitelistOutcome outcome = _removeIndex < RemoveOutcomes.Count ? RemoveOutcomes[_removeIndex] : throw new InvalidOperationException("No more fake remove outcomes configured.");
            _removeIndex++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeAuthFacade : IAuthFacade
    {
        private readonly Queue<ChangePasswordResult> _results = new();

        public ChangePasswordResult? Result { get; set; }

        public Task<ChangePasswordResult>? PendingResult { get; set; }

        /// <summary>Cho phép giả lập nhiều lần gọi liên tiếp (mỗi lần trả 1 kết quả khác nhau) — dùng khi <see cref="Result"/> không đủ.</summary>
        public void EnqueueResult(ChangePasswordResult result) => _results.Enqueue(result);

        public Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ChangePasswordResult> ChangePasswordAsync(byte[] oldPasswordUtf8Pinned, byte[] newPasswordUtf8Pinned, bool regenerateRecoveryKey, CancellationToken cancellationToken)
            => PendingResult ?? Task.FromResult(_results.Count > 0 ? _results.Dequeue() : Result ?? throw new InvalidOperationException("Result not configured"));

        public Task<RecoveryResult> RecoveryResetAsync(byte[] recoveryKeyUtf8Pinned, byte[] newPasswordUtf8Pinned, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>Trả lần lượt token trong <c>_tokens</c> mỗi lần <c>ShowAuthPromptAsync</c> được gọi (null = user huỷ dialog) — cùng mẫu hình <see cref="AuditLogViewModelTests"/>.</summary>
    private sealed class FakeAuthPromptService(params byte[]?[] tokens) : IAuthPromptService
    {
        private readonly Queue<byte[]?> _tokens = new(tokens);

        public int CallCount { get; private set; }

        public Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot)
        {
            CallCount++;
            return Task.FromResult(_tokens.Count > 0 ? _tokens.Dequeue() : null);
        }
    }
}

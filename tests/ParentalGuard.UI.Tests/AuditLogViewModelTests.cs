using Microsoft.UI.Xaml;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `AuditLogViewModel` (Architecture/10-ui-architecture.md mục 6.3) — cùng mẫu hình
/// <see cref="DashboardViewModelTests"/>: fake facade/`IAuthPromptService`, không cần `XamlRoot`/
/// `ContentDialog`/`DispatcherQueue` thật.
/// </summary>
public sealed class AuditLogViewModelTests
{
    private static AuditLogViewModel CreateViewModel(FakeAuditFacade? auditFacade = null, IAuthPromptService? authPromptService = null)
        => new(auditFacade ?? new FakeAuditFacade(), authPromptService ?? new FakeAuthPromptService([[1]]));

    [Fact]
    public async Task InitializeAsync_Success_SetsGatedAndLoadsEntries()
    {
        var auditFacade = new FakeAuditFacade
        {
            Pages = [new AuditLogFetchResult(AuditLogQueryOutcome.Success, [new AuditLogEntry(1, 1758000000000, "ContentBlocked", "chrome.exe", 0.9f)], HasMore: true)],
        };
        var authPromptService = new FakeAuthPromptService([[1, 2, 3]]);
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.InitializeAsync(null!);

        Assert.True(viewModel.IsGated);
        Assert.False(viewModel.GateCancelled);
        Assert.Single(viewModel.Entries);
        Assert.True(viewModel.HasMore);
        Assert.True(viewModel.ShowEntries);
        Assert.False(viewModel.IsEmpty);
        Assert.Equal(new byte[] { 1, 2, 3 }, auditFacade.TokensReceived[0]);
        Assert.Equal(0u, auditFacade.PagesRequested[0]);
    }

    [Fact]
    public async Task InitializeAsync_UserCancelsGate_SetsGateCancelled()
    {
        var authPromptService = new FakeAuthPromptService([null]);
        var auditFacade = new FakeAuditFacade();
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.InitializeAsync(null!);

        Assert.True(viewModel.GateCancelled);
        Assert.False(viewModel.IsGated);
        Assert.Empty(auditFacade.TokensReceived);
    }

    /// <summary>`FE-040`.</summary>
    [Fact]
    public async Task InitializeAsync_EmptyLog_IsEmptyTrue()
    {
        var auditFacade = new FakeAuditFacade { Pages = [new AuditLogFetchResult(AuditLogQueryOutcome.Success, [], HasMore: false)] };
        var viewModel = CreateViewModel(auditFacade, new FakeAuthPromptService([[1]]));

        await viewModel.InitializeAsync(null!);

        Assert.True(viewModel.IsGated);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.ShowEntries);
    }

    /// <summary>Mục 6.5 — race token hết hạn ở trang đầu → tự mở lại `S5` NGAY, đúng 1 lần.</summary>
    [Fact]
    public async Task InitializeAsync_InvalidTokenOnFirstPage_ReopensS5Once_SucceedsWithNewToken()
    {
        var authPromptService = new FakeAuthPromptService([[1], [2]]);
        var auditFacade = new FakeAuditFacade
        {
            Pages =
            [
                new AuditLogFetchResult(AuditLogQueryOutcome.InvalidToken, [], HasMore: false),
                new AuditLogFetchResult(AuditLogQueryOutcome.Success, [new AuditLogEntry(1, 0, "ContentBlocked", "a.exe", 0.5f)], HasMore: false),
            ],
        };
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.InitializeAsync(null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.True(viewModel.IsGated);
        Assert.False(viewModel.GateCancelled);
        Assert.Single(viewModel.Entries);
    }

    [Fact]
    public async Task InitializeAsync_InvalidTokenOnFirstPage_UserCancelsRetry_SetsGateCancelled()
    {
        var authPromptService = new FakeAuthPromptService([[1], null]);
        var auditFacade = new FakeAuditFacade { Pages = [new AuditLogFetchResult(AuditLogQueryOutcome.InvalidToken, [], HasMore: false)] };
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.InitializeAsync(null!);

        Assert.True(viewModel.GateCancelled);
        Assert.False(viewModel.IsGated);
    }

    /// <summary>Mục 6.3 — trang ≥1 gửi <c>action_token</c> RỖNG (Service tự nhớ "đã qua gate" theo session pipe).</summary>
    [Fact]
    public async Task LoadMoreAsync_SendsEmptyToken_AppendsEntries()
    {
        var auditFacade = new FakeAuditFacade
        {
            Pages =
            [
                new AuditLogFetchResult(AuditLogQueryOutcome.Success, [new AuditLogEntry(1, 0, "ContentBlocked", "a.exe", 0.5f)], HasMore: true),
                new AuditLogFetchResult(AuditLogQueryOutcome.Success, [new AuditLogEntry(2, 0, "AuthAttempt", string.Empty, 0f)], HasMore: false),
            ],
        };
        var viewModel = CreateViewModel(auditFacade, new FakeAuthPromptService([[1]]));
        await viewModel.InitializeAsync(null!);

        await viewModel.LoadMoreAsync();

        Assert.Equal(2, viewModel.Entries.Count);
        Assert.False(viewModel.HasMore);
        Assert.Equal(2, auditFacade.TokensReceived.Count);
        Assert.Empty(auditFacade.TokensReceived[1]);
        Assert.Equal(1u, auditFacade.PagesRequested[1]);
    }

    [Fact]
    public async Task LoadMoreAsync_NoHasMore_DoesNotCallFacade()
    {
        var auditFacade = new FakeAuditFacade { Pages = [new AuditLogFetchResult(AuditLogQueryOutcome.Success, [], HasMore: false)] };
        var viewModel = CreateViewModel(auditFacade, new FakeAuthPromptService([[1]]));
        await viewModel.InitializeAsync(null!);

        await viewModel.LoadMoreAsync();

        Assert.Single(auditFacade.TokensReceived);
    }

    [Fact]
    public async Task MarkFalsePositiveAsync_Success_SetsStatusMessage()
    {
        var authPromptService = new FakeAuthPromptService([[9]]);
        var auditFacade = new FakeAuditFacade { MarkOutcomes = [MarkFalsePositiveOutcome.Success] };
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.MarkFalsePositiveAsync("chrome.exe", null!);

        Assert.False(string.IsNullOrEmpty(viewModel.StatusMessage));
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("chrome.exe", Assert.Single(auditFacade.MarkProcessNamesReceived));
    }

    [Fact]
    public async Task MarkFalsePositiveAsync_UserCancelsGate_NoRequestSent()
    {
        var authPromptService = new FakeAuthPromptService([null]);
        var auditFacade = new FakeAuditFacade();
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.MarkFalsePositiveAsync("chrome.exe", null!);

        Assert.Empty(auditFacade.MarkProcessNamesReceived);
    }

    /// <summary>Mục 6.5 — cùng luồng retry đúng 1 lần như <c>DashboardViewModel.PauseAsync</c>, gate riêng <c>manage_whitelist</c>.</summary>
    [Fact]
    public async Task MarkFalsePositiveAsync_InvalidToken_ReopensS5Once_SucceedsWithNewToken()
    {
        var authPromptService = new FakeAuthPromptService([[1], [2]]);
        var auditFacade = new FakeAuditFacade { MarkOutcomes = [MarkFalsePositiveOutcome.InvalidToken, MarkFalsePositiveOutcome.Success] };
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.MarkFalsePositiveAsync("chrome.exe", null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.Equal(2, auditFacade.MarkProcessNamesReceived.Count);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task MarkFalsePositiveAsync_InvalidToken_RetryAlsoInvalidToken_StopsAfterOneRetry()
    {
        var authPromptService = new FakeAuthPromptService([[1], [2]]);
        var auditFacade = new FakeAuditFacade { MarkOutcomes = [MarkFalsePositiveOutcome.InvalidToken, MarkFalsePositiveOutcome.InvalidToken] };
        var viewModel = CreateViewModel(auditFacade, authPromptService);

        await viewModel.MarkFalsePositiveAsync("chrome.exe", null!);

        Assert.Equal(2, authPromptService.CallCount);
        Assert.False(string.IsNullOrEmpty(viewModel.ErrorMessage));
    }

    /// <summary>Ghi lại bản SAO CHÉP token nhận được (không giữ nguyên tham chiếu) — `AuditLogViewModel` zero buffer gốc trong <c>finally</c> ngay sau khi facade dùng xong (memory hygiene), nên giữ tham chiếu gốc sẽ đọc ra toàn số 0 lúc assert.</summary>
    private sealed class FakeAuditFacade : IAuditFacade
    {
        private int _pageIndex;
        private int _markIndex;

        public List<AuditLogFetchResult> Pages { get; set; } = [];

        public List<MarkFalsePositiveOutcome> MarkOutcomes { get; set; } = [];

        public List<byte[]> TokensReceived { get; } = [];

        public List<uint> PagesRequested { get; } = [];

        public List<string> MarkProcessNamesReceived { get; } = [];

        public Task<AuditLogFetchResult> GetAuditLogAsync(byte[] actionToken, uint page, uint pageSize, CancellationToken cancellationToken)
        {
            TokensReceived.Add(actionToken.ToArray());
            PagesRequested.Add(page);
            AuditLogFetchResult result = _pageIndex < Pages.Count ? Pages[_pageIndex] : throw new InvalidOperationException("No more fake pages configured.");
            _pageIndex++;
            return Task.FromResult(result);
        }

        public Task<MarkFalsePositiveOutcome> MarkFalsePositiveAsync(byte[] actionToken, string processName, CancellationToken cancellationToken)
        {
            MarkProcessNamesReceived.Add(processName);
            MarkFalsePositiveOutcome outcome = _markIndex < MarkOutcomes.Count ? MarkOutcomes[_markIndex] : throw new InvalidOperationException("No more fake outcomes configured.");
            _markIndex++;
            return Task.FromResult(outcome);
        }
    }

    /// <summary>Trả lần lượt token trong <c>_tokens</c> mỗi lần <c>ShowAuthPromptAsync</c> được gọi (null = user huỷ dialog) — cùng mẫu hình <c>DashboardViewModelTests.FakeAuthPromptService</c>.</summary>
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

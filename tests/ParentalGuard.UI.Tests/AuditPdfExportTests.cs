using System.Text;
using Microsoft.UI.Xaml;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Tests;

/// <summary>`MISC-011` (2026-10-07) — xuất lịch sử ra PDF.</summary>
public sealed class AuditPdfExportTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static AuditLogEntry Entry(ulong seq, int daysAgo, string type = "ContentBlocked") =>
        new(seq, _now.AddDays(-daysAgo).ToUnixTimeMilliseconds(), type, "chrome.exe", 0.91f);

    [Fact]
    public void Build_ProducesPdfWithEmbeddedFont_EvenWhenEmpty()
    {
        byte[] withRows = AuditPdfExporter.Build([Entry(2, 0), Entry(1, 1, "PauseActivated")], "7 ngày gần nhất", _now, "vi");
        byte[] empty = AuditPdfExporter.Build([], "Toàn bộ lịch sử", _now, "vi");

        foreach (byte[] pdf in new[] { withRows, empty })
        {
            string head = Encoding.ASCII.GetString(pdf, 0, 5);
            Assert.Equal("%PDF-", head);
            Assert.Contains("/FontFile2", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal); // font nhúng → hiển thị đúng tiếng Việt
        }
    }

    [Fact]
    public void Build_ManyRows_SpansSeveralPages()
    {
        AuditLogEntry[] rows = [.. Enumerable.Range(0, 200).Select(i => Entry((ulong)(200 - i), 0))];

        string pdf = Encoding.Latin1.GetString(AuditPdfExporter.Build(rows, "7", _now, "vi"));

        Assert.Contains("/Count 5", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterRange_KeepsOnlyEntriesInsideWindow()
    {
        long cutoff = _now.AddDays(-7).ToUnixTimeMilliseconds();

        IReadOnlyList<AuditLogEntry> kept = AuditPdfExporter.FilterRange([Entry(3, 1), Entry(2, 6), Entry(1, 8)], cutoff);

        Assert.Equal([3UL, 2UL], kept.Select(e => e.Seq));
        Assert.Equal(3, AuditPdfExporter.FilterRange([Entry(3, 1), Entry(2, 6), Entry(1, 8)], null).Count);
    }

    [Fact]
    public async Task BuildPdfAsync_NotGated_ReturnsNull_GatedStopsPagingOncePastCutoff()
    {
        var facade = new PagedAuditFacade(
        [
            [Entry(10, 1), Entry(9, 3)],
            [Entry(8, 5), Entry(7, 9)], // vượt mốc 7 ngày → dừng, không xin trang 3
            [Entry(6, 20)],
        ]);
        var vm = new AuditLogViewModel(facade, new TokenPrompt());

        Assert.Null(await vm.BuildPdfAsync(7, "7", "vi", () => _now));

        await vm.InitializeAsync(null!);
        int pagesBeforeExport = facade.Requests.Count;
        byte[]? pdf = await vm.BuildPdfAsync(7, "7", "vi", () => _now);

        Assert.NotNull(pdf);
        Assert.Equal([0u, 1u], facade.Requests.Skip(pagesBeforeExport).Select(r => r.Page));
        Assert.All(facade.Requests.Skip(pagesBeforeExport), r => Assert.Empty(r.Token)); // trang xuất dùng phiên, không token
    }

    private sealed class TokenPrompt : IAuthPromptService
    {
        public Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot) => Task.FromResult<byte[]?>([7]);
    }

    private sealed class PagedAuditFacade(AuditLogEntry[][] pages) : IAuditFacade
    {
        public List<(uint Page, byte[] Token)> Requests { get; } = [];

        public Task<AuditLogFetchResult> GetAuditLogAsync(byte[] actionToken, uint page, uint pageSize, CancellationToken cancellationToken)
        {
            Requests.Add((page, actionToken.ToArray()));
            AuditLogEntry[] rows = page < pages.Length ? pages[page] : [];
            return Task.FromResult(new AuditLogFetchResult(AuditLogQueryOutcome.Success, rows, page + 1 < pages.Length));
        }
    }
}

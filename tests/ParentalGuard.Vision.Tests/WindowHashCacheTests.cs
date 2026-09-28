using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>Architecture/05 mục 3.8.2 (ADR-65 evict threshold tái dùng) — RAM-only trong Vision.</summary>
public class WindowHashCacheTests
{
    private static readonly IntPtr Hwnd = new(1);

    [Fact]
    public void ResolveContentChanged_FirstTimeSeen_ReturnsTrue_WithZeroCachedRiskScore()
    {
        var cache = new WindowHashCache();

        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 0x1234, out float cachedRiskScore);

        Assert.True(changed);
        Assert.Equal(0f, cachedRiskScore);
    }

    [Fact]
    public void ResolveContentChanged_SameHashAgain_ReturnsFalse_WithPreviouslyCachedRiskScore()
    {
        var cache = new WindowHashCache();
        cache.ResolveContentChanged(Hwnd, newHash: 0x1234, out _);
        cache.UpdateRiskScore(Hwnd, 0.42f);

        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 0x1234, out float cachedRiskScore);

        Assert.False(changed);
        Assert.Equal(0.42f, cachedRiskScore);
    }

    [Fact]
    public void ResolveContentChanged_HashWithinThreshold_ReturnsFalse()
    {
        var cache = new WindowHashCache();
        cache.ResolveContentChanged(Hwnd, newHash: 0b0000, out _);

        // Lệch đúng WindowHashCache.HammingUnchangedThreshold bit (3) — vẫn coi là "không đổi".
        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 0b0111, out _);

        Assert.False(changed);
    }

    [Fact]
    public void ResolveContentChanged_HashBeyondThreshold_ReturnsTrue()
    {
        var cache = new WindowHashCache();
        cache.ResolveContentChanged(Hwnd, newHash: 0b0000, out _);

        // Lệch 4 bit — vượt ngưỡng 3.
        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 0b1111, out _);

        Assert.True(changed);
    }

    [Fact]
    public void ResolveContentChanged_AlwaysUpdatesPreviousHash_ComparesAgainstImmediatelyPriorFrame()
    {
        var cache = new WindowHashCache();
        cache.ResolveContentChanged(Hwnd, newHash: 0b0000, out _); // first time, previous := 0b0000
        cache.ResolveContentChanged(Hwnd, newHash: 0b0111, out _); // distance 3 vs 0b0000 → unchanged, previous := 0b0111

        // distance(0b1000, previous=0b0111) = popcount(0b1111) = 4 → CHANGED. Nếu cache sai — so với
        // frame GỐC (0b0000) thay vì frame NGAY TRƯỚC — sẽ ra distance(0b1000,0b0000)=1 → unchanged
        // (sai). Test này phân biệt rõ 2 hành vi.
        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 0b1000, out _);

        Assert.True(changed);
    }

    [Fact]
    public void EndCycle_UsedWindow_DoesNotEvict()
    {
        var cache = new WindowHashCache();
        cache.ResolveContentChanged(Hwnd, newHash: 1, out _);

        for (int i = 0; i < 10; i++)
        {
            cache.EndCycle(new HashSet<IntPtr> { Hwnd });
        }

        // Vẫn còn nhớ hash cũ (nếu bị evict, lần gọi dưới sẽ trả về true như "lần đầu gặp").
        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 1, out _);
        Assert.False(changed);
    }

    [Fact]
    public void EndCycle_FiveConsecutiveIdleCycles_EvictsEntry()
    {
        var cache = new WindowHashCache();
        cache.ResolveContentChanged(Hwnd, newHash: 1, out _);

        for (int i = 0; i < 5; i++)
        {
            cache.EndCycle(new HashSet<IntPtr>());
        }

        // Bị evict → coi như lần đầu gặp lại (true), dù hash giống hệt trước.
        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 1, out float cachedRiskScore);
        Assert.True(changed);
        Assert.Equal(0f, cachedRiskScore);
    }

    [Fact]
    public void EndCycle_BelowFiveIdleCycles_KeepsEntry()
    {
        var cache = new WindowHashCache();
        cache.ResolveContentChanged(Hwnd, newHash: 1, out _);

        for (int i = 0; i < 4; i++)
        {
            cache.EndCycle(new HashSet<IntPtr>());
        }

        bool changed = cache.ResolveContentChanged(Hwnd, newHash: 1, out _);
        Assert.False(changed);
    }
}

using System.Numerics;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 3.8.2 — <c>Dictionary&lt;window_handle,
/// WindowHashEntry&gt;</c> RAM-only trong <c>Vision</c> ("state kỹ thuật" cho phép ở Vision theo
/// 02-process-architecture.md mục 5 điểm 2, KHÔNG phải "state nghiệp vụ"). Evict sau
/// <see cref="IdleEvictThreshold"/> chu kỳ liên tiếp không thấy <c>window_handle</c> (cùng ngưỡng
/// ADR-65 dùng cho <see cref="OutputCaptureContextPool"/>).
/// </summary>
public sealed class WindowHashCache
{
    /// <summary>PERF-061 (mục 3.7.6) — PROPOSED, chờ benchmark đa cấu hình máy trước khi khoá cứng.</summary>
    public const int HammingUnchangedThreshold = 3;

    private const int IdleEvictThreshold = 5; // ADR-65

    private sealed class WindowHashEntry
    {
        public ulong PreviousHash;
        public float CachedRiskScore;
        public int IdleCycles;
    }

    private readonly Dictionary<IntPtr, WindowHashEntry> _entries = [];

    /// <summary>
    /// Mục 3.8.1: cửa sổ lần đầu gặp (chưa có hash trước) → luôn <c>true</c> (mặc định an toàn, chạy
    /// inference đầy đủ). LUÔN cập nhật <c>PreviousHash</c> ngay tại đây bất kể kết quả so sánh (so
    /// với frame NGAY TRƯỚC ĐÓ, tránh "trôi" qua nhiều chu kỳ nhỏ dưới ngưỡng).
    /// </summary>
    public bool ResolveContentChanged(IntPtr windowHandle, ulong newHash, out float cachedRiskScore)
    {
        if (!_entries.TryGetValue(windowHandle, out WindowHashEntry? entry))
        {
            _entries[windowHandle] = new WindowHashEntry { PreviousHash = newHash, CachedRiskScore = 0f, IdleCycles = 0 };
            cachedRiskScore = 0f;
            return true;
        }

        entry.IdleCycles = 0;
        int hammingDistance = BitOperations.PopCount(newHash ^ entry.PreviousHash);
        entry.PreviousHash = newHash;
        cachedRiskScore = entry.CachedRiskScore;
        return hammingDistance > HammingUnchangedThreshold;
    }

    /// <summary>Gọi sau khi vừa chạy inference thật (content_changed=true) — cache lại cho chu kỳ sau tái dùng (PERF-011).</summary>
    public void UpdateRiskScore(IntPtr windowHandle, float riskScore)
    {
        if (_entries.TryGetValue(windowHandle, out WindowHashEntry? entry))
        {
            entry.CachedRiskScore = riskScore;
        }
    }

    /// <summary>Gọi đúng 1 lần cuối mỗi chu kỳ capture (Architecture/05 mục 3.8.2, cùng mẫu hình <see cref="OutputCaptureContextPool.EndCycle"/>).</summary>
    public void EndCycle(IReadOnlySet<IntPtr> usedWindowHandles)
    {
        List<IntPtr>? evicted = null;
        foreach ((IntPtr handle, WindowHashEntry entry) in _entries)
        {
            if (usedWindowHandles.Contains(handle))
            {
                continue;
            }

            entry.IdleCycles++;
            if (entry.IdleCycles >= IdleEvictThreshold)
            {
                (evicted ??= []).Add(handle);
            }
        }

        if (evicted is null)
        {
            return;
        }

        foreach (IntPtr handle in evicted)
        {
            _entries.Remove(handle);
        }
    }
}

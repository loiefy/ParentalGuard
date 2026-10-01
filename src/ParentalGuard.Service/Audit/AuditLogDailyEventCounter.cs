using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ParentalGuard.Service.Audit;

/// <summary>
/// Đếm số event theo loại + ngày lịch UTC trong <c>audit.log</c>, quét TĂNG DẦN (chỉ đọc phần byte mới ghi
/// thêm kể từ lần trước — audit.log chỉ append, Architecture/04) và lọc thô theo byte tên event trước khi
/// parse JSON. Bug real-hardware 2026-10-01: bản cũ (biểu đồ `S2` + `PAUSE-021`) <c>ReadAllLines</c> + parse
/// TỪNG dòng toàn bộ file MỖI lần — log phình to khiến bấm "Tạm dừng" mất ~8s, Dashboard "mất kết nối" &gt;15s.
/// Cache theo (đường dẫn, loại event); file ngắn đi (bị thay) thì quét lại từ đầu.
/// </summary>
public static class AuditLogDailyEventCounter
{
    private sealed class Cache(string eventType)
    {
        public readonly SemaphoreSlim Lock = new(1, 1);
        public readonly byte[] Marker = Encoding.UTF8.GetBytes($"\"{eventType}\"");
        public readonly string EventType = eventType;
        public readonly Dictionary<DateOnly, uint> Counts = [];
        public long ScannedBytes;
    }

    private static readonly ConcurrentDictionary<(string Path, string EventType), Cache> _caches = new();

    /// <summary>Bản sao số event <paramref name="eventType"/> theo ngày UTC.</summary>
    public static async Task<Dictionary<DateOnly, uint>> CountByDayAsync(string auditLogPath, string eventType, CancellationToken cancellationToken)
    {
        Cache cache = _caches.GetOrAdd((Path.GetFullPath(auditLogPath), eventType), key => new Cache(key.EventType));
        await cache.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(auditLogPath))
            {
                cache.Counts.Clear();
                cache.ScannedBytes = 0;
                return [];
            }

            await using var stream = new FileStream(auditLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
            if (stream.Length < cache.ScannedBytes)
            {
                cache.Counts.Clear();
                cache.ScannedBytes = 0;
            }

            stream.Seek(cache.ScannedBytes, SeekOrigin.Begin);
            byte[] buffer = new byte[1 << 20];
            var pending = new List<byte>();
            long consumed = cache.ScannedBytes;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                int lineStart = 0;
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n')
                    {
                        continue;
                    }

                    if (pending.Count > 0)
                    {
                        pending.AddRange(buffer.AsSpan(lineStart, i - lineStart).ToArray());
                        CountLine(cache, CollectionsMarshal.AsSpan(pending));
                        consumed += pending.Count + 1;
                        pending.Clear();
                    }
                    else
                    {
                        CountLine(cache, buffer.AsSpan(lineStart, i - lineStart));
                        consumed += i - lineStart + 1;
                    }

                    lineStart = i + 1;
                }

                pending.AddRange(buffer.AsSpan(lineStart, read - lineStart).ToArray());
            }

            // Dòng cuối chưa có LF (đang ghi dở) — không tính, lần sau đọc lại từ đầu dòng đó.
            cache.ScannedBytes = consumed;
            return new Dictionary<DateOnly, uint>(cache.Counts);
        }
        finally
        {
            cache.Lock.Release();
        }
    }

    private static void CountLine(Cache cache, ReadOnlySpan<byte> line)
    {
        if (line.IndexOf(cache.Marker) < 0)
        {
            return; // lọc thô — tuyệt đại đa số dòng không phải event cần đếm, không tốn parse JSON
        }

        try
        {
            var reader = new Utf8JsonReader(line);
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("event_type", out JsonElement eventType) || eventType.GetString() != cache.EventType)
            {
                return;
            }

            long tsUnixMs = root.GetProperty("ts_unix_ms").GetInt64();
            DateOnly date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(tsUnixMs).UtcDateTime);
            cache.Counts[date] = cache.Counts.GetValueOrDefault(date) + 1;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // Dòng hỏng/không parse được — không phải việc của bộ đếm xác minh hash-chain, chỉ bỏ qua khi đếm.
        }
    }
}

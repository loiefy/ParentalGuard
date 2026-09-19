using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ParentalGuard.Service.Audit;

/// <summary>
/// Audit log hash-chain, append-only, JSONL (MISC-010/SEC-041, Architecture/04-data-architecture.md
/// mục 5). Không mã hoá nội dung (SEC-041) — chỉ toàn vẹn qua hash-chain + ACL ghi (Architecture/06).
/// </summary>
public sealed class AuditLogWriter
{
    /// <summary>Số record cuối cùng verify lúc khởi động (ADR-28, giá trị khởi điểm).</summary>
    private const int _verifyWindowSize = 50;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _path;
    private AuditCheckpoint _checkpoint;

    private AuditLogWriter(string path, AuditCheckpoint checkpoint)
    {
        _path = path;
        _checkpoint = checkpoint;
    }

    public AuditCheckpoint Checkpoint => _checkpoint;

    public static async Task<AuditLogWriter> InitializeAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            var writer = new AuditLogWriter(path, AuditCheckpoint.CreateGenesis());
            await writer.AppendAsync("ServiceStarted", new { }, cancellationToken).ConfigureAwait(false);
            return writer;
        }

        // Đọc toàn bộ dòng (đơn giản hoá Đợt 0 cho quy mô log còn nhỏ) nhưng CHỈ verify
        // _verifyWindowSize record cuối cùng, đúng tinh thần ADR-28 (không quét lại toàn bộ
        // lịch sử mỗi lần khởi động chỉ để tìm dấu hiệu tamper).
        string[] lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        List<string> nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmptyLines.Count == 0)
        {
            var writer = new AuditLogWriter(path, AuditCheckpoint.CreateGenesis());
            await writer.AppendAsync("ServiceStarted", new { }, cancellationToken).ConfigureAwait(false);
            return writer;
        }

        List<ParsedRecord> tail = nonEmptyLines
            .Skip(Math.Max(0, nonEmptyLines.Count - _verifyWindowSize))
            .Select(ParseRecord)
            .ToList();

        string? brokenDetail = VerifyTail(tail);
        AuditLogWriter result;
        if (brokenDetail is not null)
        {
            ParsedRecord lastKnown = tail[^1];
            result = new AuditLogWriter(path, new AuditCheckpoint(lastKnown.ChainId, lastKnown.Seq, lastKnown.Hash));
            await result.AppendAsync(
                "AuditChainBrokenDetected",
                new { detail = brokenDetail, last_known_good_seq = lastKnown.Seq },
                cancellationToken,
                startNewChain: true).ConfigureAwait(false);
            return result;
        }

        ParsedRecord last = tail[^1];
        result = new AuditLogWriter(path, new AuditCheckpoint(last.ChainId, last.Seq, last.Hash));
        await result.AppendAsync("ServiceStarted", new { }, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Luôn ghi vào path đã dùng lúc <see cref="InitializeAsync"/> (Bug 4 fix — trước đây nhận tham số <c>path</c> riêng, caller hay truyền lệch sang <c>InstallPaths.AuditLogPath</c> production dù writer được khởi tạo bằng path test cô lập, làm ô nhiễm audit.log thật).</summary>
    public Task AppendAsync(string eventType, object detail, CancellationToken cancellationToken) =>
        AppendAsync(eventType, detail, cancellationToken, startNewChain: false);

    private async Task AppendAsync(string eventType, object detail, CancellationToken cancellationToken, bool startNewChain)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string chainId = startNewChain ? Guid.NewGuid().ToString() : _checkpoint.ChainId;
            long seq = startNewChain ? 1 : _checkpoint.LastSeq + 1;
            string prevHash = startNewChain ? AuditCheckpoint.GenesisHash : _checkpoint.LastHash;
            long timestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            JsonObject canonical = BuildRecordObject(seq, timestampUnixMs, chainId, eventType, detail, prevHash, hash: null);
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SortKeys(canonical).ToJsonString())));

            JsonObject stored = BuildRecordObject(seq, timestampUnixMs, chainId, eventType, detail, prevHash, hash);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.AppendAllTextAsync(_path, stored.ToJsonString() + Environment.NewLine, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            _checkpoint = new AuditCheckpoint(chainId, seq, hash);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static JsonObject BuildRecordObject(long seq, long ts, string chainId, string eventType, object detail, string prevHash, string? hash)
    {
        var obj = new JsonObject
        {
            ["seq"] = seq,
            ["ts_unix_ms"] = ts,
            ["chain_id"] = chainId,
            ["event_type"] = eventType,
            ["detail"] = JsonSerializer.SerializeToNode(detail),
            ["prev_hash"] = prevHash,
        };
        if (hash is not null)
        {
            obj["hash"] = hash;
        }

        return obj;
    }

    /// <summary>Sắp xếp key theo alphabet (đệ quy) — "canonical = sorted-key JSON" (mục 5.2).</summary>
    private static JsonNode SortKeys(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (string key in obj.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal))
                {
                    JsonNode? value = obj[key];
                    sorted[key] = value is null ? null : SortKeys(value.DeepClone());
                }

                return sorted;
            case JsonArray array:
                var sortedArray = new JsonArray();
                foreach (JsonNode? item in array)
                {
                    sortedArray.Add(item is null ? null : SortKeys(item.DeepClone()));
                }

                return sortedArray;
            default:
                return node.DeepClone();
        }
    }

    private static ParsedRecord ParseRecord(string line)
    {
        JsonObject obj = JsonNode.Parse(line)!.AsObject();
        return new ParsedRecord(
            obj["seq"]!.GetValue<long>(),
            obj["chain_id"]!.GetValue<string>(),
            obj["prev_hash"]!.GetValue<string>(),
            obj["hash"]!.GetValue<string>(),
            obj);
    }

    /// <summary>
    /// Verify linkage + hash từng record trong cửa sổ đuôi (mục 5.3). Trả về null nếu hợp lệ,
    /// hoặc mô tả điểm đứt nếu phát hiện bất thường.
    /// </summary>
    private static string? VerifyTail(IReadOnlyList<ParsedRecord> tail)
    {
        for (int i = 0; i < tail.Count; i++)
        {
            ParsedRecord current = tail[i];
            JsonObject withoutHash = (JsonObject)current.Raw.DeepClone();
            withoutHash.Remove("hash");
            string recomputed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SortKeys(withoutHash).ToJsonString())));
            if (!string.Equals(recomputed, current.Hash, StringComparison.Ordinal))
            {
                return $"hash mismatch at seq={current.Seq}";
            }

            if (i == 0)
            {
                // Không có anchor bên ngoài cửa sổ (mục boundary) — chỉ chấp nhận nếu đây đúng
                // là điểm bắt đầu 1 chain (seq=1, prev_hash=genesis) hoặc chấp nhận làm điểm neo
                // (không đủ dữ liệu để kiểm tra xa hơn, đúng tinh thần "chỉ verify N record cuối").
                continue;
            }

            ParsedRecord previous = tail[i - 1];
            bool continuesChain = current.ChainId == previous.ChainId && current.Seq == previous.Seq + 1 && current.PrevHash == previous.Hash;
            bool startsNewChain = current.ChainId != previous.ChainId && current.Seq == 1 && current.PrevHash == AuditCheckpoint.GenesisHash;
            if (!continuesChain && !startsNewChain)
            {
                return $"chain linkage broken between seq={previous.Seq} and seq={current.Seq}";
            }
        }

        return null;
    }

    private sealed record ParsedRecord(long Seq, string ChainId, string PrevHash, string Hash, JsonObject Raw);
}

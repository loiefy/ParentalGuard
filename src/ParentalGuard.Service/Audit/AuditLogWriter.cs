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

    /// <summary>
    /// <c>true</c> chỉ khi nhánh phát hiện chain đứt (mục 5.3 bước 2-3) chạy trong LẦN GỌI
    /// <see cref="InitializeAsync"/> HIỆN TẠI — không phải "đã từng đứt trong lịch sử file". ADR-139:
    /// <see cref="InitializeAsync"/> chạy trước khi Overlay supervisor tồn tại (<c>Worker.ExecuteAsync</c>)
    /// nên không thể gửi Toast ngay tại chỗ — <c>Worker</c> đọc cờ này SAU KHI Overlay supervisor đã
    /// dựng xong để đưa vào danh sách one-time message (tái dùng <c>BuildOverlayOneTimeMessages</c>).
    /// </summary>
    public bool ChainWasBrokenAtStartup { get; private set; }

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

        ChainVerifyOutcome outcome = VerifyChain(tail);
        AuditLogWriter result;
        if (outcome.Detail is not null)
        {
            ParsedRecord lastKnown = tail[^1];
            result = new AuditLogWriter(path, new AuditCheckpoint(lastKnown.ChainId, lastKnown.Seq, lastKnown.Hash)) { ChainWasBrokenAtStartup = true };
            await result.AppendAsync(
                "AuditChainBrokenDetected",
                new { detail = outcome.Detail, last_known_good_seq = lastKnown.Seq, broken_at_seq = outcome.BrokenAtSeq },
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
    /// Verify linkage + hash từng record trong 1 dãy record liên tiếp (mục 5.3 — cửa sổ đuôi N=50 lúc
    /// boot, hoặc mục 5.3a — TOÀN BỘ file lúc <see cref="VerifyFullChainAsync"/>). <see cref="ChainVerifyOutcome.Detail"/>
    /// null nếu hợp lệ; <see cref="ChainVerifyOutcome.BrokenIndex"/> chỉ có ý nghĩa khi <c>Detail</c> khác null.
    /// </summary>
    private static ChainVerifyOutcome VerifyChain(IReadOnlyList<ParsedRecord> records)
    {
        for (int i = 0; i < records.Count; i++)
        {
            ParsedRecord current = records[i];
            JsonObject withoutHash = (JsonObject)current.Raw.DeepClone();
            withoutHash.Remove("hash");
            string recomputed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SortKeys(withoutHash).ToJsonString())));
            if (!string.Equals(recomputed, current.Hash, StringComparison.Ordinal))
            {
                return new ChainVerifyOutcome($"hash mismatch at seq={current.Seq}", current.Seq, i);
            }

            if (i == 0)
            {
                // Không có anchor bên ngoài dãy (mục boundary) — chỉ chấp nhận nếu đây đúng
                // là điểm bắt đầu 1 chain (seq=1, prev_hash=genesis) hoặc chấp nhận làm điểm neo
                // (không đủ dữ liệu để kiểm tra xa hơn, đúng tinh thần "chỉ verify N record cuối" khi
                // gọi với cửa sổ đuôi — với toàn bộ file, i==0 LUÔN là genesis thật theo đúng construction).
                continue;
            }

            ParsedRecord previous = records[i - 1];
            bool continuesChain = current.ChainId == previous.ChainId && current.Seq == previous.Seq + 1 && current.PrevHash == previous.Hash;
            bool startsNewChain = current.ChainId != previous.ChainId && current.Seq == 1 && current.PrevHash == AuditCheckpoint.GenesisHash;
            if (!continuesChain && !startsNewChain)
            {
                return new ChainVerifyOutcome($"chain linkage broken between seq={previous.Seq} and seq={current.Seq}", current.Seq, i);
            }
        }

        return new ChainVerifyOutcome(null, 0, -1);
    }

    /// <summary>
    /// `04-data-architecture.md` mục 5.3a (ADR-138) — quét TOÀN BỘ <c>audit.log</c> (không giới hạn N=50),
    /// on-demand (<c>VerifyAuditChainRequest</c>). Dùng chung <see cref="_writeLock"/> nhưng thả NGAY sau khi
    /// đọc xong (không giữ lock suốt phép tính hash — mục 5.3a bước 3).
    /// </summary>
    /// <remarks>
    /// <b>2026-09-29 audit fix (FAIL cứng do test-runner phát hiện)</b>: bản gốc dùng đúng 1
    /// <see cref="VerifyChain"/> tuyến tính, `return` NGAY khi gặp bất thường ĐẦU TIÊN của cả file — vì
    /// 1 record đã bị tamper mãi mãi fail lại self-hash-check của chính nó ở MỌI lần gọi sau, việc bail
    /// sớm khiến verify KHÔNG BAO GIỜ quét tới các đoạn chain (`chain_id`) phía sau, nên 1 tamper ĐỘC LẬP
    /// thứ 2 xảy ra sau lần "phục hồi" (đoạn chain mới) hoàn toàn không được phát hiện — vĩnh viễn mù sau
    /// đúng 1 sự cố lịch sử. Sửa đúng thiết kế mục 5.3a bước 1 ("chia thành các đoạn chain kế tiếp nhau
    /// theo chain_id ... áp dụng đúng thuật toán cho TOÀN BỘ TỪNG ĐOẠN"): <see cref="VerifySegments"/>
    /// verify MỖI đoạn `chain_id` ĐỘC LẬP, 1 đoạn bị tamper không cản trở việc verify các đoạn sau nó.
    /// </remarks>
    public async Task<AuditChainVerifyResult> VerifyFullChainAsync(CancellationToken cancellationToken)
    {
        string[] lines;
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lines = File.Exists(_path) ? await File.ReadAllLinesAsync(_path, Encoding.UTF8, cancellationToken).ConfigureAwait(false) : [];
        }
        finally
        {
            _writeLock.Release();
        }

        List<ParsedRecord> all = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(ParseRecord).ToList();
        List<ChainVerifyOutcome> breaks = VerifySegments(all);
        bool isIntact = breaks.Count == 0;
        long verifiedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (ChainVerifyOutcome outcome in breaks)
        {
            bool alreadyFlagged = all.Skip(outcome.BrokenIndex + 1)
                .Any(r => EventType(r.Raw) == "AuditChainBrokenDetected" && BrokenAtSeqOf(r.Raw) == outcome.BrokenAtSeq);
            if (alreadyFlagged)
            {
                continue;
            }

            long lastKnownGoodSeq = outcome.BrokenIndex > 0 && all[outcome.BrokenIndex - 1].ChainId == all[outcome.BrokenIndex].ChainId
                ? all[outcome.BrokenIndex - 1].Seq
                : 0;
            await AppendAsync(
                "AuditChainBrokenDetected",
                new { detail = outcome.Detail, last_known_good_seq = lastKnownGoodSeq, broken_at_seq = outcome.BrokenAtSeq },
                CancellationToken.None,
                startNewChain: true).ConfigureAwait(false);
        }

        long firstBrokenAtSeq = breaks.Count > 0 ? breaks[0].BrokenAtSeq : 0;
        return new AuditChainVerifyResult(isIntact, all.Count, firstBrokenAtSeq, verifiedAtUnixMs);
    }

    /// <summary>
    /// Chia <paramref name="records"/> thành các đoạn liên tiếp theo <c>chain_id</c> (mục 5.3a bước 1),
    /// verify MỖI đoạn độc lập qua <see cref="VerifySegment"/> — 1 đoạn có bất thường không ngăn việc
    /// verify các đoạn còn lại. Trả về TỐI ĐA 1 <see cref="ChainVerifyOutcome"/> cho mỗi đoạn bị hỏng
    /// (bất thường ĐẦU TIÊN trong đoạn đó, đúng ngữ nghĩa "broken_at_seq" hiện có — không liệt kê nhiều
    /// bất thường trong cùng 1 đoạn).
    /// </summary>
    private static List<ChainVerifyOutcome> VerifySegments(IReadOnlyList<ParsedRecord> records)
    {
        var outcomes = new List<ChainVerifyOutcome>();
        int segmentStart = 0;
        for (int i = 1; i <= records.Count; i++)
        {
            bool isSegmentBoundary = i == records.Count || records[i].ChainId != records[i - 1].ChainId;
            if (!isSegmentBoundary)
            {
                continue;
            }

            ChainVerifyOutcome segmentOutcome = VerifySegment(records, segmentStart, i);
            if (segmentOutcome.Detail is not null)
            {
                outcomes.Add(segmentOutcome);
            }

            segmentStart = i;
        }

        return outcomes;
    }

    /// <summary>
    /// Verify 1 đoạn chain độc lập <paramref name="records"/>[<paramref name="start"/>..<paramref name="end"/>)
    /// — khác <see cref="VerifyChain"/> (dùng cho cửa sổ đuôi lúc boot, record đầu tiên của cửa sổ được
    /// coi là điểm neo vì có thể không phải genesis thật), verify toàn-file LUÔN có genesis thật ở đầu mỗi
    /// đoạn nên bắt buộc kiểm tra <c>seq=1</c>/<c>prev_hash=genesis</c> tại vị trí <paramref name="start"/>.
    /// </summary>
    private static ChainVerifyOutcome VerifySegment(IReadOnlyList<ParsedRecord> records, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            ParsedRecord current = records[i];
            JsonObject withoutHash = (JsonObject)current.Raw.DeepClone();
            withoutHash.Remove("hash");
            string recomputed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SortKeys(withoutHash).ToJsonString())));
            if (!string.Equals(recomputed, current.Hash, StringComparison.Ordinal))
            {
                return new ChainVerifyOutcome($"hash mismatch at seq={current.Seq}", current.Seq, i);
            }

            if (i == start)
            {
                if (current.Seq != 1 || current.PrevHash != AuditCheckpoint.GenesisHash)
                {
                    return new ChainVerifyOutcome($"segment does not start at genesis, seq={current.Seq}", current.Seq, i);
                }

                continue;
            }

            ParsedRecord previous = records[i - 1];
            if (current.Seq != previous.Seq + 1 || current.PrevHash != previous.Hash)
            {
                return new ChainVerifyOutcome($"chain linkage broken between seq={previous.Seq} and seq={current.Seq}", current.Seq, i);
            }
        }

        return new ChainVerifyOutcome(null, 0, -1);
    }

    private static long? BrokenAtSeqOf(JsonObject raw) => raw["detail"]?.AsObject() is JsonObject detail && detail["broken_at_seq"] is JsonNode node ? node.GetValue<long>() : null;

    private static string? EventType(JsonObject raw) => raw["event_type"]?.GetValue<string>();

    private readonly record struct ChainVerifyOutcome(string? Detail, long BrokenAtSeq, int BrokenIndex);

    /// <summary>
    /// `10-ui-architecture.md` mục 6.3 (`AuditLogQuery`, gap fix Đợt 7) — <paramref name="page"/>
    /// 0-based, mới nhất trước (quyết định implement, không phải yêu cầu spec tường minh — thứ tự
    /// khớp trải nghiệm "xem lịch sử gần đây" thông thường). Dùng chung <see cref="_writeLock"/> với
    /// <see cref="AppendAsync(string, object, CancellationToken)"/> — tránh đọc trúng dòng đang ghi dở.
    /// Dòng nào không parse được (JSON hỏng, race hiếm) bị bỏ qua thay vì ném lỗi cả trang (fail-secure
    /// nghiêng về phía vẫn hiển thị được các dòng còn lại, không phải về phía chặn UI).
    /// </summary>
    public async Task<(IReadOnlyList<AuditLogEntryRaw> Entries, bool HasMore)> ReadPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return ([], false);
            }

            string[] lines = await File.ReadAllLinesAsync(_path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            List<AuditLogEntryRaw> newestFirst = [];
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                AuditLogEntryRaw? entry = TryParseEntry(lines[i]);
                if (entry is not null)
                {
                    newestFirst.Add(entry);
                }
            }

            int skip = page * pageSize;
            List<AuditLogEntryRaw> pageEntries = newestFirst.Skip(skip).Take(pageSize).ToList();
            bool hasMore = skip + pageEntries.Count < newestFirst.Count;
            return (pageEntries, hasMore);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static AuditLogEntryRaw? TryParseEntry(string line)
    {
        try
        {
            JsonObject obj = JsonNode.Parse(line)!.AsObject();
            long seq = obj["seq"]!.GetValue<long>();
            long tsUnixMs = obj["ts_unix_ms"]!.GetValue<long>();
            string eventType = obj["event_type"]!.GetValue<string>();
            JsonObject? detail = obj["detail"] as JsonObject;
            string detailJson = detail?.ToJsonString() ?? "{}";

            string processName = "";
            float riskScore = 0f;
            if (eventType == "ContentBlocked" && detail is not null)
            {
                processName = detail["processName"]?.GetValue<string>() ?? "";
                riskScore = detail["riskScore"]?.GetValue<float>() ?? 0f;
            }

            return new AuditLogEntryRaw(seq, tsUnixMs, eventType, detailJson, processName, riskScore);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private sealed record ParsedRecord(long Seq, string ChainId, string PrevHash, string Hash, JsonObject Raw);
}

/// <summary>1 dòng `audit.log` đã parse (`AuditLogWriter.ReadPageAsync`) — <see cref="ProcessName"/>/<see cref="RiskScore"/> chỉ có ý nghĩa khi <see cref="EventType"/>="ContentBlocked" (`04-data-architecture.md` mục 5.1).</summary>
public sealed record AuditLogEntryRaw(long Seq, long TsUnixMs, string EventType, string DetailJson, string ProcessName, float RiskScore);

/// <summary>Kết quả <see cref="AuditLogWriter.VerifyFullChainAsync"/> (`04-data-architecture.md` mục 5.3a) — <see cref="BrokenAtSeq"/>=0 khi <see cref="IsIntact"/>=true.</summary>
public sealed record AuditChainVerifyResult(bool IsIntact, long TotalRecordsScanned, long BrokenAtSeq, long VerifiedAtUnixMs);

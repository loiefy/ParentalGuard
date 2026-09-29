using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Config;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

/// <summary>Gap fix Đợt 7 (`10-ui-architecture.md` mục 6.3) — chỉ test qua <see cref="AuditLogCoordinator.HandleAsync"/> (production path).</summary>
public class AuditLogCoordinatorTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-audit-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-audit-{Guid.NewGuid():N}.log");
    private readonly string _configDbPath = Path.Combine(Path.GetTempPath(), $"pg-config-audit-{Guid.NewGuid():N}.db");

    private sealed record Fixture(AuditLogCoordinator AuditCoordinator, AuthCoordinator Auth, AuditLogWriter AuditLog, FakeMonotonicClock Clock, MonitoringStateHolder Holder);

    private async Task<Fixture> CreateAsync()
    {
        MonitoringStateData state = MonitoringStateData.CreateFirstRunDefault();
        ConfigDb.CreateFresh(_configDbPath, state, PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);

        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var authCoordinator = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);
        var holder = new MonitoringStateHolder(state);
        var configCoordinator = new ConfigCoordinator(holder, _configDbPath, authCoordinator, auditLog, () => { });
        var auditLogCoordinator = new AuditLogCoordinator(authCoordinator, auditLog, configCoordinator, clock, _configDbPath);

        return new Fixture(auditLogCoordinator, authCoordinator, auditLog, clock, holder);
    }

    private static async Task<byte[]> GetValidActionTokenAsync(AuthCoordinator auth, string actionContext)
    {
        IpcPayload setupResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 1, SetInitialPasswordReq = new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8("Passw0rd!") } }, CancellationToken.None);
        await auth.HandleAsync(
            new IpcPayload { MessageId = 2, ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = setupResponse.SetInitialPasswordResp.SetupToken, Confirmed = true } }, CancellationToken.None);
        IpcPayload verifyResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 3, AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFromUtf8("Passw0rd!"), ActionContext = actionContext } }, CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyResponse.AuthVerifyResp.Result);
        return verifyResponse.AuthVerifyResp.ActionToken.ToByteArray();
    }

    /// <summary>Audit fix 2026-09-29 (ADR-142) — mở gate <c>view_audit_log</c> cho 1 <see cref="AuditLogViewSession"/> cụ thể trước khi test <c>VerifyAuditChainRequest</c>, đúng luồng thật (S3 luôn query trang 0 trước khi hiện nút "Kiểm tra tính toàn vẹn").</summary>
    private static async Task OpenViewGateAsync(Fixture fx, AuditLogViewSession session)
    {
        byte[] token = await GetValidActionTokenAsync(fx.Auth, "view_audit_log");
        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 100, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.CopyFrom(token), Page = 0, PageSize = 50 } },
            session, CancellationToken.None);
        Assert.Equal(AuditLogQueryResult.Success, response.AuditLogResp.Result);
    }

    [Fact]
    public async Task AuditLogQuery_InvalidToken_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.CopyFrom([9, 9, 9]), Page = 0, PageSize = 50 } },
            new AuditLogViewSession(), CancellationToken.None);

        Assert.Equal(AuditLogQueryResult.InvalidToken, response.AuditLogResp.Result);
    }

    [Fact]
    public async Task AuditLogQuery_EmptyTokenWithoutPriorGate_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.Empty, Page = 0, PageSize = 50 } },
            new AuditLogViewSession(), CancellationToken.None);

        Assert.Equal(AuditLogQueryResult.InvalidToken, response.AuditLogResp.Result);
    }

    [Fact]
    public async Task AuditLogQuery_ValidToken_ReturnsSuccessAndOpensGateForNextPage()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth, "view_audit_log");
        var session = new AuditLogViewSession(); // 1 "kết nối pipe" duy nhất cho cả 2 trang dưới đây.

        IpcPayload firstPage = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.CopyFrom(token), Page = 0, PageSize = 50 } },
            session, CancellationToken.None);
        Assert.Equal(AuditLogQueryResult.Success, firstPage.AuditLogResp.Result);
        Assert.NotEmpty(firstPage.AuditLogResp.Entries); // ít nhất "ServiceStarted"

        // Trang kế tiếp, CÙNG session — token rỗng, đúng nguyên tắc "chỉ bắt buộc hợp lệ ở request đầu
        // tiên của 1 phiên xem".
        IpcPayload secondPage = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 2, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.Empty, Page = 1, PageSize = 50 } },
            session, CancellationToken.None);
        Assert.Equal(AuditLogQueryResult.Success, secondPage.AuditLogResp.Result);
    }

    /// <summary>
    /// Audit fix 2026-09-28 — regression test cho đúng lỗ hổng đã tìm thấy: gate mở ở 1 kết nối (session
    /// A) KHÔNG được rò rỉ sang 1 kết nối khác (session B, mô phỏng phụ huynh đóng app rồi ai đó khác mở
    /// lại UI trong cùng cửa sổ 10 phút). Test này PHẢI FAIL nếu lỡ revert về field service-wide cũ.
    /// </summary>
    [Fact]
    public async Task AuditLogQuery_EmptyTokenFromDifferentConnection_ReturnsInvalidToken_EvenWithinGateWindow()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth, "view_audit_log");
        var sessionA = new AuditLogViewSession();

        IpcPayload firstPage = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.CopyFrom(token), Page = 0, PageSize = 50 } },
            sessionA, CancellationToken.None);
        Assert.Equal(AuditLogQueryResult.Success, firstPage.AuditLogResp.Result);

        // "Đóng kết nối" (sessionA bị bỏ) rồi 1 kết nối MỚI (sessionB) gửi token rỗng ngay trong cửa sổ
        // 10 phút còn hiệu lực — đúng kịch bản PoC 2 auditor độc lập đã xác nhận bypass được trước khi sửa.
        var sessionB = new AuditLogViewSession();
        IpcPayload attackerResponse = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 2, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.Empty, Page = 0, PageSize = 50 } },
            sessionB, CancellationToken.None);

        Assert.Equal(AuditLogQueryResult.InvalidToken, attackerResponse.AuditLogResp.Result);
    }

    [Fact]
    public async Task AuditLogQuery_EmptyTokenAfterGateWindowExpires_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth, "view_audit_log");
        var session = new AuditLogViewSession();
        await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.CopyFrom(token), Page = 0, PageSize = 50 } },
            session, CancellationToken.None);

        fx.Clock.Now += (long)TimeSpan.FromMinutes(11).TotalMilliseconds; // > cửa sổ tái sử dụng 10 phút

        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 2, AuditLogQuery = new AuditLogQuery { ActionToken = ByteString.Empty, Page = 1, PageSize = 50 } },
            session, CancellationToken.None);

        Assert.Equal(AuditLogQueryResult.InvalidToken, response.AuditLogResp.Result);
    }

    [Fact]
    public async Task MarkFalsePositive_InvalidToken_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, MarkFalsePositiveReq = new MarkFalsePositiveRequest { ActionToken = ByteString.CopyFrom([1]), ProcessName = "chrome.exe" } },
            new AuditLogViewSession(), CancellationToken.None);

        Assert.Equal(MarkFalsePositiveResult.InvalidToken, response.MarkFalsePositiveResp.Result);
    }

    [Fact]
    public async Task MarkFalsePositive_ValidTokenNewEntry_AddsToWhitelistAndLogsConfigChanged()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth, "manage_whitelist");

        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, MarkFalsePositiveReq = new MarkFalsePositiveRequest { ActionToken = ByteString.CopyFrom(token), ProcessName = "chrome.exe" } },
            new AuditLogViewSession(), CancellationToken.None);

        Assert.Equal(MarkFalsePositiveResult.Success, response.MarkFalsePositiveResp.Result);
        Assert.Equal(["chrome.exe"], fx.Holder.Current.UserWhitelistedProcessNames);

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.Contains("\"event_type\":\"ConfigChanged\"", auditContent);
        Assert.Contains("chrome.exe", auditContent);
    }

    [Fact]
    public async Task MarkFalsePositive_AlreadyListed_ReturnsAlreadyListed()
    {
        Fixture fx = await CreateAsync();
        byte[] token1 = await GetValidActionTokenAsync(fx.Auth, "manage_whitelist");
        await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, MarkFalsePositiveReq = new MarkFalsePositiveRequest { ActionToken = ByteString.CopyFrom(token1), ProcessName = "chrome.exe" } },
            new AuditLogViewSession(), CancellationToken.None);

        byte[] token2 = await GetValidActionTokenAsync(fx.Auth, "manage_whitelist");
        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 2, MarkFalsePositiveReq = new MarkFalsePositiveRequest { ActionToken = ByteString.CopyFrom(token2), ProcessName = "chrome.exe" } },
            new AuditLogViewSession(), CancellationToken.None);

        Assert.Equal(MarkFalsePositiveResult.AlreadyListed, response.MarkFalsePositiveResp.Result);
    }

    [Fact]
    public async Task VerifyAuditChainRequest_IntactLog_ReturnsIsIntactAndPersistsLastFullVerifyAt()
    {
        Fixture fx = await CreateAsync();
        await fx.AuditLog.AppendAsync("MonitoringToggled", new { enabled = true }, CancellationToken.None);
        var session = new AuditLogViewSession();
        await OpenViewGateAsync(fx, session);

        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, VerifyAuditChainReq = new VerifyAuditChainRequest() },
            session, CancellationToken.None);

        Assert.Equal(VerifyAuditChainResult.Success, response.VerifyAuditChainResp.Result);
        Assert.True(response.VerifyAuditChainResp.IsIntact);
        Assert.Equal(0, response.VerifyAuditChainResp.BrokenAtSeq);
        // ServiceStarted (genesis) + MonitoringToggled + PasswordInitialSetup + AuthAttempt (cả 2 ghi bởi
        // OpenViewGateAsync/GetValidActionTokenAsync — AuditLogQuery bản thân KHÔNG ghi audit event, chỉ đọc).
        Assert.Equal(4, response.VerifyAuditChainResp.TotalRecordsScanned);

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        Assert.Equal(response.VerifyAuditChainResp.VerifiedAtUnixMs, db.ReadLastFullVerifyAtUnixMs());
    }

    /// <summary>Audit fix 2026-09-29 (ADR-142) — regression cho FAIL cứng do security-privacy-auditor phát hiện: endpoint này trước đây HOÀN TOÀN không gate (bất kỳ kết nối nào cũng đọc được kết quả verify mà không cần qua <c>AuthVerifyRequest</c>). Test này PHẢI FAIL nếu ai đó lỡ revert về code không kiểm tra <see cref="AuditLogViewSession.GateOpenUntilUnixMs"/>.</summary>
    [Fact]
    public async Task VerifyAuditChainRequest_SessionNeverPassedViewAuditLogGate_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, VerifyAuditChainReq = new VerifyAuditChainRequest() },
            new AuditLogViewSession(), CancellationToken.None);

        Assert.Equal(VerifyAuditChainResult.InvalidToken, response.VerifyAuditChainResp.Result);
    }

    /// <summary>Cùng lớp lỗi session-scoping đã sửa cho <c>AuditLogQuery</c> ở Đợt 7 — gate mở ở 1 kết nối (session A) không được rò rỉ sang kết nối khác (session B).</summary>
    [Fact]
    public async Task VerifyAuditChainRequest_DifferentConnectionSession_ReturnsInvalidToken_EvenAfterAnotherSessionPassedGate()
    {
        Fixture fx = await CreateAsync();
        var sessionA = new AuditLogViewSession();
        await OpenViewGateAsync(fx, sessionA);

        var sessionB = new AuditLogViewSession();
        IpcPayload response = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, VerifyAuditChainReq = new VerifyAuditChainRequest() },
            sessionB, CancellationToken.None);

        Assert.Equal(VerifyAuditChainResult.InvalidToken, response.VerifyAuditChainResp.Result);
    }

    [Fact]
    public async Task VerifyAuditChainRequest_TamperedRecord_ReturnsBrokenAtSeqAndAppendsAuditChainBrokenDetectedOnce()
    {
        Fixture fx = await CreateAsync();
        string[] lines = await File.ReadAllLinesAsync(_auditLogPath);
        lines[0] = lines[0].Replace("ServiceStarted", "ServiceStartedTampered", StringComparison.Ordinal);
        await File.WriteAllLinesAsync(_auditLogPath, lines);
        var session = new AuditLogViewSession();
        await OpenViewGateAsync(fx, session);

        IpcPayload first = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 1, VerifyAuditChainReq = new VerifyAuditChainRequest() },
            session, CancellationToken.None);
        Assert.Equal(VerifyAuditChainResult.Success, first.VerifyAuditChainResp.Result);
        Assert.False(first.VerifyAuditChainResp.IsIntact);
        Assert.Equal(1, first.VerifyAuditChainResp.BrokenAtSeq);

        string afterFirstVerify = await File.ReadAllTextAsync(_auditLogPath);
        int firstOccurrenceCount = CountOccurrences(afterFirstVerify, "AuditChainBrokenDetected");
        Assert.Equal(1, firstOccurrenceCount);

        // ADR-138: lần verify thứ 2 KHÔNG lặp lại record — điểm đứt đã có AuditChainBrokenDetected phía sau nó.
        IpcPayload second = await fx.AuditCoordinator.HandleAsync(
            new IpcPayload { MessageId = 2, VerifyAuditChainReq = new VerifyAuditChainRequest() },
            session, CancellationToken.None);
        Assert.False(second.VerifyAuditChainResp.IsIntact);

        string afterSecondVerify = await File.ReadAllTextAsync(_auditLogPath);
        Assert.Equal(1, CountOccurrences(afterSecondVerify, "AuditChainBrokenDetected"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    public void Dispose()
    {
        foreach (string path in new[] { _authDatPath, _auditLogPath, _configDbPath, _configDbPath + "-wal", _configDbPath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}

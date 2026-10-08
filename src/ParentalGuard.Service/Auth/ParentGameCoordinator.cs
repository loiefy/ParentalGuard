using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;

namespace ParentalGuard.Service.Auth;

/// <summary>
/// `PAUSE-044`–`PAUSE-048` (2026-10-08) — trò chơi nhảy rào "Bảo vệ cả phụ huynh", thay thử thách phép tính.
/// <list type="bullet">
/// <item>Tạm dừng: mật khẩu TRƯỚC (<c>action_token</c> "pause_monitoring" tiêu thụ ngay lúc bắt đầu ván, vì token chỉ sống 15 giây),
/// về đích → Service tự áp dụng tạm dừng với thời lượng đã chọn lúc bắt đầu. Vấp rào/thoát game → không tạm dừng.</item>
/// <item>Tắt chế độ / giảm độ khó: cần phiên phụ huynh + về đích ở độ khó HIỆN HÀNH.</item>
/// </list>
/// Trò chơi chạy ở UI (tiến trình người dùng) nên Service không tin kết quả mù quáng: chặn mọi kết quả về đích sớm hơn thời gian
/// tối thiểu để chạy hết quãng đường theo đúng đường pace của trò chơi (`PAUSE-045c`: 6 → 3 phút/km trong 500 m đầu, sau đó
/// 3 phút/km) — 800 m = 189 giây với mọi độ khó. PHẢI khớp <c>HurdleGameEngine.SecondsToRun</c> phía UI.
/// Ván chơi gắn với ĐÚNG kết nối pipe (<see cref="UiParentSession"/>), mỗi ván chỉ kết thúc được 1 lần.
/// </summary>
public sealed class ParentGameCoordinator(
    MonotonicClock clock,
    AuditLogWriter auditLog,
    Func<bool> protectionEnabled,
    Func<uint> currentGameMeters,
    Func<ByteString, CancellationToken, Task<bool>> consumePauseToken,
    Func<bool> isPaused,
    Func<PauseDuration, CancellationToken, Task<(PauseResult Result, long ExpiresAtUnixMs)>> applyPause,
    Func<bool, uint, CancellationToken, Task<bool>> applySettings)
{
    public const double StartPaceSecondsPerKm = 360;
    public const double EndPaceSecondsPerKm = 180;
    public const double PaceRampMeters = 500;

    /// <summary>Dung sai đồng hồ giữa UI và Service khi kiểm tra thời gian tối thiểu.</summary>
    public static readonly TimeSpan TimingTolerance = TimeSpan.FromSeconds(1.5);

    /// <summary>Ván chưa kết thúc sau ngần này (ngoài thời gian chạy) thì bỏ — chống giữ ván treo vô hạn.</summary>
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(20);

    /// <summary>`PAUSE-045c` (2026-10-08, chủ dự án giảm độ khó): Dễ 800 m / Vừa 1.600 m / Khó 2.000 m.</summary>
    public static readonly IReadOnlyList<uint> AllowedMeters = [800, 1600, 2000];

    /// <summary>Đổi giá trị đã lưu theo bộ mốc cũ (1000/2000/3000) sang mốc mới cùng mức; giá trị lạ → mặc định.</summary>
    /// Lưu ý: 2000 vừa là mốc "Vừa" cũ vừa là mốc "Khó" mới — giữ nguyên 2000 (an toàn: không làm giảm độ khó).
    public static uint NormalizeMeters(uint meters) => meters switch
    {
        800 or 1600 or 2000 => meters,
        1000 => 800,
        3000 => 2000,
        _ => 800,
    };

    private readonly IpcMessageIdGenerator _messageIds = new();

    public static long MinDurationMs(uint meters) =>
        (long)((SecondsToRun(meters) * 1000) - TimingTolerance.TotalMilliseconds);

    /// <summary>Tích phân pace theo quãng đường: pace giảm tuyến tính 360 → 180 giây/km trong 500 m đầu, sau đó 180 giây/km.</summary>
    public static double SecondsToRun(double meters)
    {
        double ramp = Math.Min(meters, PaceRampMeters);
        double slope = (StartPaceSecondsPerKm - EndPaceSecondsPerKm) / PaceRampMeters / 1000;
        double rampSeconds = (StartPaceSecondsPerKm / 1000 * ramp) - (slope * ramp * ramp / 2);
        return rampSeconds + (Math.Max(0, meters - PaceRampMeters) * EndPaceSecondsPerKm / 1000);
    }

    public Task<IpcPayload> HandleAsync(IpcPayload request, UiParentSession session, CancellationToken cancellationToken) => request.BodyCase switch
    {
        IpcPayload.BodyOneofCase.ParentGameStartReq => StartAsync(request, session, cancellationToken),
        IpcPayload.BodyOneofCase.ParentGameFinishReq => FinishAsync(request, session, cancellationToken),
        _ => throw new InvalidOperationException($"ParentGameCoordinator received unexpected message: {request.BodyCase}."),
    };

    private async Task<IpcPayload> StartAsync(IpcPayload request, UiParentSession session, CancellationToken cancellationToken)
    {
        ParentGameStartRequest req = request.ParentGameStartReq;
        IpcPayload response = NewResponse(request);
        ParentGameResult result = await ValidateStartAsync(req, session, cancellationToken).ConfigureAwait(false);
        if (result != ParentGameResult.Started)
        {
            response.ParentGameStartResp = new ParentGameStartResponse { Result = result };
            return response;
        }

        uint meters = currentGameMeters();
        long now = clock.UtcNowUnixMs;
        ulong seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        session.SetPendingGame(new PendingParentGame(req.Purpose, req.Duration, req.SettingsEnabled, req.SettingsGameMeters, meters, now));
        await auditLog.AppendAsync("ParentGameStarted", new { purpose = req.Purpose.ToString(), meters }, CancellationToken.None).ConfigureAwait(false);

        response.ParentGameStartResp = new ParentGameStartResponse
        {
            Result = ParentGameResult.Started,
            TargetMeters = meters,
            Seed = seed,
            MinDurationMs = (uint)MinDurationMs(meters),
        };
        return response;
    }

    private async Task<ParentGameResult> ValidateStartAsync(ParentGameStartRequest req, UiParentSession session, CancellationToken cancellationToken)
    {
        if (!protectionEnabled())
        {
            return ParentGameResult.NotRequired;
        }

        switch (req.Purpose)
        {
            case ParentGamePurpose.Pause:
                // Token tiêu thụ TRƯỚC mọi kiểm tra khác — mật khẩu sai/hết hạn thì không được chơi.
                if (!await consumePauseToken(req.ActionToken, cancellationToken).ConfigureAwait(false))
                {
                    return ParentGameResult.InvalidToken;
                }

                return isPaused() ? ParentGameResult.AlreadyPaused : ParentGameResult.Started;

            case ParentGamePurpose.Settings:
                if (!session.TryTouch())
                {
                    return ParentGameResult.NotAuthenticated;
                }

                return NeedsGame(req.SettingsEnabled, req.SettingsGameMeters, currentGameMeters())
                    ? ParentGameResult.Started
                    : ParentGameResult.NotRequired;

            default:
                return ParentGameResult.Failed;
        }
    }

    /// <summary>`PAUSE-047`: chỉ TẮT chế độ hoặc GIẢM quãng đường mới phải chơi (bật/tăng độ khó không làm giảm bảo vệ).</summary>
    public static bool NeedsGame(bool wantEnabled, uint wantMeters, uint currentMeters) =>
        !wantEnabled || (wantMeters != 0 && wantMeters < currentMeters);

    private async Task<IpcPayload> FinishAsync(IpcPayload request, UiParentSession session, CancellationToken cancellationToken)
    {
        ParentGameFinishRequest req = request.ParentGameFinishReq;
        IpcPayload response = NewResponse(request);
        var resp = new ParentGameFinishResponse();
        response.ParentGameFinishResp = resp;

        long now = clock.UtcNowUnixMs;
        if (session.TakePendingGame() is not { } game
            || now - game.StartedAtUnixMs > MinDurationMs(game.TargetMeters) + (long)AbandonAfter.TotalMilliseconds)
        {
            resp.Result = ParentGameResult.NoGame;
            return response;
        }

        long elapsedMs = now - game.StartedAtUnixMs;
        if (!req.Completed || req.MetersReached < game.TargetMeters)
        {
            resp.Result = ParentGameResult.Lost;
            await AuditAsync("ParentGameFailed", game, req.MetersReached, elapsedMs, "lost").ConfigureAwait(false);
            return response;
        }

        if (elapsedMs < MinDurationMs(game.TargetMeters))
        {
            resp.Result = ParentGameResult.TooFast;
            await AuditAsync("ParentGameFailed", game, req.MetersReached, elapsedMs, "too_fast").ConfigureAwait(false);
            return response;
        }

        await AuditAsync("ParentGamePassed", game, req.MetersReached, elapsedMs, null).ConfigureAwait(false);
        if (game.Purpose == ParentGamePurpose.Pause)
        {
            (PauseResult pauseResult, long expiresAt) = await applyPause(game.Duration, cancellationToken).ConfigureAwait(false);
            resp.Result = pauseResult switch
            {
                PauseResult.Success => ParentGameResult.Paused,
                PauseResult.AlreadyPaused => ParentGameResult.AlreadyPaused,
                _ => ParentGameResult.Failed,
            };
            resp.PauseExpiresAtUnixMs = expiresAt;
            return response;
        }

        resp.Result = await applySettings(game.SettingsEnabled, game.SettingsGameMeters, cancellationToken).ConfigureAwait(false)
            ? ParentGameResult.Applied
            : ParentGameResult.Failed;
        return response;
    }

    private Task AuditAsync(string eventType, PendingParentGame game, uint metersReached, long elapsedMs, string? reason) =>
        auditLog.AppendAsync(
            eventType,
            new { purpose = game.Purpose.ToString(), target_meters = game.TargetMeters, meters_reached = metersReached, elapsed_ms = elapsedMs, reason },
            CancellationToken.None);

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);
}

/// <summary>1 ván đang chơi của 1 kết nối UI.</summary>
public sealed record PendingParentGame(
    ParentGamePurpose Purpose,
    PauseDuration Duration,
    bool SettingsEnabled,
    uint SettingsGameMeters,
    uint TargetMeters,
    long StartedAtUnixMs);

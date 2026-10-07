using System.Security.Cryptography;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Service.Auth;

/// <summary>
/// `PAUSE-041`/`PAUSE-042` (2026-10-07) — thử thách "Bảo vệ cả phụ huynh": <c>Service</c> sinh 5 phép tính, tự chấm (UI không
/// biết đáp án). Bộ đếm thất bại và thời điểm khoá dùng chung TOÀN Service (không theo kết nối) — mở lại Dashboard không
/// xoá được khoá. Kết quả "đã vượt qua" gắn với kết nối pipe hiện tại (<see cref="UiParentSession"/>), dùng đúng 1 lần.
/// </summary>
public sealed class ParentChallengeCoordinator(MonotonicClock clock)
{
    public const int QuestionCount = 5;
    public const int MaxFailuresBeforeLockout = 3;
    public static readonly TimeSpan AnswerTime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PassValidity = TimeSpan.FromMinutes(2);

    private readonly object _sync = new();
    private readonly IpcMessageIdGenerator _messageIds = new();
    private int _consecutiveFailures;
    private long _lockedUntilUnixMs;

    public Task<IpcPayload> HandleAsync(IpcPayload request, UiParentSession session) => Task.FromResult(request.BodyCase switch
    {
        IpcPayload.BodyOneofCase.ChallengeStartReq => Start(request, session),
        IpcPayload.BodyOneofCase.ChallengeSubmitReq => Submit(request, session),
        _ => throw new InvalidOperationException($"ParentChallengeCoordinator received unexpected message: {request.BodyCase}."),
    });

    private IpcPayload Start(IpcPayload request, UiParentSession session)
    {
        IpcPayload response = NewResponse(request);
        long now = clock.UtcNowUnixMs;
        lock (_sync)
        {
            if (now < _lockedUntilUnixMs)
            {
                response.ChallengeStartResp = new ChallengeStartResponse { Result = ChallengeResult.LockedOut, LockedUntilUnixMs = _lockedUntilUnixMs };
                return response;
            }
        }

        var questions = new string[QuestionCount];
        var answers = new int[QuestionCount];
        for (int i = 0; i < QuestionCount; i++)
        {
            (questions[i], answers[i]) = NextQuestion();
        }

        long expiresAt = now + (long)AnswerTime.TotalMilliseconds;
        session.SetPendingChallenge(answers, expiresAt);
        var resp = new ChallengeStartResponse { Result = ChallengeResult.Success, ExpiresAtUnixMs = expiresAt };
        resp.Questions.AddRange(questions);
        response.ChallengeStartResp = resp;
        return response;
    }

    private IpcPayload Submit(IpcPayload request, UiParentSession session)
    {
        IpcPayload response = NewResponse(request);
        long now = clock.UtcNowUnixMs;
        var resp = new ChallengeSubmitResponse();
        lock (_sync)
        {
            if (now < _lockedUntilUnixMs)
            {
                resp.Result = ChallengeResult.LockedOut;
                resp.LockedUntilUnixMs = _lockedUntilUnixMs;
            }
            else if (session.TakePendingChallenge() is not { } pending)
            {
                resp.Result = ChallengeResult.NoChallenge;
            }
            else
            {
                bool inTime = now <= pending.ExpiresAtUnixMs;
                IList<int> given = request.ChallengeSubmitReq.Answers;
                bool allCorrect = inTime && given.Count == pending.Answers.Length && given.SequenceEqual(pending.Answers);
                if (allCorrect)
                {
                    _consecutiveFailures = 0;
                    session.MarkChallengePassed(now + (long)PassValidity.TotalMilliseconds);
                    resp.Result = ChallengeResult.Passed;
                }
                else
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= MaxFailuresBeforeLockout)
                    {
                        _consecutiveFailures = 0;
                        _lockedUntilUnixMs = now + (long)LockoutDuration.TotalMilliseconds;
                        resp.Result = ChallengeResult.LockedOut;
                        resp.LockedUntilUnixMs = _lockedUntilUnixMs;
                    }
                    else
                    {
                        resp.Result = inTime ? ChallengeResult.Wrong : ChallengeResult.Expired;
                        resp.FailuresBeforeLockout = (uint)(MaxFailuresBeforeLockout - _consecutiveFailures);
                    }
                }
            }
        }

        response.ChallengeSubmitResp = resp;
        return response;
    }

    /// <summary>Cộng/trừ/nhân với số 1–2 chữ số; phép trừ luôn ra số không âm, phép nhân giữ đáp án vừa phải.</summary>
    internal static (string Question, int Answer) NextQuestion()
    {
        switch (RandomNumberGenerator.GetInt32(3))
        {
            case 0:
                {
                    int a = RandomNumberGenerator.GetInt32(10, 100);
                    int b = RandomNumberGenerator.GetInt32(10, 100);
                    return ($"{a} + {b}", a + b);
                }

            case 1:
                {
                    int a = RandomNumberGenerator.GetInt32(20, 100);
                    int b = RandomNumberGenerator.GetInt32(10, a);
                    return ($"{a} - {b}", a - b);
                }

            default:
                {
                    int a = RandomNumberGenerator.GetInt32(3, 13);
                    int b = RandomNumberGenerator.GetInt32(3, 13);
                    return ($"{a} × {b}", a * b);
                }
        }
    }

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);
}

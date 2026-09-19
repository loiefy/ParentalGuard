using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Nguồn sự thật cho trạng thái icon (`FE-021`, Architecture/07-overlay-architecture.md mục 4.1.2,
/// ADR-65): ánh xạ trực tiếp từ state machine trung tâm sang <see cref="IconState"/>, đẩy
/// <c>MonitoringStatusUpdate</c> xuống Overlay mỗi khi đổi, đúng nguyên tắc "Service là nguồn sự
/// thật duy nhất, Overlay không tự suy luận" (ADR-12).
/// </summary>
public sealed class IconStatusCoordinator(ChildProcessSupervisor overlaySupervisor)
{
    private readonly object _sync = new();

    // Mặc định ERROR (Starting) đúng bảng ánh xạ mục 4.1.2 — chuyển ACTIVE khi kênh Vision hoặc
    // Overlay kết nối thành công lần đầu (Worker.cs wiring).
    private IconState _state = IconState.Error;
    private long _pauseExpiresAtUnixMs;

    public void SetState(IconState state, long pauseExpiresAtUnixMs = 0)
    {
        lock (_sync)
        {
            if (_state == state && _pauseExpiresAtUnixMs == pauseExpiresAtUnixMs)
            {
                return;
            }

            _state = state;
            _pauseExpiresAtUnixMs = pauseExpiresAtUnixMs;
        }

        overlaySupervisor.TryEnqueueBusinessMessage(payload => payload.MonitoringStatus = Build());
    }

    /// <summary>Dùng làm 1 trong các <c>initialPushBuilders</c> khi Overlay (re)connect (Architecture/03 mục 4.3).</summary>
    public void ConfigureInitialPush(IpcPayload payload) => payload.MonitoringStatus = Build();

    private MonitoringStatusUpdate Build()
    {
        lock (_sync)
        {
            return new MonitoringStatusUpdate
            {
                State = _state,
                PauseExpiresAtUnixMs = _pauseExpiresAtUnixMs,
                GeneratedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
        }
    }
}

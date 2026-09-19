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

    // ANTI-060/061 (Architecture/09 mục 6.2, ADR-100): khi banner đang bật, GIỮ NGUYÊN ERROR bất kể
    // SetState gọi gì — "attack window" phải đỏ liên tục, không được ACTIVE thoáng qua giữa các lần
    // Vision/Overlay respawn dồn dập. _state vẫn được cập nhật (nguồn sự thật thật sự) để khi banner
    // tắt, hiển thị đúng trạng thái hiện hành thay vì đông cứng ở giá trị lúc banner bật.
    private bool _attackBannerActive;

    public void SetState(IconState state, long pauseExpiresAtUnixMs = 0)
    {
        IconState effective;
        long effectivePause;
        lock (_sync)
        {
            if (_state == state && _pauseExpiresAtUnixMs == pauseExpiresAtUnixMs)
            {
                return;
            }

            _state = state;
            _pauseExpiresAtUnixMs = pauseExpiresAtUnixMs;
            if (_attackBannerActive)
            {
                return; // ADR-100 — không đẩy state "bình thường" trong lúc banner đang giữ ERROR.
            }

            effective = _state;
            effectivePause = _pauseExpiresAtUnixMs;
        }

        overlaySupervisor.TryEnqueueBusinessMessage(payload => payload.MonitoringStatus = Build(effective, effectivePause));
    }

    /// <summary>ANTI-061 (mục 6.2): bật/tắt banner — không tạo message/schema IPC mới, tái dùng nguyên <see cref="IconState.Error"/>.</summary>
    public void SetAttackBannerActive(bool active)
    {
        IconState effective;
        long effectivePause;
        lock (_sync)
        {
            if (_attackBannerActive == active)
            {
                return;
            }

            _attackBannerActive = active;
            effective = active ? IconState.Error : _state;
            effectivePause = active ? 0 : _pauseExpiresAtUnixMs;
        }

        overlaySupervisor.TryEnqueueBusinessMessage(payload => payload.MonitoringStatus = Build(effective, effectivePause));
    }

    /// <summary>Dùng làm 1 trong các <c>initialPushBuilders</c> khi Overlay (re)connect (Architecture/03 mục 4.3).</summary>
    public void ConfigureInitialPush(IpcPayload payload)
    {
        lock (_sync)
        {
            payload.MonitoringStatus = Build(_attackBannerActive ? IconState.Error : _state, _attackBannerActive ? 0 : _pauseExpiresAtUnixMs);
        }
    }

    private static MonitoringStatusUpdate Build(IconState state, long pauseExpiresAtUnixMs)
    {
        return new MonitoringStatusUpdate
        {
            State = state,
            PauseExpiresAtUnixMs = pauseExpiresAtUnixMs,
            GeneratedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }
}
